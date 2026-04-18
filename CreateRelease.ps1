<#
.SYNOPSIS
    Crée une release locale de ZoopMod et la publie sur GitHub

.DESCRIPTION
    Ce script :
    1. Compile le projet en mode Release
    2. Valide le format du tag (V-YYYY.DD.MM ou V-YYYY.DD.MM-patch)
    3. Package les fichiers dans un ZIP
    4. Crée un tag Git
    5. Publie la release sur GitHub

.PARAMETER TagName
    Nom du tag (format: V-YYYY.DD.MM ou V-YYYY.DD.MM-patch)
    Si non spécifié, utilise la date du jour : V-YYYY.DD.MM

.PARAMETER SkipBuild
    Si présent, ne recompile pas le projet (utilise le DLL existant)

.PARAMETER DryRun
    Si présent, effectue toutes les étapes sauf la publication GitHub

.EXAMPLE
    .\CreateRelease.ps1
    Crée une release avec le tag de la date du jour (ex: V-2026.18.04)

.EXAMPLE
    .\CreateRelease.ps1 -TagName "V-2026.18.04-1"
    Crée une release avec un patch

.EXAMPLE
    .\CreateRelease.ps1 -SkipBuild
    Utilise le DLL déjà compilé sans recompiler

.EXAMPLE
    .\CreateRelease.ps1 -DryRun
    Test complet sans publier sur GitHub
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

# Fonction pour lire le chemin du DLL depuis ZoopMod.VS.props
function Get-ModDllPath {
    $propsFile = "ZoopMod.VS.props"

    if (-not (Test-Path $propsFile)) {
        Write-Host "❌ Fichier ZoopMod.VS.props introuvable" -ForegroundColor Red
        Write-Host ""
        Write-Host "Ce fichier est nécessaire pour localiser le DLL compilé." -ForegroundColor Yellow
        Write-Host "Copiez ZoopMod.VS.props.example vers ZoopMod.VS.props et configurez les chemins." -ForegroundColor Yellow
        exit 1
    }

    try {
        # Charger le XML
        [xml]$props = Get-Content $propsFile

        # Extraire StationeersModOutputFolder (chercher dans tous les PropertyGroup)
        $outputFolder = $null
        foreach ($propGroup in $props.Project.PropertyGroup) {
            if ($propGroup.StationeersModOutputFolder) {
                $outputFolder = $propGroup.StationeersModOutputFolder.InnerText
                break
            }
        }

        if (-not $outputFolder) {
            Write-Host "❌ StationeersModOutputFolder non trouvé dans ZoopMod.VS.props" -ForegroundColor Red
            exit 1
        }

        # Résoudre les variables d'environnement comme %USERNAME%
        $outputFolder = [System.Environment]::ExpandEnvironmentVariables($outputFolder)

        # Résoudre les variables MSBuild comme $(RootFolder)
        # Simple approche: remplacer $(RootFolder) si défini
        if ($outputFolder -match '\$\(.*\)') {
            foreach ($propGroup in $props.Project.PropertyGroup) {
                if ($propGroup.RootFolder) {
                    $rootFolder = $propGroup.RootFolder.InnerText
                    $outputFolder = $outputFolder -replace '\$\(RootFolder\)', $rootFolder
                }
            }
        }

        # Construire le chemin complet
        $dllPath = Join-Path $outputFolder "$ModName\$ModName.dll"

        return $dllPath
    } catch {
        Write-Host "❌ Erreur lors de la lecture de ZoopMod.VS.props: $_" -ForegroundColor Red
        exit 1
    }
}

$DllPath = Get-ModDllPath

