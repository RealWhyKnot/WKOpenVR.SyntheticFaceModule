param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$SdkVersion = "0.1.0"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionPath = Join-Path $root "version.txt"
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($versionPath, $Version.Trim(), $utf8NoBom)
}
$version = [System.IO.File]::ReadAllText($versionPath).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "version.txt is empty"
}
if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
    throw "SdkVersion is empty"
}

dotnet restore (Join-Path $root "WKOpenVR.SyntheticFaceModule.sln") /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $root "WKOpenVR.SyntheticFaceModule.sln") -c $Configuration --no-restore /p:Version=$version /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
