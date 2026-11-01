<#
.SYNOPSIS
    Produit un paquet autonome de Bobine pour une plate-forme : exécutable unique + MediaInfo CLI embarqué,
    puis une archive et son empreinte SHA-256.
.EXAMPLE
    ./build/package.ps1 -Rid win-x64
    ./build/package.ps1 -Rid linux-x64
    ./build/package.ps1 -Rid osx-arm64
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Rid,
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish'),
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $Output $Rid
if (Test-Path $target) { Remove-Item -Recurse -Force $target }

& $Dotnet publish (Join-Path $root 'src/Bobine.App') -c Release -r $Rid --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $target
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication pour $Rid" }
# Symboles de débogage des bibliothèques natives (SkiaSharp, HarfBuzz) : inutiles à l'utilisateur, ~100 Mo.
Get-ChildItem -Path $target -Filter '*.pdb' | Remove-Item -Force

& (Join-Path $PSScriptRoot 'fetch-mediainfo.ps1') -Rid $Rid -Destination $target

Copy-Item (Join-Path $root 'README.md') $target
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$archive = Join-Path $Output "Bobine-$version-$Rid.zip"
if (Test-Path $archive) { Remove-Item -Force $archive }
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $archive
$hash = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
Set-Content -Path "$archive.sha256" -Value "$hash  $(Split-Path $archive -Leaf)"
Write-Host "Paquet : $archive"
Write-Host "SHA-256 : $hash"
