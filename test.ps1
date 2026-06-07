param(
    [string]$Configuration = "Release",
    [string]$SdkVersion = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$testProject = Join-Path $root "tests\WKOpenVR.SyntheticFaceModule.Tests\WKOpenVR.SyntheticFaceModule.Tests.csproj"
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

dotnet run --project $testProject -c $Configuration /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
