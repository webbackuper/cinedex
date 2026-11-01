<#
.SYNOPSIS
    Télécharge MediaInfo CLI (version épinglée dans mediainfo.json), vérifie son empreinte SHA-256 et
    l'installe dans <Destination>/tools/MediaInfo, où Cinedex le trouve sans réglage.
.DESCRIPTION
    Aucune installation système : seul le binaire et sa licence sont copiés. Toute empreinte différente
    de celle du manifeste arrête le script (fichier altéré ou substitué).
    Compatible Windows PowerShell 5.1 et PowerShell 7 (Windows, Linux, macOS).
.EXAMPLE
    ./build/fetch-mediainfo.ps1 -Rid win-x64 -Destination publish/win-x64
#>
param(
    [Parameter(Mandatory = $true)][string]$Rid,
    [Parameter(Mandatory = $true)][string]$Destination,
    [string]$Cache = (Join-Path ([System.IO.Path]::GetTempPath()) 'cinedex-mediainfo-cache')
)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'mediainfo.json') | ConvertFrom-Json
$package = $manifest.packages.$Rid
if ($null -eq $package) { throw "Plate-forme non prise en charge pour MediaInfo : $Rid" }

New-Item -ItemType Directory -Force -Path $Cache | Out-Null
$archive = Join-Path $Cache $package.file
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

if ($package.kind -eq 'zip') {
    Expand-Archive -Path $archive -DestinationPath $work -Force
}
elseif ($package.kind -eq 'dmg') {
    # Le .dmg contient un .pkg dont la charge utile contient usr/local/bin/mediainfo (binaire universel).
    if ($IsMacOS) {
        $mount = Join-Path $work 'mnt'
        New-Item -ItemType Directory -Force -Path $mount | Out-Null
        & hdiutil attach -nobrowse -readonly -mountpoint $mount $archive | Out-Null
        try {
            & pkgutil --expand-full (Join-Path $mount 'mediainfo.pkg') (Join-Path $work 'pkg')
            Copy-Item -Recurse (Join-Path $work 'pkg/Payload/*') $work
        }
        finally { & hdiutil detach $mount -quiet | Out-Null }
    }
    else {
        $sevenZip = @('7z', '7za', 'C:\Program Files\7-Zip\7z.exe') | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
        if ($null -eq $sevenZip) { throw "Extraction du .dmg hors macOS : 7-Zip est nécessaire." }
        & $sevenZip e -y "-o$work" $archive 'MediaInfo/mediainfo.pkg' | Out-Null
        # .pkg (xar) -> Payload (gzip) -> Payload~ (cpio) -> usr/local/bin/mediainfo
        & $sevenZip e -y -r "-o$work" (Join-Path $work 'mediainfo.pkg') '*Payload*' | Out-Null
        # Selon la version de 7-Zip, la décompression gzip est déjà faite à l'étape précédente.
        if (Test-Path (Join-Path $work 'Payload')) { & $sevenZip x -y "-o$work" (Join-Path $work 'Payload') | Out-Null }
        & $sevenZip x -y "-o$work" (Join-Path $work 'Payload~') | Out-Null
    }
}
else { throw "Type d'archive inconnu : $($package.kind)" }

$binary = Join-Path $work $package.binary
if (-not (Test-Path $binary)) { throw "Binaire introuvable dans l'archive : $($package.binary)" }

$target = Join-Path $Destination (Join-Path 'tools' 'MediaInfo')
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $binary $target
foreach ($extra in $package.extra) { Copy-Item (Join-Path $work $extra) $target }
if (-not (Test-Path (Join-Path $target 'LICENSE'))) {
    Set-Content -Path (Join-Path $target 'LICENSE') -Value "MediaInfo $($manifest.version) - $($manifest.license) - https://mediaarea.net/en/MediaInfo/License"
}
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { & chmod +x (Join-Path $target (Split-Path $package.binary -Leaf)) }

Set-Content -Path (Join-Path $target 'VERSION') -Value "MediaInfo CLI $($manifest.version) ($($package.file), sha256 $hash)"
Remove-Item -Recurse -Force $work
Write-Host "MediaInfo $($manifest.version) installé dans $target"
