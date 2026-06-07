param(
    [string]$Configuration = "Release",
    [string]$SdkVersion = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
    $sdkVersionPath = Join-Path (Split-Path -Parent $root) "WKOpenVR.FaceTracking.Sdk\version.txt"
    if (Test-Path -LiteralPath $sdkVersionPath) {
        $SdkVersion = [System.IO.File]::ReadAllText($sdkVersionPath).Trim()
    }
}
if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
    throw "SdkVersion is empty"
}
Write-Host "SDK version: $SdkVersion"

dotnet build (Join-Path $root "WKOpenVR.SyntheticFaceModule.sln") -c $Configuration /warnaserror /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
