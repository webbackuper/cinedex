using System.Text.Json;
using Bobine.Core;
using Microsoft.Data.Sqlite;

namespace Bobine.Data;

public sealed record CatalogEntry(string Name, long SizeBytes, DateTime LastModifiedUtc);

/// <summary>
/// Plusieurs catalogues indépendants (comme les bases multiples d'EMDB) : un dossier par catalogue, avec sa base
/// et ses images. Le catalogue courant est mémorisé dans <c>state.json</c>. Supprimer un catalogue le déplace
/// dans les sauvegardes au lieu de l'effacer.
/// </summary>
public sealed class CatalogManager(AppPaths paths)
{
    private string StateFile => Path.Combine(paths.DataRoot, "state.json");

    public IReadOnlyList<CatalogEntry> List()
    {
        if (!Directory.Exists(paths.CatalogsDirectory))
        {
            return [];
        }

        return new DirectoryInfo(paths.CatalogsDirectory).EnumerateDirectories()
            .Select(d => (Dir: d, Db: new FileInfo(Path.Combine(d.FullName, "bobine.db"))))
            .Where(x => x.Db.Exists)
            .Select(x => new CatalogEntry(x.Dir.Name, x.Db.Length, x.Db.LastWriteTimeUtc))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Catalogue à ouvrir : celui mémorisé s'il existe encore, sinon le premier, sinon le catalogue par défaut.</summary>
    public string Current
    {
        get
        {
            var catalogs = List();
            var remembered = ReadState();
            if (remembered is not null && catalogs.Any(c => c.Name == remembered))
            {
                return remembered;
            }

            return catalogs.FirstOrDefault()?.Name ?? AppPaths.DefaultCatalogName;
        }
    }

    public void SetCurrent(string name)
    {
        var safe = AppPaths.SanitizeName(name);
        if (!Exists(safe))
        {
            throw new DirectoryNotFoundException($"Catalogue introuvable : {safe}");
        }

        Directory.CreateDirectory(paths.DataRoot);
        File.WriteAllText(StateFile, JsonSerializer.Serialize(new State(safe)));
    }

    public string DatabasePath(string name) => paths.CatalogDatabasePath(AppPaths.SanitizeName(name));

    public bool Exists(string name) => File.Exists(paths.CatalogDatabasePath(AppPaths.SanitizeName(name)));

    /// <summary>Crée un catalogue vide (schéma à jour) ; renvoie son nom normalisé.</summary>
    public string Create(string name)
    {
        var safe = AppPaths.SanitizeName(name);
        if (Exists(safe))
        {
            throw new InvalidOperationException($"Le catalogue « {safe} » existe déjà.");
        }

        new CatalogDatabase(paths.CatalogDatabasePath(safe)).Initialize(paths.BackupsDirectory);
        SqliteConnection.ClearAllPools();
        return safe;
    }

    /// <summary>Renomme un catalogue fermé (le catalogue ouvert doit d'abord être quitté).</summary>
    public string Rename(string oldName, string newName, string? openCatalog)
    {
        var from = AppPaths.SanitizeName(oldName);
        var to = AppPaths.SanitizeName(newName);
        if (from == openCatalog)
        {
            throw new InvalidOperationException("Ouvrez un autre catalogue avant de renommer celui-ci.");
        }

        if (!Exists(from))
        {
            throw new DirectoryNotFoundException($"Catalogue introuvable : {from}");
        }

        if (Directory.Exists(paths.CatalogDirectory(to)))
        {
            throw new InvalidOperationException($"Le catalogue « {to} » existe déjà.");
        }

        SqliteConnection.ClearAllPools();
        Directory.Move(paths.CatalogDirectory(from), paths.CatalogDirectory(to));
        if (ReadState() == from)
        {
            SetCurrent(to);
        }

        return to;
    }

    /// <summary>Retire un catalogue fermé en le déplaçant dans les sauvegardes ; renvoie l'emplacement de la copie.</summary>
    public string Remove(string name, string? openCatalog)
    {
        var safe = AppPaths.SanitizeName(name);
        if (safe == openCatalog)
        {
            throw new InvalidOperationException("Impossible de supprimer le catalogue ouvert.");
        }

        if (!Exists(safe))
        {
            throw new DirectoryNotFoundException($"Catalogue introuvable : {safe}");
        }

        SqliteConnection.ClearAllPools();
        var target = Path.Combine(paths.BackupsDirectory, "catalogues-supprimes", $"{safe}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(paths.CatalogDirectory(safe), target);
        return target;
    }

    private string? ReadState()
    {
        try
        {
            return File.Exists(StateFile) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile))?.Current : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record State(string Current);
}
