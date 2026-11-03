<#
.SYNOPSIS
    Ajoute l'APK Android à une release GitHub déjà publiée par le workflow « Paquets » (étiquette vX.Y.Z).

.DESCRIPTION
    L'APK est construit en local (build/android.ps1) : il est signé avec la même clé à chaque version, si bien qu'une
    nouvelle version s'installe par-dessus la précédente. Jeton : variable GITHUB_TOKEN, sinon celui du gestionnaire
    d'identifiants de git (jamais affiché).
#>
param(
    [string]$Repository = 'webbackuper/cinedex',
    [string]$Folder = (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$tag = "v$version"
$files = @("Cinedex-$version-android.apk", "Cinedex-$version-android.apk.sha256") | ForEach-Object { Join-Path $Folder $_ }
foreach ($file in $files) { if (-not (Test-Path $file)) { throw "Fichier absent : $file (lancer build/android.ps1)" } }

$token = $env:GITHUB_TOKEN
if (-not $token) {
    # Demande passée par un fichier (sans BOM, fins de ligne LF) : le tube de Windows PowerShell 5.1 la déforme.
    $request = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText($request, "protocol=https`nhost=github.com`n`n")
    $line = cmd /c "git credential fill < `"$request`"" | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
    Remove-Item $request
    if ($line) { $token = $line.Substring('password='.Length) }
}
if (-not $token) { throw "Aucun jeton GitHub disponible" }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json' }

$release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/tags/$tag"
foreach ($file in $files) {
    $name = Split-Path $file -Leaf
    # Un envoi précédent du même fichier est remplacé.
    $existing = $release.assets | Where-Object { $_.name -eq $name }
    if ($existing) { Invoke-RestMethod -Headers $headers -Method Delete -Uri $existing.url | Out-Null }
    $type = if ($name.EndsWith('.apk')) { 'application/vnd.android.package-archive' } else { 'text/plain' }
    Invoke-RestMethod -Headers $headers -Method Post -ContentType $type -InFile $file `
        -Uri "https://uploads.github.com/repos/$Repository/releases/$($release.id)/assets?name=$name" | Out-Null
    Write-Host "Ajouté à $tag : $name"
}
