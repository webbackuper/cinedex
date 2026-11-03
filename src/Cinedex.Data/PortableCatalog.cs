using System.IO.Compression;

namespace Cinedex.Data;

/// <summary>
/// Catalogue en consultation sur un autre appareil (téléphone Android) : copie importée d'une sauvegarde .zip faite
/// par Cinedex (base dans « database/cinedex.db », images dans « images/ »). L'import remplace la copie précédente.
/// </summary>
public sealed class PortableCatalog(string root)
{
    private const string DatabaseEntry = "database/cinedex.db";
    private const string ImagesPrefix = "images/";

    public string Root { get; } = Path.GetFullPath(root);

    public string CatalogDirectory => Path.Combine(Root, "catalog");

    public string DatabasePath => Path.Combine(CatalogDirectory, Core.AppPaths.DatabaseFileName);

    public string ImagesDirectory => Path.Combine(CatalogDirectory, "images");

    public bool Exists => File.Exists(DatabasePath);

    /// <summary>Importe une sauvegarde ; renvoie le nombre d'images copiées. La copie précédente n'est remplacée qu'en cas de succès.</summary>
    public int Import(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        Directory.CreateDirectory(Root);
        var staging = Path.Combine(Root, "import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var images = Extract(archive, staging);
            // Les connexions SQLite gardées en réserve tiennent le fichier ouvert : elles sont fermées avant le remplacement.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var previous = Path.Combine(Root, "previous-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(CatalogDirectory))
            {
                Directory.Move(CatalogDirectory, previous);
            }

            Directory.Move(staging, CatalogDirectory);
            if (Directory.Exists(previous))
            {
                Directory.Delete(previous, recursive: true);
            }

            return images;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>Ouvre la copie importée (mise au schéma courant si elle vient d'une version plus ancienne).</summary>
    public CatalogDatabase Open()
    {
        if (!Exists)
        {
            throw new FileNotFoundException(Core.Localization.Tr.T("Aucun catalogue importé."), DatabasePath);
        }

        var database = new CatalogDatabase(DatabasePath);
        database.Initialize(Path.Combine(Root, "backups"));
        return database;
    }

    private static int Extract(Stream archive, string target)
    {
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        var database = zip.GetEntry(DatabaseEntry)
            ?? throw new InvalidDataException(Core.Localization.Tr.T("Cette archive n'est pas une sauvegarde Cinedex contenant la base du catalogue."));
        Directory.CreateDirectory(target);
        database.ExtractToFile(Path.Combine(target, Core.AppPaths.DatabaseFileName));

        var imagesRoot = Path.GetFullPath(Path.Combine(target, "images")) + Path.DirectorySeparatorChar;
        var images = 0;
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(ImagesPrefix, StringComparison.Ordinal) && e.Name.Length > 0))
        {
            // Jamais d'écriture hors du dossier des images, quel que soit le chemin inscrit dans l'archive.
            var destination = Path.GetFullPath(Path.Combine(imagesRoot, entry.FullName[ImagesPrefix.Length..]));
            if (!destination.StartsWith(imagesRoot, StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination);
            images++;
        }

        return images;
    }
}
