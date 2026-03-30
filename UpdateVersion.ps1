# UpdateVersion.ps1
# Updates version numbers in About/About.xml and ZoopMod.info while preserving each file's encoding and formatting.

function Get-FileEncoding {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)

    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return [System.Text.UTF8Encoding]::new($true)
    }

    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        return [System.Text.UnicodeEncoding]::new($false, $true)
    }

    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
        return [System.Text.UnicodeEncoding]::new($true, $true)
    }

    return [System.Text.UTF8Encoding]::new($false)
}

function Update-VersionString {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$Replacement
    )

    $encoding = Get-FileEncoding -Path $Path
    $content = [System.IO.File]::ReadAllText($Path, $encoding)
    $match = [System.Text.RegularExpressions.Regex]::Match($content, $Pattern)
    if (-not $match.Success) {
        throw "Failed to update version in $Path. Pattern not found: $Pattern"
    }

    $updatedContent = [System.Text.RegularExpressions.Regex]::Replace($content, $Pattern, $Replacement, 1)

    if ($content -ne $updatedContent) {
        [System.IO.File]::WriteAllText($Path, $updatedContent, $encoding)
    }
}

# Get the current date in the format YYYY.DD.MM
$currentDate = (Get-Date).ToString("yyyy.dd.MM")

# Update the version in About/About.xml
Update-VersionString `
    -Path "About/About.xml" `
    -Pattern '<Version>[^<]+</Version>' `
    -Replacement "<Version>$currentDate</Version>"

# Update the version in ZoopMod.info
Update-VersionString `
    -Path "ZoopMod.info" `
    -Pattern '("_version"\s*:\s*")[^"]+(")' `
    -Replacement "`${1}$currentDate`${2}"
