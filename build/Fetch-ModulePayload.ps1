[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ModuleId,
  [Parameter(Mandatory)][string]$DestinationDirectory,
  [string]$PayloadPath,
  [string]$CacheDirectory
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$module = Get-ModuleById -ModuleId $ModuleId
$moduleDirectory = Resolve-ModuleDirectory -Module $module
$payloadFile = Join-Path $moduleDirectory 'payload.json'
if (-not (Test-Path -LiteralPath $payloadFile)) {
    throw "进程插件 '$ModuleId' 缺少 payload.json，无法确定 payload 来源。"
}

$payload = Read-JsonFile -Path $payloadFile
foreach ($field in @('repository', 'releaseTag', 'assetName', 'sha256', 'extractTo')) {
    if ([string]::IsNullOrWhiteSpace([string]$payload.$field)) {
        throw "payload.json 缺少字段 '$field'。"
    }
}

# 哈希是这条链路唯一的信任锚：它同时约束"下载到的东西"和"打进包的东西"。
$expected = ([string]$payload.sha256).Trim().ToLowerInvariant()
if ($expected -notmatch '^[a-f0-9]{64}$') {
    throw "payload.json 的 sha256 必须是 64 位小写十六进制。"
}

if (-not $CacheDirectory) {
    $CacheDirectory = if ($env:STARPIE_PAYLOAD_CACHE) { $env:STARPIE_PAYLOAD_CACHE } else { Join-Path ([System.IO.Path]::GetTempPath()) 'starpie-payload-cache' }
}
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$cached = Join-Path $CacheDirectory $expected

if ($PayloadPath) {
    # 离线/调试路径：直接用本地已构建的产物，但仍要过哈希这一关，
    # 否则"本地能过、CI 挂掉"这种差异会一直藏着。
    if (-not (Test-Path -LiteralPath $PayloadPath)) { throw "本地 payload 不存在：$PayloadPath" }
    $actual = Get-Sha256Hex -Path $PayloadPath
    if ($actual -ne $expected) {
        throw "本地 payload 的 SHA-256 与 payload.json 不一致：$actual != $expected"
    }
    Copy-Item -LiteralPath $PayloadPath -Destination $cached -Force
}
elseif (Test-Path -LiteralPath $cached) {
    Write-Host "payload cache hit: $expected"
}
else {
    $url = "https://github.com/$($payload.repository)/releases/download/$($payload.releaseTag)/$($payload.assetName)"
    Write-Host "downloading payload: $url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $cached -UseBasicParsing -ErrorAction Stop
    $actual = Get-Sha256Hex -Path $cached
    if ($actual -ne $expected) {
        Remove-Item -LiteralPath $cached -Force -ErrorAction SilentlyContinue
        throw "下载的 payload SHA-256 与 payload.json 不一致：$actual != $expected"
    }
}

$destination = Join-Path $DestinationDirectory ($payload.extractTo -replace '/', '\')
$destinationDirectory = Split-Path -Parent $destination
if ($destinationDirectory) { New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null }
Copy-Item -LiteralPath $cached -Destination $destination -Force

Write-Host "payload ready: $destination ($expected)"
