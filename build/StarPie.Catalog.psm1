Set-StrictMode -Version Latest

function Get-CatalogProperty {
    param([object]$Value,[string]$Name,[object]$Default = $null)
    if ($null -ne $Value -and $null -ne $Value.PSObject.Properties[$Name]) { return $Value.$Name }
    return $Default
}

function ConvertTo-CatalogVersion {
    param([Parameter(Mandatory)][string]$Version)
    if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$') { throw "Invalid semantic version: $Version" }
    $pre = [string]$Matches[4]
    $result = [pscustomobject]@{ Major = [int]$Matches[1]; Minor = [int]$Matches[2]; Patch = [int]$Matches[3]; Pre = $pre }
    foreach ($part in @($pre -split '\.')) { if ($part -match '^[0-9]+$' -and $part.Length -gt 1 -and $part.StartsWith('0')) { throw "Invalid numeric prerelease: $Version" } }
    return $result
}

function Compare-CatalogVersions {
    param([Parameter(Mandatory)][string]$Left,[Parameter(Mandatory)][string]$Right)
    $a = ConvertTo-CatalogVersion $Left; $b = ConvertTo-CatalogVersion $Right
    foreach ($part in @('Major','Minor','Patch')) { $c = $a.$part.CompareTo($b.$part); if ($c -ne 0) { return $c } }
    if (-not $a.Pre -and -not $b.Pre) { return 0 }
    if (-not $a.Pre) { return 1 }; if (-not $b.Pre) { return -1 }
    $l = @($a.Pre -split '\.'); $r = @($b.Pre -split '\.')
    for ($i = 0; $i -lt [Math]::Min($l.Count,$r.Count); $i++) {
        $ln = $l[$i] -match '^[0-9]+$'; $rn = $r[$i] -match '^[0-9]+$'
        if ($ln -and $rn) { $c = $l[$i].Length.CompareTo($r[$i].Length); if ($c -eq 0) { $c = [string]::CompareOrdinal($l[$i],$r[$i]) } }
        elseif ($ln -ne $rn) { $c = if ($ln) { -1 } else { 1 } }
        else { $c = [string]::CompareOrdinal($l[$i],$r[$i]) }
        if ($c -ne 0) { return $c }
    }
    return $l.Count.CompareTo($r.Count)
}

function Get-CatalogVersions {
    param([Parameter(Mandatory)][object]$Entry)
    if ($null -ne $Entry.PSObject.Properties['versions']) { return @($Entry.versions) }
    if ($null -ne $Entry.PSObject.Properties['version']) { return @($Entry) } # 仅发布工具允许迁移 v1；客户端不兼容 v1。
    return @()
}

function Get-LatestCatalogVersion {
    param([Parameter(Mandatory)][object]$Entry)
    $best = $null
    foreach ($version in @(Get-CatalogVersions $Entry)) { if ($null -eq $best -or (Compare-CatalogVersions $version.version $best.version) -gt 0) { $best = $version } }
    return $best
}

