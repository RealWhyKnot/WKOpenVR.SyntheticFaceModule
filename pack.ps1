param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$SdkVersion = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Resolve-SdkVersion {
    param([string]$Root, [string]$RequestedVersion)

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return $RequestedVersion.Trim()
    }

    $sdkVersionPath = Join-Path (Split-Path -Parent $Root) "WKOpenVR.FaceTracking.Sdk\version.txt"
    if (Test-Path -LiteralPath $sdkVersionPath) {
        $resolved = [System.IO.File]::ReadAllText($sdkVersionPath).Trim()
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            return $resolved
        }
    }

    $ownVersionPath = Join-Path $Root "version.txt"
    if (Test-Path -LiteralPath $ownVersionPath) {
        $resolved = [System.IO.File]::ReadAllText($ownVersionPath).Trim()
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            return $resolved
        }
    }

    throw "SdkVersion is empty and no sibling SDK version.txt was found."
}

$versionPath = Join-Path $root "version.txt"
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($versionPath, $Version.Trim(), $utf8NoBom)
}
$version = [System.IO.File]::ReadAllText($versionPath).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "version.txt is empty"
}
$SdkVersion = Resolve-SdkVersion -Root $root -RequestedVersion $SdkVersion
if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
    throw "SdkVersion is empty"
}
Write-Host "Package version: $version"
Write-Host "SDK version: $SdkVersion"
$project = Join-Path $root "src\WKOpenVR.SyntheticFaceModule\WKOpenVR.SyntheticFaceModule.csproj"
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "publish"
$stageDir = Join-Path $artifacts "stage\WKOpenVR.SyntheticFaceModule"
$assembliesDir = Join-Path $stageDir "assemblies"
$packageDir = Join-Path $artifacts "packages"
$payload = Join-Path $packageDir ("WKOpenVR.SyntheticFaceModule." + $version + ".zip")
$registryManifest = Join-Path $packageDir ("WKOpenVR.SyntheticFaceModule." + $version + ".manifest.json")
$releaseTag = "v" + $version
$payloadName = [System.IO.Path]::GetFileName($payload)
$releaseUrl = "https://github.com/RealWhyKnot/WKOpenVR.SyntheticFaceModule/releases/tag/" + $releaseTag
$payloadUrl = "https://github.com/RealWhyKnot/WKOpenVR.SyntheticFaceModule/releases/download/" + $releaseTag + "/" + $payloadName
$isPrerelease = $version.Contains("-")
$releaseChannel = if ($isPrerelease) { "beta" } else { "stable" }

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
    homepage = "https://github.com/RealWhyKnot/WKOpenVR.SyntheticFaceModule"
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
    release_tag = $releaseTag
    release_url = $releaseUrl
    release_channel = $releaseChannel
    prerelease = $isPrerelease
    payload_url = $payloadUrl
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
