<#
.SYNOPSIS
    Produit le programme d'installation de Cinedex et les paquets de mise à jour automatique (Velopack) pour une
    plate-forme : Cinedex-win-Setup.exe sous Windows, AppImage sous Linux, .pkg sous macOS.
.DESCRIPTION
    L'application est publiée de façon autonome (aucun .NET à installer), MediaInfo CLI est ajouté, puis vpk crée
    l'installation, le paquet complet et, si une version précédente est présente dans le dossier de sortie, un
    paquet différentiel (seuls les fichiers modifiés sont téléchargés lors de la mise à jour).

    Signature du code (facultative) : définir CINEDEX_SIGN_PARAMS avec les paramètres de signtool, par exemple
    '/fd sha256 /tr http://timestamp.digicert.com /td sha256 /f cert.pfx /p motdepasse'. Sans signature, Windows
    affiche l'avertissement SmartScreen au premier lancement de l'installation.
.EXAMPLE
    ./build/installer.ps1 -Rid win-x64
    ./build/installer.ps1 -Rid linux-x64 -Output ./releases
#>
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish/installer'),
    [string]$Dotnet = 'dotnet',
    [string]$Vpk = 'vpk'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$channel = switch -Wildcard ($Rid) { 'win-*' { 'win' } 'linux-*' { 'linux' } default { 'osx' } }
$staging = Join-Path ([IO.Path]::GetTempPath()) "cinedex-installer-$Rid"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }

# Publication autonome en dossier (Velopack calcule les différences fichier par fichier).
& $Dotnet publish (Join-Path $root 'src/Cinedex.App') -c Release -r $Rid --self-contained -p:DebugType=none -o $staging
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication pour $Rid" }
Get-ChildItem -Path $staging -Filter '*.pdb' | Remove-Item -Force
& (Join-Path $PSScriptRoot 'fetch-mediainfo.ps1') -Rid $Rid -Destination $staging
Copy-Item (Join-Path $root 'README.md') $staging

$mainExe = if ($channel -eq 'win') { 'Cinedex.exe' } else { 'Cinedex' }
$png = Join-Path $root 'src/Cinedex.App/Assets/cinedex.png'
$icon = switch ($channel) {
    'win' { Join-Path $root 'src/Cinedex.App/Assets/cinedex.ico' }
    'osx' {
        # macOS exige une icône .icns : fabriquée à partir du PNG avec les outils du système (sips, iconutil).
        $iconset = Join-Path ([IO.Path]::GetTempPath()) 'cinedex.iconset'
        if (Test-Path $iconset) { Remove-Item -Recurse -Force $iconset }
        New-Item -ItemType Directory -Path $iconset | Out-Null
        foreach ($size in 16, 32, 128, 256, 512) {
            & sips -z $size $size $png --out (Join-Path $iconset "icon_${size}x${size}.png") | Out-Null
            & sips -z ($size * 2) ($size * 2) $png --out (Join-Path $iconset "icon_${size}x${size}@2x.png") | Out-Null
        }
        $icns = Join-Path ([IO.Path]::GetTempPath()) 'cinedex.icns'
        & iconutil -c icns $iconset -o $icns
        if ($LASTEXITCODE -ne 0) { throw "Échec de la création de l'icône macOS" }
        $icns
    }
    default { $png }
}
$arguments = @(
    'pack', '--packId', 'Cinedex', '--packVersion', $version, '--packDir', $staging, '--mainExe', $mainExe,
    '--packTitle', 'Cinedex', '--packAuthors', 'Cinedex', '--icon', $icon, '--channel', $channel,
    '--runtime', $Rid, '--outputDir', $Output
)
$notes = Join-Path $root 'docs/release-notes.md'
if (Test-Path $notes) { $arguments += @('--releaseNotes', $notes) }
if ($env:CINEDEX_SIGN_PARAMS -and $channel -eq 'win') { $arguments += @('--signParams', $env:CINEDEX_SIGN_PARAMS) }

& $Vpk @arguments
if ($LASTEXITCODE -ne 0) { throw "Échec de vpk pack pour $Rid" }
Remove-Item -Recurse -Force $staging
Write-Host "Installation et paquets de mise à jour : $Output"
