# Cinedex

Catalogue de films et de séries, multiplateforme (Windows, macOS, Linux). Cinedex reprend les fonctionnalités d'un gestionnaire de collection vidéo de bureau : fiches enrichies depuis TMDb, scan des dossiers de fichiers vidéo, suivi des visionnages et des prêts, export HTML et CSV.

Nom de code provisoire. L'inventaire fonctionnel et l'architecture sont décrits dans le document de conception du projet.

## Prérequis

- SDK .NET 10 (version fixée dans `global.json`).

## Commandes

```bash
dotnet build
```

```bash
dotnet test --project tests/Cinedex.Tests
```

```bash
dotnet run --project src/Cinedex.App
```

## Données

- Par défaut : `%APPDATA%\Cinedex` (Windows), `~/Library/Application Support/Cinedex` (macOS), `$XDG_DATA_HOME/cinedex` (Linux).
- Mode portable : créer un fichier vide `portable.flag` à côté de l'exécutable ; les données vont alors dans `./data`.
- Chaque catalogue est une base SQLite (`catalogs/<nom>/cinedex.db`) avec ses images. Une sauvegarde est faite automatiquement avant chaque migration de schéma.

## Sources de données

Cinedex utilise l'API TMDb. This product uses the TMDB API but is not endorsed or certified by TMDB.
IMDb n'est utilisé que pour les identifiants, les liens et, en option, ses jeux de données officiels non commerciaux ; aucun site n'est scrapé.

## Structure

| Dossier | Rôle |
| --- | --- |
| `src/Cinedex.Core` | Modèle métier, chemins, règles sans dépendance |
| `src/Cinedex.Data` | SQLite : schéma, migrations, dépôts |
| `src/Cinedex.App` | Interface Avalonia (MVVM) |
| `tests/Cinedex.Tests` | Tests xUnit |
