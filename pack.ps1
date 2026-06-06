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
$project = Join-Path $root "src\WKOpenVR.SyntheticFaceModule\WKOpenVR.SyntheticFaceModule.csproj"
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "publish"
$stageDir = Join-Path $artifacts "stage\WKOpenVR.SyntheticFaceModule"
$assembliesDir = Join-Path $stageDir "assemblies"
$packageDir = Join-Path $artifacts "packages"
$payload = Join-Path $packageDir ("WKOpenVR.SyntheticFaceModule." + $version + ".zip")
$registryManifest = Join-Path $packageDir ("WKOpenVR.SyntheticFaceModule." + $version + ".manifest.json")

if (Test-Path $publishDir) { Remove-Item -Recurse -Force -LiteralPath $publishDir }
if (Test-Path $stageDir) { Remove-Item -Recurse -Force -LiteralPath $stageDir }
New-Item -ItemType Directory -Force -Path $publishDir, $assembliesDir, $packageDir | Out-Null

dotnet publish $project -c $Configuration -o $publishDir /p:Version=$version /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem -Path $publishDir -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $assembliesDir $_.Name) -Force
}

$manifest = [ordered]@{
    schema = 1
    uuid = "4df7850f-1d75-4665-9eab-6f07e0f3b5dc"
    name = "WKOpenVR Synthetic Face Module"
    vendor = "WhyKnot"
    homepage = "https://github.com/whyknotdev/WKOpenVR.SyntheticFaceModule"
    license = "GPL-3.0-only"
    version = $version
    sdk_version = $SdkVersion
    min_host_version = "1.0"
    supported_hmds = @("*")
    capabilities = @("expression", "audio")
    platforms = @("windows-x64")
    module_kind = "wkopenvr-native"
    module_api = "WKOpenVR.FaceTracking.Sdk/" + $SdkVersion
    sdk_package = "WKOpenVR.FaceTracking.Sdk"
    entry_assembly = "WKOpenVR.SyntheticFaceModule.dll"
    entry_type = "WKOpenVR.SyntheticFaceModule.SyntheticFaceModule"
    dependencies = @()
    payload_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    payload_size = 0
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $stageDir "manifest.json"), ($manifest | ConvertTo-Json -Depth 12), $utf8NoBom)

if (Test-Path $payload) { Remove-Item -Force -LiteralPath $payload }
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $payload -Force

$hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $payload).Length
$manifest.payload_sha256 = $hash
$manifest.payload_size = $size
[System.IO.File]::WriteAllText($registryManifest, ($manifest | ConvertTo-Json -Depth 12), $utf8NoBom)

Write-Host ("Payload: " + $payload)
Write-Host ("Registry manifest: " + $registryManifest)
