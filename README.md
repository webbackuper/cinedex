# Bobine

Catalogue de films et de séries, multiplateforme (Windows, macOS, Linux). Bobine reprend les fonctionnalités d'un gestionnaire de collection vidéo de bureau : fiches enrichies depuis TMDb, scan des dossiers de fichiers vidéo, suivi des visionnages et des prêts, export HTML et CSV.

Nom de code provisoire. L'inventaire fonctionnel et l'architecture sont décrits dans le document de conception du projet.

## Prérequis

- SDK .NET 10 (version fixée dans `global.json`).

## Commandes

```bash
dotnet build
```

```bash
dotnet test --project tests/Bobine.Tests
```

```bash
dotnet run --project src/Bobine.App
```

## Données

- Par défaut : `%APPDATA%\Bobine` (Windows), `~/Library/Application Support/Bobine` (macOS), `$XDG_DATA_HOME/bobine` (Linux).
- Mode portable : créer un fichier vide `portable.flag` à côté de l'exécutable ; les données vont alors dans `./data`.
- Chaque catalogue est une base SQLite (`catalogs/<nom>/bobine.db`) avec ses images. Une sauvegarde est faite automatiquement avant chaque migration de schéma.

## Sources de données

Bobine utilise l'API TMDb. This product uses the TMDB API but is not endorsed or certified by TMDB.
IMDb n'est utilisé que pour les identifiants, les liens et, en option, ses jeux de données officiels non commerciaux ; aucun site n'est scrapé.

## Structure

| Dossier | Rôle |
| --- | --- |
| `src/Bobine.Core` | Modèle métier, chemins, règles sans dépendance |
| `src/Bobine.Data` | SQLite : schéma, migrations, dépôts |
| `src/Bobine.App` | Interface Avalonia (MVVM) |
| `tests/Bobine.Tests` | Tests xUnit |
