Set-StrictMode -Version Latest

function Get-RepositoryRoot {
    [CmdletBinding()]
    param([string]$StartPath = $PSScriptRoot)

    $current = Get-Item -LiteralPath $StartPath
    while ($null -ne $current) {
        if (Test-Path -LiteralPath (Join-Path $current.FullName '.git')) {
            return $current.FullName
        }
        $current = $current.Parent
    }

    throw "Unable to locate repository root from '$StartPath'."
}

function Resolve-RepositoryPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-RepositoryRoot) $Path))
}
function Read-JsonFile {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $Path = Resolve-RepositoryPath -Path $Path
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "JSON file not found: $Path"
    }

    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('Depth')) {
        return $raw | ConvertFrom-Json -Depth 100
    }
    return $raw | ConvertFrom-Json
}

function Write-JsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    $Path = Resolve-RepositoryPath -Path $Path
    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-ModuleRegistry {
    [CmdletBinding()]
    param([string]$RegistryPath)

    $root = Get-RepositoryRoot
    if (-not $RegistryPath) {
        $RegistryPath = Join-Path $root 'module-registry.json'
    }

    $registry = Read-JsonFile -Path $RegistryPath
    if ($registry.schemaVersion -ne 1) {
        throw "Unsupported module registry schemaVersion: $($registry.schemaVersion)"
    }

    return $registry
}

function Get-EnabledModules {
    [CmdletBinding()]
    param([object]$Registry)

    if ($null -eq $Registry) {
        $Registry = Get-ModuleRegistry
    }

    $modules = @($Registry.modules)
    return @($modules | Where-Object {
        if ($null -eq $_.PSObject.Properties['enabled']) { return $true }
        return $_.enabled -ne $false
    })
}

function Get-ModuleById {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ModuleId,
        [object]$Registry
    )

    $module = Get-EnabledModules -Registry $Registry | Where-Object { $_.id -eq $ModuleId } | Select-Object -First 1
    if ($null -eq $module) {
        throw "Module '$ModuleId' is not present or not enabled in module-registry.json."
    }

    return $module
}

function Resolve-ModuleProjectPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Module)

    $root = Get-RepositoryRoot
    return [System.IO.Path]::GetFullPath((Join-Path $root $Module.project))
}

function Resolve-ModuleDirectory {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Module)

    return Split-Path -Parent (Resolve-ModuleProjectPath -Module $Module)
}

function Resolve-ModuleOutputDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Module,
        [string]$Configuration = 'Release'
    )

    $projectDirectory = Resolve-ModuleDirectory -Module $Module
    return Join-Path $projectDirectory (Join-Path 'bin' (Join-Path $Configuration $Module.targetFramework))
}

function Get-Sha256Hex {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha.ComputeHash($stream)
            return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function ConvertTo-NormalizedRelativePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$BasePath,[Parameter(Mandatory)][string]$Path)

    try {
        $relative = [System.IO.Path]::GetRelativePath($BasePath, $Path)
    }
    catch {
        $baseUri = New-Object System.Uri(($BasePath.TrimEnd('\', '/') + '/'))
        $targetUri = New-Object System.Uri($Path)
        $relative = [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString())
    }
    return $relative.Replace('\', '/')
}

function New-DeterministicZip {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $directory = Split-Path -Parent $DestinationPath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    $fixedTimestamp = [DateTime]::new(1980, 1, 1, 0, 0, 0, [DateTimeKind]::Unspecified)
    $files = Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File | Sort-Object FullName

    $archiveStream = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($archiveStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in $files) {
                $entryName = ConvertTo-NormalizedRelativePath -BasePath $SourceDirectory -Path $file.FullName
                $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp

                $entryStream = $entry.Open()
                try {
                    $input = [System.IO.File]::OpenRead($file.FullName)
                    try {
                        $input.CopyTo($entryStream)
                    }
                    finally {
                        $input.Dispose()
                    }
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }
}

function Select-ModulesFromJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ModulesJson,
        [object]$Registry
    )

    $requested = if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('Depth')) {
        @($ModulesJson | ConvertFrom-Json -Depth 100)
    } else {
        @($ModulesJson | ConvertFrom-Json)
    }
    if ($requested.Count -eq 1 -and $null -ne $requested[0].modules) {
        $requested = @($requested[0].modules)
    }

    $enabled = Get-EnabledModules -Registry $Registry
    if ($requested.Count -eq 0) {
        return @()
    }

    $ids = @($requested | ForEach-Object {
        if ($_.id) { [string]$_.id } else { [string]$_ }
    } | Where-Object { $_ } | Sort-Object -Unique)

    return @($enabled | Where-Object { $ids -contains $_.id })
}

Export-ModuleMember -Function *