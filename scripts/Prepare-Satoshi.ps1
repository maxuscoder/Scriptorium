# Obtain unmodified desktop fonts directly from Fontshare for local app embedding.
[CmdletBinding()]
param(
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fontDirectory = Join-Path $PSScriptRoot '../src/Scriptorium.App/Assets/Fonts/Satoshi'
New-Item -ItemType Directory -Path $fontDirectory -Force | Out-Null
$downloadedArchive = $null
$archive = $null
try {
    if (!$ArchivePath) {
        $downloadedArchive = [System.IO.Path]::GetTempFileName()
        Invoke-WebRequest -Uri 'https://api.fontshare.com/v2/fonts/download/satoshi' -OutFile $downloadedArchive
        $ArchivePath = $downloadedArchive
    }
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
    $styles = @('Light', 'LightItalic', 'Regular', 'Italic', 'Medium', 'MediumItalic', 'Bold', 'BoldItalic', 'Black', 'BlackItalic')
    foreach ($style in $styles) {
        $name = "Satoshi-$style.otf"
        $entry = $archive.GetEntry("Satoshi_Complete/Fonts/OTF/$name")
        if (!$entry) { throw "Official Satoshi archive is missing $name." }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $fontDirectory $name), $true)
    }
    $license = $archive.GetEntry('Satoshi_Complete/License/FFL.txt')
    if (!$license) { throw 'Official Satoshi archive is missing its license.' }
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($license, (Join-Path $fontDirectory 'FFL.txt'), $true)
    Write-Output 'Satoshi desktop fonts and license are ready for embedding.'
}
finally {
    if ($archive) { $archive.Dispose() }
    if ($downloadedArchive) { Remove-Item -LiteralPath $downloadedArchive }
}
