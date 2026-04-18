# ZoopMod Release Guide

This guide explains how to create and publish a ZoopMod release **from your local machine** using the automated PowerShell script.

## 🔐 Prerequisites

Before creating your first release, ensure you have:

### 1. GitHub CLI installed and authenticated
```powershell
# Install GitHub CLI (if needed)
winget install --id GitHub.cli

# Authenticate
gh auth login
```

### 2. Verify authentication
```powershell
gh auth status
```

You should see: `✓ Logged in to github.com as <your-username>`

### 3. Set default repository
```powershell
gh repo set-default Nivvdiy/ZoopModRecovered
```

### 4. Be on the main branch
```powershell
git checkout main
git pull origin main
```

### 5. Clean working directory
All modifications must be committed before creating a release. The script will refuse to continue if modified files are detected.

---

## 🚀 Using the Script

⚠️ **IMPORTANT**: The script **NEVER compiles** the project. You must manually compile in Release mode in Visual Studio before running the script.

The `CreateRelease.ps1` script **automatically detects** the version from the **already compiled DLL** and creates the corresponding tag.

### Complete Workflow

1️⃣ **Compile the project in Release mode** in Visual Studio  
2️⃣ **Run the release script**

### Standard Release (current DLL version)
```powershell
# First compile in Visual Studio (Release mode)
# Then run the script
.\CreateRelease.ps1
```

✅ Automatically detects version from `ZoopMod.info` in the compiled output  
✅ Creates tag `V-2026.18.04` if the info file has version `2026.18.04`

### Release with Patch
```powershell
.\CreateRelease.ps1 -PatchVersion 1
```

✅ Creates tag `V-2026.18.04-1` without modifying the DLL

### Test Mode (Dry Run)
```powershell
.\CreateRelease.ps1 -DryRun
```

✅ Performs all steps **except** GitHub publication  
✅ Ideal for verifying everything works before publishing

---

## 📋 Complete Workflow

The script automatically performs:

1. ✅ **Prerequisites check**: Git, GitHub CLI, project files
2. ✅ **Branch verification**: must be `main`
3. ✅ **Git status check**: must be clean (no modified files)
4. ✅ **DLL verification**: must exist in the configured output folder
5. ✅ **Version detection**: from the compiled `ZoopMod.info`
6. ✅ **Tag construction**: `V-<version>` or `V-<version>-<patch>`
7. ✅ **Format validation**: of the tag
8. ✅ **Packaging**: ZIP creation with correct structure
9. ✅ **Git tag creation**: local
10. ✅ **Tag push**: to GitHub
11. ✅ **Release publication**: with attached ZIP
12. ✅ **Automatic cleanup**: of the `release/` folder

⚠️ **The script NEVER compiles the project** — you must compile manually in Release mode before running it.

---

## 🔧 Visual Studio Integration

You can launch the script directly from Visual Studio:

### Configuration Steps

1. **Tools** → **External Tools...**
2. Click **Add**
3. Configure:
   - **Title**: `Create Release`
   - **Command**: `pwsh.exe`
   - **Arguments**: `-NoProfile -ExecutionPolicy Bypass -File "$(SolutionDir)CreateRelease.ps1"`
   - **Initial directory**: `$(SolutionDir)`
   - ☑️ **Use Output window**
   - ☑️ **Prompt for arguments** (if you want to specify `-PatchVersion` each time)

4. Click **OK**

### Usage in Visual Studio

- **Tools** → **Create Release**
- If you enabled "Prompt for arguments", you can add `-PatchVersion 1` or `-DryRun`

---

## 🎯 Tag Format

The script **only accepts** the following format (detected from `ZoopMod.info`):

- `V-YYYY.DD.MM` (e.g., `V-2026.18.04`)
- `V-YYYY.DD.MM-<patch>` (e.g., `V-2026.18.04-1`, `V-2026.18.04-2`, etc.)

**Note**: The date order follows the `UpdateVersion.ps1` script: **year.day.month**

---

## ⚠️ Important Constraints

### ✅ Mandatory Branch
The script **refuses** to run if you're not on `main`. This ensures only validated releases are published.

### ✅ Clean Working Directory
The script **refuses** to continue if modified (tracked) files are detected. This prevents:
- Including uncommitted code in the release
- Creating tags on an inconsistent repository state

**Build files are automatically ignored** thanks to `.gitignore`:
- `release/` (temporary packaging folder)
- `*.zip` (created archives)
- `bin/`, `obj/` (compilation artifacts)

### ✅ Existing Tag Detection
If the tag already exists, the script stops immediately with a clear message. Use `-PatchVersion <N>` to create a variant.

### ✅ External Build Output
The script reads the DLL path from `ZoopMod.VS.props` to locate the compiled output in your configured Stationeers mods folder (e.g., `D:\Documents\My Games\Stationeers\mods\ZoopMod\`).

---

## 🐛 Troubleshooting

### "DLL not found: ..."
The script **never** compiles automatically. You must compile manually:

**In Visual Studio:**
1. Configuration: **Release**
2. Build → Build Solution (Ctrl+Shift+B)

**Or via command line:**
```powershell
dotnet build -c Release
```

### "GitHub CLI (gh) is not installed"
```powershell
winget install --id GitHub.cli
gh auth login
gh repo set-default Nivvdiy/ZoopModRecovered
```

### "Tag V-2026.18.04 already exists"
Option 1: Use a patch
```powershell
.\CreateRelease.ps1 -PatchVersion 1
```

Option 2: Delete the existing tag (⚠️ caution)
```powershell
git tag -d V-2026.18.04
git push origin :refs/tags/V-2026.18.04
```

### "You must be on the 'main' branch"
```powershell
git checkout main
git pull origin main
```

### "Uncommitted modifications are present"
```powershell
# Commit changes
git add .
git commit -m "Description of modifications"

# OR stash temporarily
git stash
# ... create release ...
git stash pop
```

### "No default remote repository has been set"
```powershell
gh repo set-default Nivvdiy/ZoopModRecovered
```

### Detected version is incorrect
The script reads the version from `ZoopMod.info` in the compiled output folder, not from the project source.

Verify that you've compiled in **Release** mode after modifying the version in your project.

---

## 📦 Release Content

Each GitHub release contains:

- A ZIP file: `ZoopMod-V-<version>.zip`
- ZIP structure:
  ```
  ZoopMod/
    ├── ZoopMod.dll
    ├── ZoopMod.info
    ├── About/
    └── GameData/
  ```

The ZIP is automatically attached to the GitHub release with English release notes.

---

## 🔗 Useful Links

- **GitHub Releases**: https://github.com/Nivvdiy/ZoopModRecovered/releases
- **GitHub CLI Documentation**: https://cli.github.com/manual/
- **Versioning Script**: `UpdateVersion.ps1` (reference for date format)

---

**Note**: All build artifacts (`release/`, `*.zip`, `bin/`, `obj/`) are automatically ignored by Git thanks to `.gitignore`. You will never see these files in `git status`.
