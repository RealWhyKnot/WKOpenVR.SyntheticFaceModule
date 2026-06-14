[CmdletBinding()]
param(
    [string] $RepoRoot = "",
    [string] $BaseTag = "",
    [string] $Tag = "",
    [string] $Today = "",
    [string] $OutputJsonPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepoRoot {
    param([string] $Value)

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        return (Resolve-Path -LiteralPath $Value).Path
    }
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
}

function Invoke-Git {
    param([string[]] $Arguments)

    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
    return @($output)
}

function Test-GitTagExists {
    param([string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    & git rev-parse --verify --quiet "refs/tags/$Name^{commit}" *> $null
    return ($LASTEXITCODE -eq 0)
}

function Get-LatestReleaseTag {
    param([string] $Override)

    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        return $Override
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
        $json = & gh api "repos/$env:GITHUB_REPOSITORY/releases?per_page=50"
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to read releases for $env:GITHUB_REPOSITORY"
        }
        foreach ($release in @($json | ConvertFrom-Json)) {
            $draft = $false
            if ($null -ne $release.PSObject.Properties["draft"]) {
                $draft = [bool]$release.draft
            }
            if (-not $draft -and -not [string]::IsNullOrWhiteSpace([string]$release.tag_name)) {
                return [string]$release.tag_name
            }
        }
    }

    $tags = @(Invoke-Git -Arguments @("tag", "--sort=-creatordate", "--list", "v*"))
    return ($tags | Select-Object -First 1)
}

function Test-HasChangesSinceTag {
    param([string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return $true }
    if (-not (Test-GitTagExists -Name $Name)) { return $true }

    & git diff --quiet "$Name..HEAD" --
    $code = $LASTEXITCODE
    if ($code -eq 0) { return $false }
    if ($code -eq 1) { return $true }
    throw "git diff failed while comparing $Name..HEAD"
}

function Get-NextBetaTag {
    param([string] $DateStamp)

    if ([string]::IsNullOrWhiteSpace($DateStamp)) {
        $DateStamp = (Get-Date).ToUniversalTime().ToString("yyyy.M.d", [System.Globalization.CultureInfo]::InvariantCulture)
    }

    $escaped = [regex]::Escape($DateStamp)
    $pattern = "^v$escaped\.(\d+)-beta$"
    $highest = -1
    foreach ($existing in @(Invoke-Git -Arguments @("tag", "--list", "v$DateStamp.*-beta"))) {
        if ($existing -match $pattern) {
            $value = [int]$Matches[1]
            if ($value -gt $highest) { $highest = $value }
        }
    }

    return "v$DateStamp.$($highest + 1)-beta"
}

function Write-GitHubOutput {
    param([string] $Name, [string] $Value)

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) { return }
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "$Name=$Value"
}

$repoRootPath = Resolve-RepoRoot -Value $RepoRoot
Push-Location $repoRootPath
try {
    if (-not [string]::IsNullOrWhiteSpace($Tag) -and $Tag -notmatch "^v\d{4}\.\d+\.\d+\.\d+-beta$") {
        throw "Nightly beta tags must match vYYYY.M.D.N-beta."
    }

    $base = Get-LatestReleaseTag -Override $BaseTag
    $hasChanges = Test-HasChangesSinceTag -Name $base
    $nextTag = if ([string]::IsNullOrWhiteSpace($Tag)) { Get-NextBetaTag -DateStamp $Today } else { $Tag }

    $plan = [pscustomobject]@{
        has_changes = $hasChanges
        base_tag = $base
        next_tag = $nextTag
    }

    $json = $plan | ConvertTo-Json -Depth 4 -Compress
    if (-not [string]::IsNullOrWhiteSpace($OutputJsonPath)) {
        $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputJsonPath)
        $parent = Split-Path -Parent $resolvedOutputPath
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent | Out-Null
        }
        [System.IO.File]::WriteAllText($resolvedOutputPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    }

    Write-GitHubOutput -Name "has_changes" -Value ([string]$hasChanges).ToLowerInvariant()
    Write-GitHubOutput -Name "base_tag" -Value $base
    Write-GitHubOutput -Name "next_tag" -Value $nextTag

    Write-Host "Nightly beta release plan:"
    Write-Host "  Base tag: $base"
    Write-Host "  Next tag: $nextTag"
    Write-Host "  Has changes: $hasChanges"
}
finally {
    Pop-Location
}
