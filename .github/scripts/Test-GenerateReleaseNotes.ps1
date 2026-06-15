#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Generator = Join-Path $ScriptRoot "Generate-ReleaseNotes.ps1"
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wkopenvr-package-notes-" + [System.Guid]::NewGuid().ToString("N"))
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$PushedLocation = $false

function Invoke-Git {
    $output = & git @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($args -join ' ') failed with exit code $LASTEXITCODE"
    }
    return @($output)
}

function Write-TestFile {
    param([string] $Path, [string] $Content)

    $fullPath = Join-Path (Get-Location).Path $Path
    $parent = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    [System.IO.File]::WriteAllText($fullPath, $Content, $Utf8NoBom)
}

function Commit-TestChange {
    param([string] $Path, [string] $Content, [string] $Subject)

    Write-TestFile -Path $Path -Content $Content
    Invoke-Git add -- $Path | Out-Null
    Invoke-Git commit -q -m $Subject | Out-Null
}

function Invoke-Generator {
    param([string] $Tag)

    $artifactPaths = [string[]]@("artifacts\packages\module.zip", "artifacts\packages\module.manifest.json")
    $output = & $Generator `
        -Tag $Tag `
        -Repo "RealWhyKnot/WKOpenVR.SyntheticFaceModule" `
        -PackageName "WKOpenVR Synthetic Face Module" `
        -TemplateDir (Join-Path (Get-Location).Path ".github\release-template") `
        -ArtifactPath $artifactPaths `
        -IntegrityName "module.integrity.tsv"
    if ($LASTEXITCODE -ne 0) {
        throw "Generate-ReleaseNotes failed for $Tag"
    }
    return ($output -join "`n")
}

function Assert-Contains {
    param([string] $Text, [string] $Expected)

    if (-not $Text.Contains($Expected)) {
        throw "Expected release notes to contain '$Expected'."
    }
}

function Assert-NotContains {
    param([string] $Text, [string] $Unexpected)

    if ($Text.Contains($Unexpected)) {
        throw "Expected release notes not to contain '$Unexpected'."
    }
}

try {
    New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null
    Push-Location $TempRoot
    $PushedLocation = $true

    Invoke-Git init -q | Out-Null
    Invoke-Git config core.autocrlf false | Out-Null
    Invoke-Git config user.name WhyKnot | Out-Null
    Invoke-Git config user.email whyknot@example.invalid | Out-Null

    Write-TestFile -Path ".github\release-template\links.md" -Content @"
## More

- **README:** <https://github.com/{full-repo}>
- **Source:** commit ``{commit-sha-short}`` on ``main``
- **License:** [GPL-3.0](https://github.com/{full-repo}/blob/main/LICENSE)
"@
    Write-TestFile -Path ".github\release-template\install.md" -Content @"
## Install

Install {package-name} from the WKOpenVR Face Tracking module registry source.
"@
    Write-TestFile -Path ".github\release-template\beta.md" -Content @"
## Beta

Beta releases require prerelease entries to be enabled in the module source.
"@
    Write-TestFile -Path ".github\release-template\compatibility.md" -Content @"
## Compatibility

Built for WKOpenVR native face modules.
"@
    Write-TestFile -Path "artifacts\packages\module.zip" -Content "payload"
    Write-TestFile -Path "artifacts\packages\module.manifest.json" -Content "{}"

    Commit-TestChange -Path "README.md" -Content "# Test`n" -Subject "docs: seed package"
    Invoke-Git tag -a v2026.6.1.0 -m "v2026.6.1.0" | Out-Null

    Commit-TestChange -Path "src.txt" -Content "beta`n" -Subject "fix(module): tune mouth envelope (2026.6.2.0-ABCD)"
    Invoke-Git tag -a v2026.6.2.0-beta -m "v2026.6.2.0-beta" | Out-Null

    Commit-TestChange -Path "src.txt" -Content "stable`n" -Subject "ci(release): compose package notes"
    Invoke-Git tag -a v2026.6.3.0 -m "v2026.6.3.0" | Out-Null

    $stableNotes = Invoke-Generator -Tag "v2026.6.3.0"
    Assert-Contains -Text $stableNotes -Expected "# WKOpenVR Synthetic Face Module v2026.6.3.0"
    Assert-Contains -Text $stableNotes -Expected "fix(module): tune mouth envelope"
    Assert-Contains -Text $stableNotes -Expected "ci(release): compose package notes"
    Assert-Contains -Text $stableNotes -Expected "compare/v2026.6.1.0...v2026.6.3.0"
    Assert-Contains -Text $stableNotes -Expected "module.integrity.tsv"
    Assert-NotContains -Text $stableNotes -Unexpected "| module.zip |"
    Assert-NotContains -Text $stableNotes -Unexpected '$sha'
    Assert-Contains -Text $stableNotes -Expected "Install WKOpenVR Synthetic Face Module"

    $betaNotes = Invoke-Generator -Tag "v2026.6.2.0-beta"
    Assert-Contains -Text $betaNotes -Expected "fix(module): tune mouth envelope"
    Assert-Contains -Text $betaNotes -Expected "compare/v2026.6.1.0...v2026.6.2.0-beta"
    Assert-NotContains -Text $betaNotes -Unexpected "ci(release): compose package notes"

    Commit-TestChange -Path "dispatch.txt" -Content "dispatch`n" -Subject "docs(release): document local package install"
    $dispatchNotes = Invoke-Generator -Tag "v2026.6.4.0-beta"
    Assert-Contains -Text $dispatchNotes -Expected "docs(release): document local package install"
    Assert-Contains -Text $dispatchNotes -Expected "compare/v2026.6.3.0...v2026.6.4.0-beta"

    Write-Host "Generate-ReleaseNotes tests passed."
}
finally {
    if ($PushedLocation) {
        Pop-Location
    }
    if (Test-Path -LiteralPath $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force
    }
}
