#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Compose a GitHub release body for a WKOpenVR package repository.
#>
[CmdletBinding()]
param(
    [string]   $Tag = $(if ($env:TAG_NAME) { $env:TAG_NAME } else { $env:GITHUB_REF_NAME }),
    [string]   $Repo = $env:GITHUB_REPOSITORY,
    [string]   $PackageName = "WKOpenVR package",
    [string]   $TemplateDir = $null,
    [string]   $Extras = $null,
    [string[]] $ArtifactPath = @(),
    [switch]   $AllowEmpty,
    [switch]   $SkipScrub
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Tag)) {
    throw "No tag provided. Pass -Tag or set TAG_NAME / GITHUB_REF_NAME."
}

if ([string]::IsNullOrWhiteSpace($TemplateDir)) {
    $TemplateDir = Join-Path (Get-Location).Path ".github\release-template"
}
if ([string]::IsNullOrWhiteSpace($Extras)) {
    $Extras = Join-Path (Get-Location).Path (".github\release-extras\" + $Tag + ".md")
}

function Invoke-Git {
    param([string[]] $Arguments)

    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
    return @($output)
}

function Test-GitCommitRef {
    param([string] $Ref)

    if ([string]::IsNullOrWhiteSpace($Ref)) { return $false }
    & git rev-parse --verify --quiet "$Ref^{commit}" 1>$null 2>$null
    return ($LASTEXITCODE -eq 0)
}

function Test-PrereleaseTag {
    param([string] $Value)

    return ($Value -match "^v?\d{4}\.\d+\.\d+\.\d+-.+")
}

function Get-ReleaseRef {
    param([string] $Value)

    $tagRef = "refs/tags/$Value"
    if (Test-GitCommitRef -Ref $tagRef) {
        return $tagRef
    }
    return "HEAD"
}

function Resolve-PreviousTag {
    param([string] $ReleaseRef, [string] $ReleaseTag)

    $describeArgs = @("describe", "--tags", "--abbrev=0", "--match", "v*")
    if (-not (Test-PrereleaseTag -Value $ReleaseTag)) {
        $describeArgs += @("--exclude", "*-*")
    }
    $describeArgs += "$ReleaseRef^"

    $previous = & git @describeArgs 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($previous)) {
        return $previous.Trim()
    }
    return ""
}

function Get-AuthorHandle {
    param([string] $Author)

    if ([string]::IsNullOrWhiteSpace($Author)) { return "" }
    $map = @{
        "WhyKnot" = "RealWhyKnot"
        "github-actions[bot]" = "github-actions[bot]"
    }
    if ($map.ContainsKey($Author)) {
        return [string]$map[$Author]
    }
    return $Author
}

function Get-BucketName {
    param([string] $Subject)

    if ($Subject -match "^(feat|fix|perf|refactor|revert|docs|style|test|ci|build|chore)(\([^)]+\))?!?:\s+") {
        switch ($Matches[1]) {
            "feat" { return "Features" }
            "fix" { return "Bug Fixes" }
            "perf" { return "Performance" }
            "refactor" { return "Refactors" }
            "revert" { return "Reverts" }
            "docs" { return "Documentation" }
            "style" { return "Style" }
            "test" { return "Tests" }
            "ci" { return "CI" }
            "build" { return "Build" }
            "chore" { return "Chores" }
        }
    }
    return "Other Changes"
}

