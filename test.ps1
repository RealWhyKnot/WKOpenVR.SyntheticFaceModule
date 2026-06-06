param(
    [string]$Configuration = "Release",
    [string]$SdkVersion = "0.1.0"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$testProject = Join-Path $root "tests\WKOpenVR.SyntheticFaceModule.Tests\WKOpenVR.SyntheticFaceModule.Tests.csproj"
if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
    throw "SdkVersion is empty"
}

dotnet run --project $testProject -c $Configuration /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
