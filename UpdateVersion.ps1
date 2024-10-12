# UpdateVersion.ps1
# Script to update version numbers in About.xml and ZoopMod.info

# Get the current date in the format YYYY.DD.MM
$currentDate = (Get-Date).ToString("yyyy.dd.MM")

# Update the version in About/About.xml
$aboutFile = "About/About.xml"
$xmldata = [XML](Get-Content $aboutFile)
$xmldata.ModMetadata.Version = $currentDate
$xmldata.PreserveWhitespace = $true
$xmldata. Save($aboutFile)

# Update the version in ZoopMod.info
$infoFile = "ZoopMod.info"
$json = Get-Content $infoFile | ConvertFrom-Json 
$json._version = $currentDate
$json | ConvertTo-Json | Out-File $infoFile