function Get-CommitEntries {
    param([string] $ReleaseRef, [string] $PreviousTag)

    $rangeDisplay = ""
    $logArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($PreviousTag)) {
        $rangeDisplay = "$PreviousTag...$Tag"
        $logArgs += "$PreviousTag..$ReleaseRef"
    } else {
        $root = (Invoke-Git -Arguments @("rev-list", "--max-parents=0", $ReleaseRef) | Select-Object -First 1).Trim()
        $rangeDisplay = "$root...$Tag"
        $logArgs += "$root..$ReleaseRef"
    }

    $lines = @(& git log --no-merges --reverse "--format=%H%x09%h%x09%an%x09%s" @logArgs)
    if ($LASTEXITCODE -ne 0) {
        throw "git log failed for $($logArgs -join ' ')"
    }

    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split "`t", 4
        if ($parts.Count -ne 4) { continue }

        $subject = $parts[3].Trim()
        if ($subject -match "\[skip changelog\]") { continue }
        $subject = [regex]::Replace($subject, "\s+\(\d{4}\.\d+\.\d+\.\d+(?:-[A-Fa-f0-9]{4})?\)$", "")

        $entries.Add([pscustomobject]@{
            Sha = $parts[0]
            ShortSha = $parts[1]
            Author = $parts[2]
            Subject = $subject
            Bucket = Get-BucketName -Subject $subject
        }) | Out-Null
    }

    return [pscustomobject]@{
        Entries = $entries.ToArray()
        RangeDisplay = $rangeDisplay
    }
}

function Format-Size {
    param([long] $Bytes)

    if ($Bytes -ge 1048576) {
        return ("{0:N2} MB" -f ($Bytes / 1048576.0))
    }
    if ($Bytes -ge 1024) {
        return ("{0:N1} KB" -f ($Bytes / 1024.0))
    }
    return "$Bytes B"
}

function Add-TemplateSection {
    param(
        [System.Text.StringBuilder] $Builder,
        [string] $Name,
        [hashtable] $Tokens
    )

    $path = Join-Path $TemplateDir "$Name.md"
    if (-not (Test-Path -LiteralPath $path)) { return }

    $text = [System.IO.File]::ReadAllText($path)
    foreach ($key in $Tokens.Keys) {
        $text = $text.Replace("{$key}", [string]$Tokens[$key])
    }
    $text = $text.Trim()
    if ($text.Length -eq 0) { return }

    [void]$Builder.AppendLine()
    [void]$Builder.AppendLine($text)
}

function Normalize-Ascii {
    param([string] $Text)

    $subs = @(
        @{ Code = 0x2014; Replacement = "--" },
        @{ Code = 0x2013; Replacement = "-" },
        @{ Code = 0x2026; Replacement = "..." },
        @{ Code = 0x2018; Replacement = "'" },
        @{ Code = 0x2019; Replacement = "'" },
        @{ Code = 0x201C; Replacement = '"' },
        @{ Code = 0x201D; Replacement = '"' },
        @{ Code = 0x00A0; Replacement = " " }
    )
    $normalized = $Text
    foreach ($sub in $subs) {
        $normalized = $normalized.Replace([string][char]$sub.Code, [string]$sub.Replacement)
    }
    return $normalized
}

