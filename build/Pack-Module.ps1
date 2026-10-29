[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ModuleId,
  [string]$Configuration = 'Release',
  [string]$OutputDirectory = 'artifacts/packages',
  [string]$ReleaseTag = 'local',
  [string]$OutputMetadataPath,
  [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$root = Get-RepositoryRoot
$registry = Get-ModuleRegistry
$module = Get-ModuleById -ModuleId $ModuleId -Registry $registry
$projectDirectory = Resolve-ModuleDirectory -Module $module
$buildOutputDirectory = Resolve-ModuleOutputDirectory -Module $module -Configuration $Configuration
$runtime = if ($module.runtime) { [string]$module.runtime } else { 'assembly' }
$assemblyPath = if ($runtime -eq 'assembly') { Join-Path $buildOutputDirectory $module.assembly } else { $null }

if ($runtime -eq 'assembly' -and -not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Module assembly not found after build: $assemblyPath. Build module '$ModuleId' first."
}
if ($runtime -eq 'process') {
    # payload 不随源码分发：由 payload.json 描述来源（仓库 + Release 标签 + sha256），
    # 打包前取到输出目录。放在这里而不是 CI 步骤里，是为了让本地单独跑 Pack-Module 也自足。
    $payloadDescriptor = Join-Path (Resolve-ModuleDirectory -Module $module) 'payload.json'
    if (Test-Path -LiteralPath $payloadDescriptor) {
        & (Join-Path $PSScriptRoot 'Fetch-ModulePayload.ps1') -ModuleId $ModuleId -DestinationDirectory $buildOutputDirectory
    }

    # 进程插件没有程序集可校验，改成要求「构建产物里必须真有那个可执行文件」——
    # 否则打出来的包能装、一触发就报"文件不存在"，而错误发生在用户那边。
    $command0 = @($module.command) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$command0)) { throw "Module '$ModuleId' declares runtime=process but the registry entry has no command." }
    $payloadPath = Join-Path $buildOutputDirectory ([string]$command0)
    if (-not (Test-Path -LiteralPath $payloadPath)) { throw "Process plugin payload not found after build: $payloadPath. Build module '$ModuleId' first." }
}

