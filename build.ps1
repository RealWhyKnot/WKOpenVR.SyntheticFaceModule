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

$currentHooksPath = & git config --get core.hooksPath 2>$null
if ($currentHooksPath -ne ".githooks" -and (Test-Path -LiteralPath (Join-Path $root ".git"))) {
    & git config core.hooksPath ".githooks"
    Write-Host "Activated .githooks/ via core.hooksPath"
}

$versionPath = Join-Path $root "version.txt"
if ([string]::IsNullOrWhiteSpace($Version)) {
    $today = Get-Date -Format "yyyy.M.d"
    $stateDir = Join-Path $root "artifacts"
    $statePath = Join-Path $stateDir "local_build_state.json"
    $counter = 0
    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.date -eq $today) {
            $counter = [int]$state.counter + 1
        }
    }
    $uid = ([guid]::NewGuid().ToString("N").Substring(0, 4)).ToUpper()
    $Version = "$today.$counter-$uid"
    New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
    @{ date = $today; counter = $counter } | ConvertTo-Json | Set-Content -LiteralPath $statePath
}
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($versionPath, $Version.Trim(), $utf8NoBom)

$version = [System.IO.File]::ReadAllText($versionPath).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "version.txt is empty"
}
$SdkVersion = Resolve-SdkVersion -Root $root -RequestedVersion $SdkVersion
if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
    throw "SdkVersion is empty"
}
Write-Host "Build version: $version"
Write-Host "SDK version: $SdkVersion"

dotnet restore (Join-Path $root "WKOpenVR.SyntheticFaceModule.sln") /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $root "WKOpenVR.SyntheticFaceModule.sln") -c $Configuration --no-restore /p:Version=$version /p:WkOpenVrFaceSdkVersion=$SdkVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
