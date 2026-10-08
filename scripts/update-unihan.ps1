[CmdletBinding()]
param(
    [string]$Version = "18.0.0"
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use the numeric x.y.z format."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dataDirectory = Join-Path $repositoryRoot "data"
$archiveUrl = "https://www.unicode.org/Public/$Version/ucd/Unihan.zip"
$uniqueId = [Guid]::NewGuid().ToString("N")
$archivePath = Join-Path ([IO.Path]::GetTempPath()) "unihan-$uniqueId.zip"
$extractPath = Join-Path ([IO.Path]::GetTempPath()) "unihan-$uniqueId"
$stagedReadingsPath = Join-Path $dataDirectory ".Unihan_Readings.$uniqueId.tmp"
$destinationPath = Join-Path $dataDirectory "Unihan_Readings.txt"

try {
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath

    $readingsPath = Join-Path $extractPath "Unihan_Readings.txt"
    if (-not (Test-Path -LiteralPath $readingsPath -PathType Leaf)) {
        throw "The archive does not contain Unihan_Readings.txt."
    }

    $versionHeader = Select-String -LiteralPath $readingsPath -Pattern '^# Unicode Version (?<version>\d+\.\d+\.\d+)$' |
        Select-Object -First 1
    if ($null -eq $versionHeader -or $versionHeader.Matches[0].Groups["version"].Value -ne $Version) {
        throw "The archive did not contain the expected Unicode $Version version header."
    }

    Copy-Item -LiteralPath $readingsPath -Destination $stagedReadingsPath
    Move-Item -LiteralPath $stagedReadingsPath -Destination $destinationPath -Force
    Write-Host "Updated $destinationPath from Unicode $Version."
}
finally {
    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $extractPath -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stagedReadingsPath -Force -ErrorAction SilentlyContinue
}