function New-PortableZip {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $source = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $destination = [IO.Path]::GetFullPath($DestinationPath)
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }

    $archive = [IO.Compression.ZipFile]::Open($destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File | Sort-Object FullName) {
            $relative = $file.FullName.Substring($source.Length).TrimStart('\', '/').Replace('\', '/')
            if ([string]::IsNullOrWhiteSpace($relative) -or $relative.StartsWith('/') -or $relative.Contains('\')) {
                throw "Unsafe ZIP entry path: $relative"
            }
            $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $file.LastWriteTime
            $input = $file.OpenRead()
            $output = $entry.Open()
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    $check = [IO.Compression.ZipFile]::OpenRead($destination)
    try {
        $entries = @($check.Entries | ForEach-Object FullName)
        $invalid = @($entries | Where-Object { $_.Contains('\') -or $_.StartsWith('/') })
        if ($invalid.Count -gt 0) { throw "ZIP contains non-portable entry paths: $($invalid -join ', ')" }
        if (-not ($entries | Where-Object { $_ -like 'BepInEx/plugins/*' })) {
            throw 'ZIP does not contain BepInEx/plugins entries.'
        }
    }
    finally { $check.Dispose() }
}
