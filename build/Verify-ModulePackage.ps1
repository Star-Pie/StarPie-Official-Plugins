[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$PackagePath,
  [Parameter(Mandatory)][string]$ModuleId
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$module = Get-ModuleById -ModuleId $ModuleId
# 进程插件没有入口程序集：校验从「单程序集哈希」改成「逐文件核对 files 清单」。
$runtime = if ($module.runtime) { [string]$module.runtime } else { 'assembly' }

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Package not found: $PackagePath"
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('starpie-spkg-verify-' + [Guid]::NewGuid().ToString('N'))
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $temp)

    $pluginJson = Join-Path $temp 'plugin.json'
    $moduleManifest = Join-Path $temp 'module.manifest.json'
    $assemblyPath = if ($module.assembly) { Join-Path $temp $module.assembly } else { $null }

    $requiredEntries = if ($runtime -eq 'process') { @($pluginJson, $moduleManifest) } else { @($pluginJson, $moduleManifest, $assemblyPath) }
    foreach ($path in $requiredEntries) {
        if ([string]::IsNullOrWhiteSpace([string]$path) -or -not (Test-Path -LiteralPath $path)) {
            throw "Package is missing required entry: $path"
        }
    }

    $plugin = Read-JsonFile -Path $pluginJson
    $manifest = Read-JsonFile -Path $moduleManifest

    if ($plugin.id -ne $module.pluginId) { throw "plugin.json id mismatch: $($plugin.id) != $($module.pluginId)" }
    if ($plugin.version -ne $module.version) { throw "plugin.json version mismatch: $($plugin.version) != $($module.version)" }
    if ($manifest.id -ne $module.id) { throw "module.manifest.json id mismatch: $($manifest.id) != $($module.id)" }
    if ($manifest.version -ne $module.version) { throw "module.manifest.json version mismatch: $($manifest.version) != $($module.version)" }

    if ($runtime -eq 'process') {
        if (-not $manifest.runtime -or [string]$manifest.runtime -ne 'process') {
            throw 'module.manifest.json 未声明 runtime=process。'
        }
        $files = @($manifest.files)
        if ($files.Count -eq 0) {
            throw '进程插件包缺少 files 清单，无法做完整性校验。'
        }

        $declared = @{}
        foreach ($entry in $files) {
            $relative = [string]$entry.path
            if ([string]::IsNullOrWhiteSpace($relative)) { throw 'files 中存在空路径。' }
            $absolute = Join-Path $temp ($relative -replace '/', '\')
            if (-not (Test-Path -LiteralPath $absolute)) {
                throw "files 声明了包里不存在的文件：$relative"
            }
            $actualHash = Get-Sha256Hex -Path $absolute
            if ($actualHash -ne [string]$entry.sha256) {
                throw "文件哈希与 module.manifest.json 不符：$relative"
            }
            $declared[$relative] = $true
        }

        # 反向校验同样重要：包里多出一个没被哈希覆盖的文件，等于这段完整性检查有洞。
        $found = @(Get-ChildItem -LiteralPath $temp -Recurse -File)
        foreach ($file in $found) {
            $relative = $file.FullName.Substring($temp.Length).TrimStart('\') -replace '\\', '/'
            if ($relative -eq 'module.manifest.json') { continue }
            if (-not $declared.ContainsKey($relative)) {
                throw "包里有 files 未声明的文件：$relative"
            }
        }
    }
    else {
        if ($plugin.assembly -ne $module.assembly) { throw "plugin.json assembly mismatch: $($plugin.assembly) != $($module.assembly)" }
        if ((Get-Sha256Hex -Path $assemblyPath) -ne $manifest.assemblySha256) {
            throw 'Assembly SHA-256 does not match module.manifest.json.'
        }
    }

    if (Test-Path -LiteralPath (Join-Path $temp 'StarPie.Plugin.Abstractions.dll')) {
        throw 'Package must not contain StarPie.Plugin.Abstractions.dll.'
    }

    Write-Host "Package verification passed ($runtime): $PackagePath"
}
finally {
    if ([System.IO.Directory]::Exists($temp)) {
        [System.IO.Directory]::Delete($temp, $true)
    }
}
