# Guide de Release ZoopMod

Ce guide explique comment créer et publier une release de ZoopMod **depuis votre machine locale** en utilisant le script PowerShell automatisé.

## 🔐 Prérequis

Avant de créer votre première release, assurez-vous d'avoir :

### 1. GitHub CLI installé et authentifié
```powershell
# Installer GitHub CLI (si nécessaire)
winget install --id GitHub.cli

# S'authentifier
gh auth login
```

### 2. Vérifier l'authentification
```powershell
gh auth status
```

Vous devez voir : `✓ Logged in to github.com as <votre-username>`

### 3. Être sur la branche main
```powershell
git checkout main
git pull origin main
```

### 4. Working directory propre
Toutes les modifications doivent être commitées avant de créer une release. Le script refusera de continuer si des fichiers modifiés sont détectés.

---

## 🚀 Utilisation du script

⚠️ **IMPORTANT** : Le script **ne compile JAMAIS** le projet. Vous devez compiler manuellement en mode Release dans Visual Studio avant d'exécuter le script.

Le script `CreateRelease.ps1` **détecte automatiquement** la version depuis le **DLL déjà compilé** et crée le tag correspondant.

### Workflow complet

1️⃣ **Compiler le projet en mode Release** dans Visual Studio
2️⃣ **Exécuter le script de release**

### Release standard (version du DLL)
```powershell
# D'abord compiler dans Visual Studio (Release mode)
# Puis lancer le script
.\CreateRelease.ps1
```

✅ Détecte automatiquement la version depuis `bin\Release\net48\ZoopMod.dll`  
✅ Crée le tag `V-2026.18.04` si le DLL a la version `2026.18.04`

### Release avec patch
```powershell
.\CreateRelease.ps1 -PatchVersion 1
```

✅ Crée le tag `V-2026.18.04-1` sans modifier le DLL

### Mode test (Dry Run)
```powershell
.\CreateRelease.ps1 -DryRun
```

✅ Effectue toutes les étapes **sauf** la publication sur GitHub  
✅ Idéal pour vérifier que tout fonctionne avant de publier

---

## 📋 Workflow complet

Le script effectue automatiquement :

1. ✅ **Vérification des prérequis** : Git, GitHub CLI, fichiers du projet
2. ✅ **Vérification de la branche** : doit être `main`
3. ✅ **Vérification Git status** : doit être propre (pas de fichiers modifiés)
4. ✅ **Vérification du DLL** : doit exister dans `bin\Release\net48\ZoopMod.dll`
5. ✅ **Détection de la version** depuis l'assembly du DLL
6. ✅ **Construction du tag** : `V-<version>` ou `V-<version>-<patch>`
7. ✅ **Validation du format** du tag
8. ✅ **Packaging** : création du ZIP avec la structure correcte
9. ✅ **Création du tag Git** local
10. ✅ **Push du tag** vers GitHub
11. ✅ **Publication de la release** avec le ZIP attaché
12. ✅ **Nettoyage automatique** du dossier `release/`

⚠️ **Le script ne compile JAMAIS le projet** — vous devez compiler manuellement en mode Release avant de l'exécuter.

---

## 🔧 Intégration avec Visual Studio

Vous pouvez lancer le script directement depuis Visual Studio :

### Étapes de configuration

1. **Tools** → **External Tools...**
2. Cliquez sur **Add**
3. Configurez :
   - **Title** : `Create Release`
   - **Command** : `pwsh.exe`
   - **Arguments** : `-NoProfile -ExecutionPolicy Bypass -File "$(SolutionDir)CreateRelease.ps1"`
   - **Initial directory** : `$(SolutionDir)`
   - ☑️ **Use Output window**
   - ☑️ **Prompt for arguments** (si vous voulez spécifier `-PatchVersion` à chaque fois)

4. Cliquez sur **OK**

### Utilisation dans Visual Studio

- **Tools** → **Create Release**
- Si vous avez activé "Prompt for arguments", vous pouvez ajouter `-PatchVersion 1` ou `-DryRun`

---

## 🎯 Format des tags

Le script accepte **uniquement** le format suivant (détecté depuis `ZoopMod.info`) :

- `V-YYYY.DD.MM` (ex: `V-2026.18.04`)
- `V-YYYY.DD.MM-<patch>` (ex: `V-2026.18.04-1`, `V-2026.18.04-2`, etc.)

**Note** : L'ordre des dates suit le script `UpdateVersion.ps1` : **année.jour.mois**

---

## ⚠️ Contraintes importantes

### ✅ Branche obligatoire
Le script **refuse** de fonctionner si vous n'êtes pas sur `main`. Cela garantit que seules les releases validées sont publiées.

### ✅ Working directory propre
Le script **refuse** de continuer si des fichiers modifiés (tracked) sont détectés. Cela évite :
- D'inclure du code non commité dans la release
- De créer des tags sur un état incohérent du dépôt

**Les fichiers de build sont automatiquement ignorés** grâce à `.gitignore` :
- `release/` (dossier de packaging temporaire)
- `*.zip` (archives créées)
- `bin/`, `obj/` (artefacts de compilation)

### ✅ Détection de tag existant
Si le tag existe déjà, le script s'arrête immédiatement avec un message clair. Utilisez `-PatchVersion <N>` pour créer une variante.

---

## 🐛 Dépannage

### "DLL introuvable: bin\Release\net48\ZoopMod.dll"
Le script ne compile **jamais** automatiquement. Vous devez compiler manuellement :

**Dans Visual Studio :**
1. Configuration : **Release**
2. Build → Build Solution (Ctrl+Shift+B)

**Ou en ligne de commande :**
```powershell
dotnet build -c Release
```

### "GitHub CLI (gh) n'est pas installé"
```powershell
winget install --id GitHub.cli
gh auth login
```

### "Le tag V-2026.18.04 existe déjà"
Option 1 : Utilisez un patch
```powershell
.\CreateRelease.ps1 -PatchVersion 1
```

Option 2 : Supprimez le tag existant (⚠️ attention)
```powershell
git tag -d V-2026.18.04
git push origin :refs/tags/V-2026.18.04
```

### "Vous devez être sur la branche 'main'"
```powershell
git checkout main
git pull origin main
```

### "Des modifications non commitées sont présentes"
```powershell
# Committer les changements
git add .
git commit -m "Description des modifications"

# OU les stasher temporairement
git stash
# ... faire la release ...
git stash pop
```

### La version détectée n'est pas la bonne
Le script lit la version depuis l'**AssemblyFileVersion** du DLL compilé, pas depuis `ZoopMod.info`.

Vérifiez que vous avez bien compilé en mode **Release** après avoir modifié la version dans votre projet.

---

## 📦 Contenu de la release

Chaque release GitHub contient :

- Un fichier ZIP : `ZoopMod-V-<version>.zip`
- Structure du ZIP :
  ```
  ZoopMod/
    ├── ZoopMod.dll
    ├── ZoopMod.info
    ├── About/
    └── GameData/
  ```

Le ZIP est automatiquement attaché à la release GitHub avec des notes de version en anglais.

---

## 🔗 Liens utiles

- **Releases GitHub** : https://github.com/Nivvdiy/ZoopModRecovered/releases
- **Documentation GitHub CLI** : https://cli.github.com/manual/
- **Script de versioning** : `UpdateVersion.ps1` (référence pour le format de date)

---

**Note** : Tous les artefacts de build (`release/`, `*.zip`, `bin/`, `obj/`) sont automatiquement ignorés par Git grâce au `.gitignore`. Vous ne verrez jamais ces fichiers dans `git status`.