# ============================================================================
# Fonctions utilitaires
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

    # Le fichier .info est dans le même dossier que le DLL
    $dllDir = Split-Path -Parent $DllPath
    $infoPath = Join-Path $dllDir "ZoopMod.info"

    if (-not (Test-Path $infoPath)) {
        Write-ErrorMsg "Fichier ZoopMod.info introuvable: $infoPath"
        Write-Host ""
        Write-Warning "Le fichier .info devrait être copié automatiquement lors de la compilation."
        Write-Info "Compilez manuellement le projet en mode Release dans Visual Studio."
        exit 1
    }

    try {
        $content = Get-Content $infoPath -Raw | ConvertFrom-Json
        $version = $content._version

        if (-not $version) {
            Write-ErrorMsg "Version non trouvée dans ZoopMod.info"
            exit 1
        }

        Write-Success "Version détectée depuis ZoopMod.info: $version"
        return $version
    } catch {
        Write-ErrorMsg "Erreur lors de la lecture de ZoopMod.info: $_"
        Write-Info "Assurez-vous que le fichier est au format JSON valide."
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
        Write-ErrorMsg "Vous devez être sur la branche 'main' pour créer une release"
        Write-Info "Branche actuelle: $currentBranch"
        Write-Host ""
        Write-Info "Pour basculer sur main:"
        Write-Host "  git checkout main" -ForegroundColor Gray
        return $false
    }
    Write-Success "Branche: $currentBranch ✓"
    return $true
}

function Test-TagFormat {
    param([string]$Tag)

    # Regex stricte: V-YYYY.DD.MM ou V-YYYY.DD.MM-patch
    $pattern = '^V-\d{4}\.(0[1-9]|[12]\d|3[01])\.(0[1-9]|1[0-2])(-([1-9]\d*))?$'

    if ($Tag -notmatch $pattern) {
        Write-ErrorMsg "Format de tag invalide: $Tag"
        Write-Host ""
        Write-Host "Format attendu: V-YYYY.DD.MM ou V-YYYY.DD.MM-patch" -ForegroundColor Yellow
        Write-Host "Exemples valides:" -ForegroundColor Yellow
        Write-Host "  - V-2024.31.12" -ForegroundColor Gray
        Write-Host "  - V-2024.15.01-1" -ForegroundColor Gray
        Write-Host "  - V-2024.30.06-42" -ForegroundColor Gray
        return $false
    }

    return $true
}

function Test-WorkingDirectoryClean {
    # Exclure explicitement les fichiers de build temporaires
    $status = git status --porcelain --untracked-files=no

    if ($status) {
        Write-ErrorMsg "Des modifications non commitées sont présentes dans le répertoire de travail"
        Write-Host ""
        git status --short
        Write-Host ""
        Write-Warning "La release nécessite un working directory propre sur la branche main."
        Write-Info "Veuillez committer ou stasher vos modifications avant de continuer."
        return $false
    }

    Write-Success "Working directory propre ✓"
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
# Début du script principal
# ============================================================================

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  ZoopMod Release Builder" -ForegroundColor White
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Vérification des prérequis
Write-Step "Vérification des prérequis"

if (-not (Test-Path $ProjectFile)) {
    Write-ErrorMsg "Fichier de projet introuvable: $ProjectFile"
    Write-Info "Assurez-vous d'exécuter ce script depuis le répertoire racine du projet"
    exit 1
}

if (-not (Test-GitRepository)) {
    Write-ErrorMsg "Ce n'est pas un dépôt Git valide"
    exit 1
}

if (-not (Test-GitHubCLI)) {
    Write-ErrorMsg "GitHub CLI (gh) n'est pas installé"
    Write-Info "Installez-le avec: winget install --id GitHub.cli"
    exit 1
}

Write-Success "Tous les prérequis sont satisfaits"

# Vérification de la branche
Write-Step "Vérification de la branche Git"
if (-not (Test-OnMainBranch)) {
    exit 1
}

# Vérification du working directory
Write-Step "Vérification de l'état Git"
if (-not (Test-WorkingDirectoryClean)) {
    exit 1
}

# Vérification du DLL (doit être compilé manuellement AVANT)
Write-Step "Vérification du DLL compilé"
if (-not (Test-Path $DllPath)) {
    Write-ErrorMsg "DLL introuvable: $DllPath"
    Write-Host ""
    Write-Warning "⚠️  Le script ne compile PAS automatiquement le projet."
    Write-Info "Compilez manuellement en mode Release dans Visual Studio avant d'exécuter ce script."
    Write-Host ""
    Write-Info "Ou utilisez la commande:"
    Write-Host "  dotnet build -c Release" -ForegroundColor Gray
    exit 1
}

$dllInfo = Get-Item $DllPath
Write-Success "DLL trouvé: $DllPath"
Write-Info "  Taille: $([math]::Round($dllInfo.Length / 1KB, 2)) KB"
Write-Info "  Modifié: $($dllInfo.LastWriteTime)"

# Détection automatique de la version depuis le DLL
Write-Step "Détection de la version depuis ZoopMod.info"
$version = Get-VersionFromModInfo -DllPath $DllPath

# Construction du tag
if ($PatchVersion -gt 0) {
    $TagName = "V-$version-$PatchVersion"
    Write-Info "Tag avec patch: $TagName"
} else {
    $TagName = "V-$version"
    Write-Info "Tag: $TagName"
}

Write-Step "Validation du tag: $TagName"
if (-not (Test-TagFormat $TagName)) {
    exit 1
}
Write-Success "Format du tag valide"

# Vérification si le tag existe déjà
if (Test-TagExists $TagName) {
    Write-ErrorMsg "Le tag $TagName existe déjà. Veuillez utiliser -PatchVersion pour créer un patch."
    exit 1
}

# Préparation du package
Write-Step "Préparation du package de release.\CreateRelease.ps1 -DryRun.\CreateRelease.ps1 -DryRun.\CreateRelease.ps1 -DryRun"

$releaseDir = Join-Path $OutputDir $ModName
$zipPath = "$ModName-$TagName.zip"

# Nettoyage de l'ancien dossier de release
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}

