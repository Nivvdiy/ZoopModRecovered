<#
.SYNOPSIS
    Creates a local ZoopMod release and publishes it to GitHub

.DESCRIPTION
    This script performs the following operations:
    - Verifies you are on the main branch
    - Verifies the working directory is clean
    - Automatically detects the version from the already compiled DLL
    - Packages files into a ZIP
    - Creates a Git tag with the V-version format
    - Publishes the release to GitHub

    IMPORTANT: The script NEVER compiles the project.
    You must manually compile in Release mode before running this script.

.PARAMETER PatchVersion
    Patch number to add to the version (e.g., -PatchVersion 1 for V-2026.18.04-1)

.PARAMETER DryRun
    If present, performs all steps except GitHub publication

.EXAMPLE
    .\CreateRelease.ps1
    Creates a release with the version detected from the DLL (e.g., V-2026.18.04)

.EXAMPLE
    .\CreateRelease.ps1 -PatchVersion 1
    Creates a release with a patch (e.g., V-2026.18.04-1)

.EXAMPLE
    .\CreateRelease.ps1 -DryRun
    Complete test without publishing to GitHub
#>

param(
    [string]$TagName = "",
    [switch]$SkipBuild = $false,
    [switch]$DryRun = $false
)

#Requires -Version 7.0

# Configuration
$ErrorActionPreference = "Stop"
$ModName = "ZoopMod"
$ProjectFile = "ZoopMod.csproj"
$OutputDir = "release"

# Function to read the DLL path from ZoopMod.VS.props
function Get-ModDllPath {
    $propsFile = "ZoopMod.VS.props"

    if (-not (Test-Path $propsFile)) {
        Write-Host "❌ ZoopMod.VS.props file not found" -ForegroundColor Red
        Write-Host ""
        Write-Host "This file is required to locate the compiled DLL." -ForegroundColor Yellow
        Write-Host "Copy ZoopMod.VS.props.example to ZoopMod.VS.props and configure the paths." -ForegroundColor Yellow
        exit 1
    }

    try {
        # Load the XML
        [xml]$props = Get-Content $propsFile

        # Extract StationeersModOutputFolder (search in all PropertyGroup)
        $outputFolder = $null
        foreach ($propGroup in $props.Project.PropertyGroup) {
            if ($propGroup.StationeersModOutputFolder) {
                $outputFolder = $propGroup.StationeersModOutputFolder.InnerText
                break
            }
        }

        if (-not $outputFolder) {
            Write-Host "❌ StationeersModOutputFolder not found in ZoopMod.VS.props" -ForegroundColor Red
            exit 1
        }

        # Resolve environment variables like %USERNAME%
        $outputFolder = [System.Environment]::ExpandEnvironmentVariables($outputFolder)

        # Resolve MSBuild variables like $(RootFolder)
        # Simple approach: replace $(RootFolder) if defined
        if ($outputFolder -match '\$\(.*\)') {
            foreach ($propGroup in $props.Project.PropertyGroup) {
                if ($propGroup.RootFolder) {
                    $rootFolder = $propGroup.RootFolder.InnerText
                    $outputFolder = $outputFolder -replace '\$\(RootFolder\)', $rootFolder
                }
            }
        }

        # Build the complete path
        $dllPath = Join-Path $outputFolder "$ModName\$ModName.dll"

        return $dllPath
    } catch {
        Write-Host "❌ Error reading ZoopMod.VS.props: $_" -ForegroundColor Red
        exit 1
    }
}

$DllPath = Get-ModDllPath

# ============================================================================
# Utility functions
# ============================================================================

function Write-ColoredMessage {
    param(
        [string]$Message,
        [string]$Color = "White",
        [string]$Prefix = ""
    )

    if ($Prefix) {
        Write-Host "$Prefix " -ForegroundColor $Color -NoNewline
        Write-Host $Message
    } else {
        Write-Host $Message -ForegroundColor $Color
    }
}

function Write-Success { param([string]$Message) Write-ColoredMessage $Message "Green" "✅" }
function Write-Info { param([string]$Message) Write-ColoredMessage $Message "Cyan" "ℹ️" }
function Write-Warning { param([string]$Message) Write-ColoredMessage $Message "Yellow" "⚠️" }
function Write-ErrorMsg { param([string]$Message) Write-ColoredMessage $Message "Red" "❌" }
function Write-Step { param([string]$Message) Write-ColoredMessage "`n$Message" "Magenta" "📍" }

function Test-GitHubCLI {
    try {
        $null = gh --version
        return $true
    } catch {
        return $false
    }
}