function Assert-CatalogRelease {
    param([Parameter(Mandatory)][object]$Release,[Parameter(Mandatory)][string]$Id)
    foreach ($key in @('version','name','releaseTag','assetName','packageUrl','sha256','size','apiVersion','targetFramework','minHostVersion','typeClaims','capabilities')) {
        if ($null -eq $Release.PSObject.Properties[$key]) { throw "Missing $key in $Id release" }
    }
    ConvertTo-CatalogVersion ([string]$Release.version) | Out-Null
    ConvertTo-CatalogVersion ([string]$Release.minHostVersion) | Out-Null
    $max = [string](Get-CatalogProperty $Release 'maxHostVersion')
    if ($max) { ConvertTo-CatalogVersion $max | Out-Null; if ((Compare-CatalogVersions $max $Release.minHostVersion) -lt 0) { throw "Invalid host range in $Id" } }
    if ([string]::IsNullOrWhiteSpace($Release.name) -or $Release.apiVersion -notmatch '^[0-9]+\.[0-9]+$' -or $Release.targetFramework -notmatch '^net[0-9]+\.[0-9]+-windows[0-9.]*$') { throw "Invalid requirements in $Id" }
    $uri = $null
    if (-not [uri]::TryCreate([string]$Release.packageUrl,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com') { throw "Invalid URL in $Id" }
    $prefix = '/Star-Pie/StarPie-Official-Plugins/releases/download/' + [uri]::EscapeDataString([string]$Release.releaseTag) + '/'
    if (-not $uri.AbsolutePath.StartsWith($prefix,[StringComparison]::Ordinal) -or [IO.Path]::GetFileName($uri.AbsolutePath) -cne $Release.assetName -or $Release.assetName -notmatch '\.spkg$') { throw "Asset/tag mismatch in $Id" }
    if ($Release.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [long]$Release.size -le 0 -or [long]$Release.size -gt 100MB) { throw "Invalid integrity fields in $Id" }
    $features = @{}
    foreach ($feature in @(Get-CatalogProperty $Release 'features' @())) {
        if ([string]::IsNullOrWhiteSpace($feature.id) -or [string]::IsNullOrWhiteSpace($feature.name) -or $features.ContainsKey($feature.id)) { throw "Invalid or duplicate feature in $Id" }
        $features[$feature.id] = $true
    }
}

function Merge-CatalogVersions {
    param([Parameter(Mandatory)][string]$Id,[AllowEmptyCollection()][object[]]$Previous = @(),[object]$NewRelease)
    $versions = [System.Collections.Generic.List[object]]::new()
    foreach ($release in $Previous) {
        Assert-CatalogRelease $release $Id
        if (@($versions | Where-Object { (Compare-CatalogVersions $_.version $release.version) -eq 0 }).Count -ne 0) { throw "Duplicate historical version in $Id" }
        $versions.Add($release)
    }
    if ($null -ne $NewRelease) {
        Assert-CatalogRelease $NewRelease $Id
        $same = @($versions | Where-Object { (Compare-CatalogVersions $_.version $NewRelease.version) -eq 0 })
        if ($same.Count -gt 0) {
            if ($same[0].version -cne $NewRelease.version -or $same[0].sha256 -ine $NewRelease.sha256 -or [long]$same[0].size -ne [long]$NewRelease.size) { throw "Published version is immutable: $Id $($NewRelease.version)" }
        } else { $versions.Add($NewRelease) }
    }
    $versions.Sort([System.Comparison[object]]{ param($a,$b) Compare-CatalogVersions $b.version $a.version })
    return $versions.ToArray()
}

function Get-CatalogReleaseSelection {
    param([AllowEmptyCollection()][string[]]$ModuleIds,[object[]]$EnabledModules)
    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $ModuleIds) {
        foreach ($part in @($value -split ',')) {
            $id = $part.Trim()
            if (-not $id -or $EnabledModules.id -cnotcontains $id -or -not $selected.Add($id)) {
                throw "Invalid, duplicate or disabled release module: $id"
            }
        }
    }
    if ($selected.Count -eq 0) { throw 'An explicit release selection must not be empty.' }
    return ,$selected
}

function Assert-CatalogHistoryPreserved {
    param([Parameter(Mandatory)]$Catalog,[object]$PreviousCatalog,[Parameter(Mandatory)]$Selected,[object[]]$EnabledModules)
    $previous = @{}
    $previousGroups = if ($null -ne $PreviousCatalog) { @($PreviousCatalog.modules) } else { @() }
    foreach ($group in $previousGroups) {
        if ($null -eq $group) { continue }
        if ($previous.ContainsKey($group.id)) { throw "Duplicate previous module: $($group.id)" }
        $previous[$group.id] = $group
    }
    $actual = @{}
    foreach ($group in @($Catalog.modules)) {
        if ($actual.ContainsKey($group.id) -or (-not $Selected.Contains($group.id) -and -not $previous.ContainsKey($group.id))) {
            throw "Unexpected or duplicate module in selected release: $($group.id)"
        }
        $actual[$group.id] = $group
    }
    foreach ($id in $previous.Keys) {
        if (-not $actual.ContainsKey($id)) { throw "Historical module was removed: $id" }
        $oldVersions = @(Get-CatalogVersions $previous[$id])
        $newVersions = @(Get-CatalogVersions $actual[$id])
        foreach ($old in $oldVersions) {
            $retained = @($newVersions | Where-Object version -CEQ $old.version)
            if ($retained.Count -ne 1) { throw "Historical version was removed: $id $($old.version)" }
            foreach ($property in $old.PSObject.Properties) {
                if ($property.Name -eq 'id') { continue } # v1 entries have the ID on the release record.
                $value = $retained[0].PSObject.Properties[$property.Name]
                if ($null -eq $value -or
                    (ConvertTo-Json -InputObject $property.Value -Depth 100 -Compress) -cne
                    (ConvertTo-Json -InputObject $value.Value -Depth 100 -Compress)) {
                    throw "Historical metadata was changed: $id $($old.version) $($property.Name)"
                }
            }
        }
        $expectedCount = $oldVersions.Count
        if ($Selected.Contains($id)) {
            $source = @($EnabledModules | Where-Object id -CEQ $id)[0]
            if (-not ($oldVersions | Where-Object version -CEQ $source.version)) { $expectedCount++ }
        }
        if ($newVersions.Count -ne $expectedCount) { throw "Unrequested versions in selected release: $id" }
    }
    foreach ($id in $Selected) {
        if (-not $actual.ContainsKey($id)) { throw "Selected module is missing: $id" }
        if (-not $previous.ContainsKey($id) -and @($actual[$id].versions).Count -ne 1) { throw "Unexpected initial history: $id" }
    }
}

Export-ModuleMember -Function Get-CatalogProperty,ConvertTo-CatalogVersion,Compare-CatalogVersions,Get-CatalogVersions,Get-LatestCatalogVersion,Assert-CatalogRelease,Merge-CatalogVersions,Get-CatalogReleaseSelection,Assert-CatalogHistoryPreserved