# Création du dossier de release
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

# Copie des fichiers
Write-Info "Copie des fichiers..."

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

# Création du ZIP
Write-Info "Création du fichier ZIP..."
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path $releaseDir -DestinationPath $zipPath -CompressionLevel Optimal

$zipInfo = Get-Item $zipPath
Write-Success "Package créé: $zipPath ($([math]::Round($zipInfo.Length / 1KB, 2)) KB)"

# Mode Dry Run
if ($DryRun) {
    Write-Host ""
    Write-Warning "MODE DRY RUN - Aucune publication sur GitHub"
    Write-Info "Le package est prêt dans: $zipPath"
    Write-Info "Pour publier pour de vrai, relancez sans le paramètre -DryRun"
    exit 0
}

# Création du tag Git
Write-Step "Création du tag Git"
try {
    git tag -a $TagName -m "Release $TagName"
    Write-Success "Tag créé: $TagName"
} catch {
    Write-ErrorMsg "Erreur lors de la création du tag: $_"
    exit 1
}

# Publication sur GitHub
Write-Step "Publication de la release sur GitHub"

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
    Write-Info "Push du tag vers GitHub..."
    git push origin $TagName

    Write-Info "Création de la release GitHub..."
    gh release create $TagName $zipPath `
        --title "Release $TagName" `
        --notes $releaseBody

    Write-Success "Release publiée avec succès!"

    # Récupérer l'URL de la release
    $releaseUrl = gh release view $TagName --json url --jq .url
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  🎉 Release $TagName créée avec succès!" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Info "URL de la release: $releaseUrl"
    Write-Host ""

} catch {
    Write-ErrorMsg "Erreur lors de la publication: $_"
    Write-Warning "Le tag a été créé localement mais la release GitHub a échoué"
    Write-Info "Vous pouvez réessayer manuellement avec:"
    Write-Host "  gh release create $TagName $zipPath --title `"Release $TagName`"" -ForegroundColor Gray
    exit 1
}

# Nettoyage automatique du dossier de release (déjà ignoré par .gitignore)
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
    Write-Success "Dossier de release nettoyé"
}

Write-Host ""
Write-Success "Script terminé avec succès!"

