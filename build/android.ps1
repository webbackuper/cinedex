<#
.SYNOPSIS
    Construit l'APK Android de Cinedex (consultation d'un catalogue importé depuis une sauvegarde .zip).

.DESCRIPTION
    Prérequis, une seule fois : la charge de travail .NET Android (dotnet workload install android), puis le JDK et le
    SDK Android, que la cible InstallAndroidDependencies télécharge dans %LOCALAPPDATA%\Android :
      dotnet build src/Cinedex.Android -t:InstallAndroidDependencies -f net10.0-android `
        -p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk -p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk `
        -p:AcceptAndroidSDKLicenses=True
    L'APK est signé avec la clé de débogage locale de .NET Android : il s'installe à la main sur un téléphone
    (« sources inconnues »), pas sur le Play Store.
#>
param(
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish'),
    [string]$Dotnet = 'dotnet',
    [string]$AndroidSdk = (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
    [string]$JavaSdk = (Join-Path $env:LOCALAPPDATA 'Android\jdk')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$staging = Join-Path ([IO.Path]::GetTempPath()) 'cinedex-android'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }

& $Dotnet publish (Join-Path $root 'src/Cinedex.Android') -c Release -f net10.0-android `
    "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$JavaSdk" -o $staging
if ($LASTEXITCODE -ne 0) { throw "Échec de la construction Android" }

$apk = Get-ChildItem -Path $staging -Filter '*-Signed.apk' | Select-Object -First 1
if (-not $apk) { throw "APK signé introuvable dans $staging" }
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$target = Join-Path $Output "Cinedex-$version-android.apk"
Copy-Item $apk.FullName $target -Force
$hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$target.sha256" -Value "$hash  $(Split-Path $target -Leaf)" -Encoding ascii
Remove-Item -Recurse -Force $staging
Write-Host "APK : $target"
Write-Host "SHA-256 : $hash"
