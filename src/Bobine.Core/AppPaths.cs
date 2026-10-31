namespace Bobine.Core;

/// <summary>
/// Emplacements des données de l'application.
/// Mode portable : un fichier <c>portable.flag</c> à côté de l'exécutable place les données dans <c>./data</c>.
/// Sinon, les données vont dans le profil utilisateur (AppData sous Windows, Application Support sous macOS, XDG sous Linux).
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarker = "portable.flag";
    public const string DefaultCatalogName = "catalogue";

    public AppPaths(string dataRoot, bool isPortable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        DataRoot = Path.GetFullPath(dataRoot);
        IsPortable = isPortable;
    }

    public string DataRoot { get; }

    public bool IsPortable { get; }

    public string CatalogsDirectory => Path.Combine(DataRoot, "catalogs");

    public string BackupsDirectory => Path.Combine(DataRoot, "backups");

    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    public string CacheDirectory => Path.Combine(DataRoot, "cache");

    public static AppPaths Resolve(string? baseDirectory = null, string? userDataOverride = null)
    {
        var appDir = baseDirectory ?? AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(appDir, PortableMarker)))
        {
            return new AppPaths(Path.Combine(appDir, "data"), isPortable: true);
        }

        return new AppPaths(userDataOverride ?? DefaultUserDataRoot(), isPortable: false);
    }

    /// <summary>Dossier d'un catalogue : la base SQLite et ses images.</summary>
    public string CatalogDirectory(string catalogName)
    {
        var safe = SanitizeName(catalogName);
        return Path.Combine(CatalogsDirectory, safe);
    }

    public string CatalogDatabasePath(string catalogName) => Path.Combine(CatalogDirectory(catalogName), "bobine.db");

    public string CatalogImagesDirectory(string catalogName) => Path.Combine(CatalogDirectory(catalogName), "images");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(CatalogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }

    /// <summary>
    /// Restreint un nom de catalogue à un nom de dossier sûr : pas de séparateur, pas de « .. », pas de caractère réservé.
    /// </summary>
    public static string SanitizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
        if (cleaned.Length == 0)
        {
            throw new ArgumentException("Nom de catalogue invalide.", nameof(name));
        }

        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    private static string DefaultUserDataRoot()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var root = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                : xdg;
            return Path.Combine(root, "bobine");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Bobine");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bobine");
    }
}
