param(
    [string]$Configuration = "Release",
    [string]$SdkVersion = "0.1.0"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
    throw "SdkVersion is empty"
}

dotnet build (Join-Path $root "WKOpenVR.SyntheticFaceModule.sln") -c $Configuration /warnaserror /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