function Test-GitRepository {
    try {
        $null = git rev-parse --git-dir 2>&1
        return $true
    } catch {
        return $false
    }
}

function Get-VersionFromModInfo {
    param([string]$DllPath)

    # The .info file is in the same folder as the DLL
    $dllDir = Split-Path -Parent $DllPath
    $infoPath = Join-Path $dllDir "ZoopMod.info"

    if (-not (Test-Path $infoPath)) {
        Write-ErrorMsg "ZoopMod.info file not found: $infoPath"
        Write-Host ""
        Write-Warning "The .info file should be automatically copied during compilation."
        Write-Info "Manually compile the project in Release mode in Visual Studio."
        exit 1
    }

    try {
        $content = Get-Content $infoPath -Raw | ConvertFrom-Json
        $version = $content._version

        if (-not $version) {
            Write-ErrorMsg "Version not found in ZoopMod.info"
            exit 1
        }

        Write-Success "Version detected from ZoopMod.info: $version"
        return $version
    } catch {
        Write-ErrorMsg "Error reading ZoopMod.info: $_"
        Write-Info "Ensure the file is in valid JSON format."
        exit 1
    }
}

function Get-CurrentBranch {
    $branch = git rev-parse --abbrev-ref HEAD
    return $branch.Trim()
}

function Test-OnMainBranch {
    $currentBranch = Get-CurrentBranch
    if ($currentBranch -ne "main") {
        Write-ErrorMsg "You must be on the 'main' branch to create a release"
        Write-Info "Branch actuelle: $currentBranch"
        Write-Host ""
        Write-Info "To switch to main:"
        Write-Host "  git checkout main" -ForegroundColor Gray
        return $false
    }
    Write-Success "Branch: $currentBranch ✓"
    return $true
}

function Test-TagFormat {
    param([string]$Tag)

    # Strict regex: V-YYYY.DD.MM or V-YYYY.DD.MM-patch
    $pattern = '^V-\d{4}\.(0[1-9]|[12]\d|3[01])\.(0[1-9]|1[0-2])(-([1-9]\d*))?$'

    if ($Tag -notmatch $pattern) {
        Write-ErrorMsg "Invalid tag format: $Tag"
        Write-Host ""
        Write-Host "Expected format: V-YYYY.DD.MM ou V-YYYY.DD.MM-patch" -ForegroundColor Yellow
        Write-Host "Valid examples:" -ForegroundColor Yellow
        Write-Host "  - V-2024.31.12" -ForegroundColor Gray
        Write-Host "  - V-2024.15.01-1" -ForegroundColor Gray
        Write-Host "  - V-2024.30.06-42" -ForegroundColor Gray
        return $false
    }

    return $true
}

function Test-WorkingDirectoryClean {
    # Explicitly exclude temporary build files
    $status = git status --porcelain --untracked-files=no

    if ($status) {
        Write-ErrorMsg "Uncommitted modifications are present in the working directory"
        Write-Host ""
        git status --short
        Write-Host ""
        Write-Warning "The release requires a clean working directory on the main branch."
        Write-Info "Please commit or stash your modifications before continuing."
        return $false
    }

    Write-Success "Working directory clean ✓"
    return $true
}

function Test-TagExists {
    param([string]$Tag)

    $existingTag = git tag -l $Tag
    if ($existingTag) {
        return $true
    }
    return $false
}

# ============================================================================
# Main script start
# ============================================================================

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  ZoopMod Release Builder" -ForegroundColor White
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Prerequisites check
Write-Step "Prerequisites check"

if (-not (Test-Path $ProjectFile)) {
    Write-ErrorMsg "Project file not found: $ProjectFile"
    Write-Info "Make sure to run this script from the project root directory"
    exit 1
}

if (-not (Test-GitRepository)) {
    Write-ErrorMsg "This is not a valid Git repository"
    exit 1
}

if (-not (Test-GitHubCLI)) {
    Write-ErrorMsg "GitHub CLI (gh) is not installed"
    Write-Info "Install it with: winget install --id GitHub.cli"
    exit 1
}

Write-Success "All prerequisites satisfied"

# Branch verification
Write-Step "Git branch verification"
if (-not (Test-OnMainBranch)) {
    exit 1
}

# Working directory verification
Write-Step "Git status check"
if (-not (Test-WorkingDirectoryClean)) {
    exit 1
}