function Assert-ReleaseBodyClean {
    param([string] $Text)

    $badChars = New-Object System.Collections.Generic.List[string]
    $lineNo = 0
    foreach ($line in ($Text -split "`r?`n")) {
        $lineNo++
        for ($i = 0; $i -lt $line.Length; $i++) {
            $code = [int][char]$line[$i]
            if (($code -lt 32 -and $code -ne 9) -or $code -gt 126) {
                $badChars.Add("line $lineNo column $($i + 1) U+$($code.ToString('X4'))") | Out-Null
            }
        }
    }
    if ($badChars.Count -gt 0) {
        throw "Non-ASCII characters in release body after normalization:`n$($badChars -join "`n")"
    }

    $forbidden = @(
        "\bAI\b",
        "\bCodex\b",
        "\bOpenAI\b",
        "\bClaude\b",
        "\bAnthropic\b",
        "\bChatGPT\b",
        "\bGPT\b",
        "\bLLM\b",
        "\bassistant\b",
        "\bagent\b",
        "\bgenerated\b",
        "\borchestrator\b",
        "\bprompt\b",
        "\btoken\b",
        "\binference\b",
        "\bseamless\b",
        "\bcomprehensive\b",
        "\bthoroughly\b",
        "\brobust\b",
        "\bleveraging\b",
        "\benhance\b"
    )
    foreach ($pattern in $forbidden) {
        if ($Text -match "(?i)$pattern") {
            throw "Release body contains blocked public-surface wording matching pattern '$pattern'."
        }
    }
}

$releaseRef = Get-ReleaseRef -Value $Tag
$releaseSha = (Invoke-Git -Arguments @("rev-parse", "$releaseRef^{commit}") | Select-Object -First 1).Trim()
$releaseShortSha = (Invoke-Git -Arguments @("rev-parse", "--short=12", "$releaseRef^{commit}") | Select-Object -First 1).Trim()
$previousTag = Resolve-PreviousTag -ReleaseRef $releaseRef -ReleaseTag $Tag
$commitData = Get-CommitEntries -ReleaseRef $releaseRef -PreviousTag $previousTag

if ($commitData.Entries.Count -eq 0 -and -not $AllowEmpty) {
    throw "No release-note commits found for $($commitData.RangeDisplay)."
}

$version = $Tag -replace "^v", ""
$tokens = @{
    "full-repo" = $Repo
    "repo-name" = if ($Repo -match "/") { ($Repo -split "/")[-1] } else { $Repo }
    "tag" = $Tag
    "version" = $version
    "commit-sha" = $releaseSha
    "commit-sha-short" = $releaseShortSha
    "package-name" = $PackageName
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# $PackageName $Tag")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## What's Changed")
if ($commitData.Entries.Count -eq 0) {
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("_Maintenance release; see commit log for details._")
} else {
    if (-not [string]::IsNullOrWhiteSpace($Repo)) {
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("Full changelog: https://github.com/$Repo/compare/$($commitData.RangeDisplay)")
    }

    $bucketOrder = @("Features", "Bug Fixes", "Performance", "Refactors", "Reverts", "Documentation", "Style", "Tests", "CI", "Build", "Chores", "Other Changes")
    foreach ($bucket in $bucketOrder) {
        $items = @($commitData.Entries | Where-Object { $_.Bucket -eq $bucket })
        if ($items.Count -eq 0) { continue }
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("### $bucket")
        foreach ($entry in $items) {
            $author = Get-AuthorHandle -Author $entry.Author
            $commitLink = if (-not [string]::IsNullOrWhiteSpace($Repo)) {
                "([$($entry.ShortSha)](https://github.com/$Repo/commit/$($entry.Sha)))"
            } else {
                "($($entry.ShortSha))"
            }
            $authorText = if (-not [string]::IsNullOrWhiteSpace($author)) { " by @$author" } else { "" }
            [void]$sb.AppendLine("- $($entry.Subject) $commitLink$authorText")
        }
    }
}

$artifactRows = New-Object System.Collections.Generic.List[string]
foreach ($artifact in $ArtifactPath) {
    if ([string]::IsNullOrWhiteSpace($artifact)) { continue }
    if (-not (Test-Path -LiteralPath $artifact)) {
        throw "Release artifact not found: $artifact"
    }
    $item = Get-Item -LiteralPath $artifact
    $sha = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $artifactRows.Add("| $($item.Name) | $(Format-Size -Bytes $item.Length) | ``$sha`` |") | Out-Null
}
if ($artifactRows.Count -gt 0) {
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("## File integrity")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Verify with ``Get-FileHash <file> -Algorithm SHA256`` on PowerShell.")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| File | Size | SHA256 |")
    [void]$sb.AppendLine("|---|---:|---|")
    foreach ($row in $artifactRows) {
        [void]$sb.AppendLine($row)
    }
}

foreach ($section in @("links", "install", "beta", "compatibility")) {
    Add-TemplateSection -Builder $sb -Name $section -Tokens $tokens
}

if (Test-Path -LiteralPath $Extras) {
    $extraText = [System.IO.File]::ReadAllText($Extras).Trim()
    if ($extraText.Length -gt 0) {
        foreach ($key in $tokens.Keys) {
            $extraText = $extraText.Replace("{$key}", [string]$tokens[$key])
        }
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("---")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("## Additional notes")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine($extraText)
    }
}

$body = Normalize-Ascii -Text ($sb.ToString().TrimEnd())
if (-not $SkipScrub) {
    Assert-ReleaseBodyClean -Text $body
}

$body
