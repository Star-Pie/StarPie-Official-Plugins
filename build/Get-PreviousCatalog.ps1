[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('stable','beta')][string]$Channel,
    [Parameter(Mandatory)][string]$Repository,
    [string]$OutputDirectory = 'artifacts\previous-catalog'
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Catalog.psm1') -Force
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$catalogPath=Join-Path $outputRoot 'module-catalog.json'
$releasesJson=gh release list --repo $Repository --exclude-drafts --limit 1000 --json tagName,isPrerelease,publishedAt,createdAt
$latest=$null; $history=@{}
if($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($releasesJson)){
    foreach($release in @($releasesJson | ConvertFrom-Json | Sort-Object publishedAt -Descending)){
        $tag=[string]$release.tagName;if(-not $tag){continue}
        $rawDir=Join-Path $outputRoot ('raw-'+[guid]::NewGuid().ToString('N'))
        gh release download $tag --repo $Repository --pattern 'module-catalog.json' --dir $rawDir --clobber 2>$null
        $rawPath=Join-Path $rawDir 'module-catalog.json'
        if($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $rawPath)){continue}
        try{$catalog=Get-Content -LiteralPath $rawPath -Raw | ConvertFrom-Json -Depth 100}catch{Write-Warning "Skipping invalid catalog in $tag";continue}
        if($catalog.releaseTag -ne $tag -or $catalog.releaseChannel -ne $Channel -or $catalog.schemaVersion -notin @(1,2)){continue}
        if($null -eq $latest){
            $latest=$catalog
            # v2 已携带累积历史，不必重复下载全部发布目录。
            if($catalog.schemaVersion -eq 2){Copy-Item -LiteralPath $rawPath -Destination $catalogPath -Force;Write-Output $catalogPath;exit 0}
        }
        foreach($group in @($catalog.modules)){
            if(-not $history.ContainsKey($group.id)){$history[$group.id]=[Collections.Generic.List[object]]::new()}
            foreach($version in @(Get-CatalogVersions $group)){
                $same=@($history[$group.id] | Where-Object version -CEQ $version.version)
                if($same.Count -gt 0){
                    if($same[0].sha256 -ine $version.sha256 -or [long]$same[0].size -ne [long]$version.size){throw "Historical version was overwritten: $($group.id) $($version.version)"}
                    continue
                }
                $record=[ordered]@{};foreach($property in $version.PSObject.Properties){if($property.Name -ne 'id'){$record[$property.Name]=$property.Value}}
                $history[$group.id].Add([pscustomobject]$record)
            }
        }
    }
}
if($null -ne $latest){
    # 一次性迁移：回溯同通道 v1 目录，保留已被旧单版本索引替换掉的历史记录。
    $latest.schemaVersion=2
    $latest.modules=@($history.Keys | Sort-Object | ForEach-Object {[pscustomobject]@{id=$_;versions=@($history[$_].ToArray())}})
    [IO.File]::WriteAllText($catalogPath,($latest | ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
    Write-Output $catalogPath;exit 0
}
$bootstrap='catalog\module-catalog.json'
if(Test-Path -LiteralPath $bootstrap){
    try{$catalog=Get-Content -LiteralPath $bootstrap -Raw | ConvertFrom-Json -Depth 100}catch{$catalog=$null}
    if($null -ne $catalog -and $catalog.releaseChannel -eq $Channel -and $catalog.schemaVersion -in @(1,2)){
        Copy-Item -LiteralPath $bootstrap -Destination $catalogPath -Force;Write-Output $catalogPath;exit 0
    }
}
Write-Output ''