# DLL verification (must be manually compiled BEFORE)
Write-Step "Compiled DLL verification"
if (-not (Test-Path $DllPath)) {
    Write-ErrorMsg "DLL not found: $DllPath"
    Write-Host ""
    Write-Warning "⚠️  The script does NOT automatically compile the project."
    Write-Info "Manually compile in Release mode in Visual Studio before running this script."
    Write-Host ""
    Write-Info "Or use the command:"
    Write-Host "  dotnet build -c Release" -ForegroundColor Gray
    exit 1
}

$dllInfo = Get-Item $DllPath
Write-Success "DLL found: $DllPath"
Write-Info "  Size: $([math]::Round($dllInfo.Length / 1KB, 2)) KB"
Write-Info "  Modified: $($dllInfo.LastWriteTime)"

# Automatic version detection from DLL
Write-Step "Version detection from ZoopMod.info"
$version = Get-VersionFromModInfo -DllPath $DllPath

# Tag construction
if ($PatchVersion -gt 0) {
    $TagName = "V-$version-$PatchVersion"
    Write-Info "Tag with patch: $TagName"
} else {
    $TagName = "V-$version"
    Write-Info "Tag: $TagName"
}

Write-Step "Tag validation: $TagName"
if (-not (Test-TagFormat $TagName)) {
    exit 1
}
Write-Success "Valid tag format"

# Check if tag already exists
if (Test-TagExists $TagName) {
    Write-ErrorMsg "Tag $TagName already exists. Please use -PatchVersion to create a patch."
    exit 1
}

# Package preparation
Write-Step "Release package preparation.\CreateRelease.ps1 -DryRun.\CreateRelease.ps1 -DryRun.\CreateRelease.ps1 -DryRun"

$releaseDir = Join-Path $OutputDir $ModName
$zipPath = "$ModName-$TagName.zip"

# Clean up old release folder
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}

# Create release folder
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

# Copy files
Write-Info "Copying files..."

Copy-Item -Path "ZoopMod.info" -Destination $releaseDir -ErrorAction Stop
Write-Success "  ✓ ZoopMod.info"

Copy-Item -Path $DllPath -Destination $releaseDir -ErrorAction Stop
Write-Success "  ✓ ZoopMod.dll"

if (Test-Path "About") {
    Copy-Item -Path "About" -Destination "$releaseDir\About" -Recurse -ErrorAction Stop
    Write-Success "  ✓ About\"
}

if (Test-Path "GameData") {
    Copy-Item -Path "GameData" -Destination "$releaseDir\GameData" -Recurse -ErrorAction Stop
    Write-Success "  ✓ GameData\"
}

# Create ZIP
Write-Info "Creating ZIP file..."
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path $releaseDir -DestinationPath $zipPath -CompressionLevel Optimal

$zipInfo = Get-Item $zipPath
Write-Success "Package created: $zipPath ($([math]::Round($zipInfo.Length / 1KB, 2)) KB)"

# Dry Run mode
if ($DryRun) {
    Write-Host ""
    Write-Warning "DRY RUN MODE - No GitHub publication"
    Write-Info "Package is ready in: $zipPath"
    Write-Info "To publish for real, rerun without the -DryRun parameter"
    exit 0
}

# Git tag creation
Write-Step "Git tag creation"
try {
    git tag -a $TagName -m "Release $TagName"
    Write-Success "Tag created: $TagName"
} catch {
    Write-ErrorMsg "Error creating tag: $_"
    exit 1
}

# GitHub publication
Write-Step "GitHub release publication"

$releaseBody = @"
## ZoopMod $TagName

### Installation
1. Download the ``$ModName-$TagName.zip`` file
2. Extract the contents into your Stationeers mods folder
3. Launch the game

### Included Files
- ZoopMod.dll
- ZoopMod.info
- About and GameData folders
"@

try {
    Write-Info "Pushing tag to GitHub..."
    git push origin $TagName

    Write-Info "Creating GitHub release..."
    gh release create $TagName $zipPath `
        --title "Release $TagName" `
        --notes $releaseBody

    Write-Success "Release published successfully!"

    # Get release URL
    $releaseUrl = gh release view $TagName --json url --jq .url
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  🎉 Release $TagName created successfully!" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Info "Release URL: $releaseUrl"
    Write-Host ""

} catch {
    Write-ErrorMsg "Error during publication: $_"
    Write-Warning "Tag was created locally but the GitHub release failed"
    Write-Info "You can retry manually with:"
    Write-Host "  gh release create $TagName $zipPath --title `"Release $TagName`"" -ForegroundColor Gray
    exit 1
}

# Automatic cleanup of release folder (already ignored by .gitignore)
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
    Write-Success "Release folder cleaned"
}

Write-Host ""
Write-Success "Script completed successfully!"








