<#
.SYNOPSIS
    Télécharge VLC portable (version épinglée dans vlc.json), vérifie son empreinte SHA-256 et l'installe
    dans <Destination>/tools/VLC, d'où l'export d'un coffret USB le copie sur le disque offert.
.DESCRIPTION
    Comme pour MediaInfo : rien n'est installé sur le système, toute empreinte différente de celle du
    manifeste arrête le script. La licence de VLC (COPYING.txt, GNU GPL) est livrée avec le lecteur.
    Compatible Windows PowerShell 5.1 et PowerShell 7.
.EXAMPLE
    ./build/fetch-vlc.ps1 -Rid win-x64 -Destination publish/coffret-usb/win-x64
#>
param(
    [Parameter(Mandatory = $true)][string]$Rid,
    [Parameter(Mandatory = $true)][string]$Destination,
    [string]$Cache = (Join-Path ([System.IO.Path]::GetTempPath()) 'cinedex-vlc-cache')
)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'vlc.json') | ConvertFrom-Json
$package = $manifest.packages.$Rid
if ($null -eq $package) { throw "Plate-forme non prise en charge pour VLC portable : $Rid" }

New-Item -ItemType Directory -Force -Path $Cache | Out-Null
$archive = Join-Path $Cache (Split-Path $package.file -Leaf)
if (-not (Test-Path $archive)) {
    $url = $manifest.source + $package.file
    Write-Host "Téléchargement de $url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile "$archive.part"
    Move-Item -Force "$archive.part" $archive
}

$hash = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
if ($hash -ne $package.sha256) {
    Remove-Item -Force $archive
    throw "Empreinte SHA-256 invalide pour $($package.file) : attendu $($package.sha256), obtenu $hash. Fichier supprimé."
}
Write-Host "Empreinte vérifiée : $hash"

$work = Join-Path $Cache ("extract-" + $Rid)
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force -Path $work | Out-Null
Expand-Archive -Path $archive -DestinationPath $work -Force

$source = Join-Path $work $package.folder
if (-not (Test-Path (Join-Path $source 'vlc.exe'))) { throw "vlc.exe introuvable dans l'archive ($($package.folder))." }
if (-not (Test-Path (Join-Path $source 'COPYING.txt'))) { throw "Licence de VLC (COPYING.txt) introuvable dans l'archive." }

$target = Join-Path $Destination (Join-Path 'tools' 'VLC')
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Recurse -Path (Join-Path $source '*') -Destination $target

Set-Content -Path (Join-Path $target 'VERSION') -Value "VLC $($manifest.version) ($($package.file), sha256 $hash) - $($manifest.license)"
Remove-Item -Recurse -Force $work
Write-Host "VLC $($manifest.version) installé dans $target"
