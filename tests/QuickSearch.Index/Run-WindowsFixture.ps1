param(
    [Parameter(Mandatory)][string]$TestDll,
    [Parameter(Mandatory)][string]$FixtureParent,
    [string]$SeedTestDll = $TestDll
)
$ErrorActionPreference = 'Stop'
$parentPath = [IO.Path]::GetFullPath($FixtureParent)
$null = [IO.Directory]::CreateDirectory($parentPath)
$owned = Join-Path $parentPath ('windows-quick-search-' + [guid]::NewGuid().ToString('N'))
$selected = Join-Path $owned 'selected'
$outside = Join-Path $owned 'outside'
$denied = Join-Path $selected 'denied'
$originalAcl = $null
try {
    $null = [IO.Directory]::CreateDirectory($denied)
    $previous = Join-Path $selected 'external-junction'
    $null = [IO.Directory]::CreateDirectory((Join-Path $previous 'private'))
    [IO.File]::WriteAllText((Join-Path $selected 'public-entry.txt'), '')
    [IO.File]::WriteAllText((Join-Path $previous 'private/secret-entry.txt'), '')
    [IO.File]::WriteAllText((Join-Path $denied 'denied-entry.txt'), '')
    dotnet $SeedTestDll --prime-windows-cache $owned
    if ($LASTEXITCODE -ne 0) { throw "Could not seed the prior accessible catalogue." }
    [IO.Directory]::Move($previous, $outside)
    $null = New-Item -ItemType Junction -Path (Join-Path $selected 'cycle') -Target $selected
    $null = New-Item -ItemType Junction -Path (Join-Path $selected 'external-junction') -Target $outside
    $originalAcl = Get-Acl -LiteralPath $denied
    $restricted = Get-Acl -LiteralPath $denied
    $everyone = [Security.Principal.SecurityIdentifier]::new('S-1-1-0')
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($everyone, 'ListDirectory', 'Deny')
    $restricted.AddAccessRule($rule)
    Set-Acl -LiteralPath $denied -AclObject $restricted
    dotnet $TestDll $parentPath $owned
    if ($LASTEXITCODE -ne 0) { throw "Index fixture failed with exit code $LASTEXITCODE." }
}
finally {
    if ($null -ne $originalAcl) { Set-Acl -LiteralPath $denied -AclObject $originalAcl }
    if (Test-Path -LiteralPath $owned) {
        $resolved = (Resolve-Path -LiteralPath $owned).ProviderPath
        if ([IO.Path]::GetDirectoryName($resolved) -ne $parentPath) { throw 'Unexpected fixture location.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