$packageOutputDirectory = Join-Path $root $OutputDirectory
if (-not [System.IO.Path]::IsPathRooted($packageOutputDirectory)) {
    $packageOutputDirectory = [System.IO.Path]::GetFullPath($packageOutputDirectory)
}
if ($Clean -and (Test-Path -LiteralPath $packageOutputDirectory)) {
    Remove-Item -LiteralPath $packageOutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $packageOutputDirectory -Force | Out-Null

$stagingRoot = Join-Path $root ('artifacts/staging/' + $module.id + '/' + $module.version)
if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$excludeNames = @(
    'StarPie.Plugin.Abstractions.dll',
    'StarPie.dll'
)
$excludePatterns = @(
    '*.pdb',
    '*.xml',
    '*.deps.json',
    '*.runtimeconfig.json'
)

function Copy-OutputPayload {
    param([string]$SourceRoot,[string]$DestinationRoot,[string]$RelativePath)

    $source = Join-Path $SourceRoot $RelativePath
    $destination = Join-Path $DestinationRoot $RelativePath
    $directory = Split-Path -Parent $destination
    if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

$allFiles = @(Get-ChildItem -LiteralPath $buildOutputDirectory -Recurse -File)
foreach ($file in $allFiles) {
    $relative = ConvertTo-NormalizedRelativePath -BasePath $buildOutputDirectory -Path $file.FullName
    $fileName = $file.Name
    # 进程插件的「打包工程」本身会产出一个空壳程序集（它只负责把 payload 复制到输出目录），
    # 那枚 dll 不该进包：装到插件目录里只会让宿主把它当成一个找不到入口的托管插件。
    # 注意判定依据是「工程文件名」而不是 $module.assembly —— 进程插件根本没有 assembly 字段。
    if ($runtime -eq 'process' -and $fileName -eq (([System.IO.Path]::GetFileNameWithoutExtension($module.project)) + '.dll')) { continue }
    if ($excludeNames -contains $fileName) { continue }
    $skip = $false
    foreach ($pattern in $excludePatterns) {
        if ($fileName -like $pattern) { $skip = $true; break }
    }
    if ($skip) { continue }
    if ($relative.StartsWith('ref/', [System.StringComparison]::OrdinalIgnoreCase)) { continue }

    Copy-OutputPayload -SourceRoot $buildOutputDirectory -DestinationRoot $stagingRoot -RelativePath $relative
}

$pluginJsonSource = Join-Path $projectDirectory 'plugin.json'
if (Test-Path -LiteralPath $pluginJsonSource) {
    Copy-Item -LiteralPath $pluginJsonSource -Destination (Join-Path $stagingRoot 'plugin.json') -Force
}
else {
    $claims = @()
    foreach ($claim in @($module.typeClaims)) {
        $claims += [ordered]@{
            typeName = $claim
            contributionId = $module.contributionId
        }
    }

    $pluginManifest = [ordered]@{
        schemaVersion = 1
        id = $module.pluginId
        name = $module.name
        description = if ($module.description) { $module.description } else { "$($module.name) official StarPie module" }
        author = if ($module.author) { $module.author } else { 'StarPie Studio' }
        homepage = if ($module.homepage) { $module.homepage } else { 'https://github.com/Star-Pie/StarPie' }
        license = if ($module.license) { $module.license } else { 'MIT' }
        version = $module.version
        apiVersion = if ($module.apiVersion) { $module.apiVersion } else { $registry.sdkApiVersion }
        minHostVersion = if ($module.minHostVersion) { $module.minHostVersion } else { $registry.minimumHostVersion }
        maxHostVersion = $module.maxHostVersion
        targetFramework = $module.targetFramework
        platform = 'win-x64'
        assembly = $module.assembly
        entryType = $module.entryType
        capabilities = @($module.capabilities)
        claimedTypes = $claims
        contributions = [ordered]@{
            actions = $true
            icons = $false
            i18n = $false
        }
        dependencies = @()
        icon = $module.icon
        tags = @($module.tags)
        features = @($module.features)
    }
    Write-JsonFile -Value $pluginManifest -Path (Join-Path $stagingRoot 'plugin.json')
}

# 完整性从"单程序集哈希"扩成"文件清单哈希"：进程插件没有入口程序集这个中心，
# 而程序集插件本来就带一堆依赖，只校验主程序集等于只校验了其中一枚文件。
$files = @(
    Get-ChildItem -LiteralPath $stagingRoot -Recurse -File |
        Where-Object { $_.Name -ne 'module.manifest.json' } |
        ForEach-Object {
            [ordered]@{
                path = ConvertTo-NormalizedRelativePath -BasePath $stagingRoot -Path $_.FullName
                sha256 = Get-Sha256Hex -Path $_.FullName
                size = [int64]$_.Length
            }
        } |
        Sort-Object { $_.path }
)

$moduleManifest = [ordered]@{
    schemaVersion = 1
    id = $module.id
    name = $module.name
    version = $module.version
    apiVersion = if ($module.apiVersion) { $module.apiVersion } else { $registry.sdkApiVersion }
    minHostVersion = if ($module.minHostVersion) { $module.minHostVersion } else { $registry.minimumHostVersion }
    maxHostVersion = $module.maxHostVersion
    runtime = $runtime
    targetFramework = $module.targetFramework
    typeClaims = @($module.typeClaims)
    capabilities = @($module.capabilities)
    releaseTag = $ReleaseTag
    files = $files
}

if ($runtime -eq 'assembly') {
    $assemblySha256 = Get-Sha256Hex -Path $assemblyPath
    $moduleManifest['assembly'] = $module.assembly
    $moduleManifest['assemblySha256'] = $assemblySha256
}
Write-JsonFile -Value $moduleManifest -Path (Join-Path $stagingRoot 'module.manifest.json')
# 进程插件没有程序集，包名退到打包工程的文件名（与既有 StarPie.Plugin.*-版本.spkg 的形态一致）。
$assetBase = if ($module.assembly) { [System.IO.Path]::GetFileNameWithoutExtension($module.assembly) } else { [System.IO.Path]::GetFileNameWithoutExtension($module.project) }
$assetName = "$assetBase-$($module.version).spkg"
$packagePath = Join-Path $packageOutputDirectory $assetName
New-DeterministicZip -SourceDirectory $stagingRoot -DestinationPath $packagePath

$sha256 = Get-Sha256Hex -Path $packagePath
$size = (Get-Item -LiteralPath $packagePath).Length
$package = [ordered]@{
    id = $module.id
    name = $module.name
    version = $module.version
    assetName = $assetName
    packagePath = $packagePath
    sha256 = $sha256
    size = $size
    apiVersion = if ($module.apiVersion) { $module.apiVersion } else { $registry.sdkApiVersion }
    minHostVersion = if ($module.minHostVersion) { $module.minHostVersion } else { $registry.minimumHostVersion }
    maxHostVersion = $module.maxHostVersion
    typeClaims = @($module.typeClaims)
    capabilities = @($module.capabilities)
    releaseTag = $ReleaseTag
}

if ($OutputMetadataPath) {
    Write-JsonFile -Value $package -Path $OutputMetadataPath
}

$package | ConvertTo-Json -Depth 100