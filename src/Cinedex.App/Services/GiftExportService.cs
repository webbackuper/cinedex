using System.Runtime.InteropServices;
using System.Text;
using Cinedex.Core;
using Cinedex.Core.Localization;
using Cinedex.Data;
using Microsoft.Data.Sqlite;

namespace Cinedex.App.Services;

/// <summary>Un titre dont un fichier n'est pas sur le disque du coffret.</summary>
public sealed record GiftOffDiskTitle(long TitleId, string Title, string Path);

/// <summary>
/// Ce que l'export vérifie avant d'écrire quoi que ce soit : les titres hors du disque, la place nécessaire
/// (arrondie à la taille d'unité d'allocation du volume) et la place libre, un coffret déjà présent.
/// </summary>
public sealed record GiftPlan(
    string TargetRoot,
    string Name,
    int TitleCount,
    IReadOnlyList<GiftOffDiskTitle> OffDisk,
    long RequiredBytes,
    long FreeBytes,
    long ClusterBytes,
    bool Replaces,
    bool HasPlayer,
    string? Problem)
{
    /// <summary>La place libre (plus celle du coffret remplacé) suffit.</summary>
    public bool HasRoom => RequiredBytes <= FreeBytes;

    /// <summary>Un dossier « Cinedex » qui n'est pas un coffret, un exécutable introuvable… : l'export est impossible.</summary>
    public bool CanExport => Problem is null && HasRoom;
}

/// <summary>Bilan d'un export.</summary>
public sealed record GiftResult(string TargetRoot, int Titles, int Files, int Images, long Bytes);

/// <summary>
/// Exporte le catalogue ouvert vers un « coffret » : un disque (USB, en général) qui porte déjà les films, et
/// sur lequel on ajoute <c>Cinedex.exe</c> en mode coffret, une copie nettoyée du catalogue aux chemins
/// relatifs, VLC, <c>Catalogue.html</c> et un <c>LISEZ-MOI.txt</c>. Les films ne sont jamais copiés ni
/// touchés : ils sont déjà sur le disque.
/// <para>
/// La base d'origine n'est pas modifiée, son schéma non plus : les chemins relatifs n'existent que dans la
/// copie. Les données personnelles (prêts, visionnages, notes, favoris, commentaires, prix, réglages, dossiers
/// surveillés) sont retirées de la copie.
/// </para>
/// </summary>
public sealed partial class GiftExportService
{
    public const string PageFileName = "Catalogue.html";
    public const string ReadMeFileName = "LISEZ-MOI.txt";
    public const string ExecutableFileName = "Cinedex.exe";
    public const string PlayerFolderName = "VLC";

    /// <summary>VLC est lancé en plein écran depuis un coffret.</summary>
    public const string PlayerArguments = "--fullscreen --play-and-exit %FILEPATH%";

    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;
    private readonly CatalogDatabase _source;
    private readonly string _imagesDirectory;
    private readonly PeopleRepository? _people;

    public GiftExportService(TitleRepository titles, MediaFileRepository files, CatalogDatabase source, string imagesDirectory, PeopleRepository? people = null)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagesDirectory);
        _titles = titles;
        _files = files;
        _source = source;
        _imagesDirectory = imagesDirectory;
        _people = people;
    }

    /// <summary>L'exécutable autonome à copier ; celui qui tourne, par défaut.</summary>
    public string? ExecutablePath { get; init; } = Environment.ProcessPath;

    /// <summary>Le VLC livré avec cette version (récupéré au build, comme MediaInfo).</summary>
    public string PlayerDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "tools", PlayerFolderName);

    /// <summary>Place libre et taille d'unité d'allocation d'un volume ; remplaçable pour les tests.</summary>
    public Func<string, (long Free, long Cluster)> DiskInfo { get; init; } = ReadDiskInfo;

    /// <summary>Dossier des données du coffret, à côté de l'exécutable.</summary>
    public static string DataRoot(string targetRoot) => Path.Combine(targetRoot, AppPaths.GiftFolderName);

    /// <summary>Vérifie tout avant d'écrire : rien n'est modifié ici.</summary>
    public GiftPlan Plan(string targetRoot, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        var root = WithSeparator(Path.GetFullPath(targetRoot));
        var titleNames = _titles.Query(new Core.Titles.TitleQuery()).ToDictionary(t => t.Id, t => t.Title);
        var offDisk = _files.AllFiles()
            .Where(f => titleNames.ContainsKey(f.TitleId) && !IsUnder(f.FullPath, root))
            .GroupBy(f => f.TitleId)
            .Select(g => new GiftOffDiskTitle(g.Key, titleNames[g.Key], g.First().FullPath))
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var (free, cluster) = DiskInfo(root);
        cluster = Math.Max(cluster, 512);
        var dataRoot = DataRoot(root);
        var replaces = File.Exists(Path.Combine(dataRoot, AppPaths.GiftMarker));
        string? problem = null;
        if (Directory.Exists(dataRoot) && !replaces)
        {
            problem = Tr.F("Le dossier « {0} » existe déjà sur ce disque et n'a pas été créé par un export de coffret : il n'est pas touché. Renommez-le ou choisissez un autre disque.", dataRoot);
        }
        else if (ExecutablePath is not { } exe || !File.Exists(exe) || !IsSelfContained(exe))
        {
            problem = Tr.T("L'export doit être lancé depuis le Cinedex publié (un seul fichier Cinedex.exe) : c'est lui qui est copié sur le disque.");
        }

        var hasPlayer = File.Exists(Path.Combine(PlayerDirectory, "vlc.exe"));
        var required = 0L;
        long Rounded(long size) => (size + cluster - 1) / cluster * cluster;
        if (ExecutablePath is { } path && File.Exists(path))
        {
            required += Rounded(new FileInfo(path).Length);
        }

        required += SizeOf(hasPlayer ? PlayerDirectory : null, cluster);
        required += SizeOf(_imagesDirectory, cluster);
        required += Rounded(new FileInfo(_source.DatabasePath).Length);
        required += Rounded(64 * 1024 + (titleNames.Count * 4L * 1024)) + Rounded(8 * 1024) + (8 * cluster);

        // Le coffret remplacé libère sa place ; l'exécutable de la racine aussi.
        var reclaimed = replaces ? SizeOf(dataRoot, cluster) + RoundedFile(Path.Combine(root, ExecutableFileName), cluster) + RoundedFile(Path.Combine(root, PageFileName), cluster) : 0;
        return new GiftPlan(root, name, titleNames.Count - offDisk.Count, offDisk, required, free + reclaimed, cluster, replaces, hasPlayer, problem);
    }

    /// <summary>
    /// Écrit le coffret. <paramref name="excluded"/> : titres retirés de la copie (ceux hors du disque). Un
    /// coffret précédent est remplacé ; les films ne sont pas touchés.
    /// </summary>
    public GiftResult Export(GiftPlan plan, IReadOnlyCollection<long> excluded, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(excluded);
        if (!plan.CanExport)
        {
            throw new InvalidOperationException(plan.Problem ?? Tr.T("Pas assez de place sur le disque."));
        }

        var root = plan.TargetRoot;
        var remaining = plan.OffDisk.Select(t => t.TitleId).Except(excluded).ToList();
        if (remaining.Count > 0)
        {
            throw new InvalidOperationException(Tr.T("Des titres ont des fichiers hors du disque : excluez-les de l'export."));
        }

        var dataRoot = DataRoot(root);
        if (plan.Replaces && Directory.Exists(dataRoot))
        {
            progress?.Report(Tr.T("Retrait du coffret précédent…"));
            Directory.Delete(dataRoot, recursive: true);
        }

        var paths = new AppPaths(dataRoot, isPortable: true, giftName: plan.Name);
        paths.EnsureCreated();
        var databasePath = paths.CatalogDatabasePath(AppPaths.DefaultCatalogName);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        // 1. La base : copie compacte, puis nettoyage. L'original n'est que lu.
        progress?.Report(Tr.T("Copie du catalogue…"));
        using (var connection = _source.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "VACUUM INTO $path;";
            command.Parameters.AddWithValue("$path", databasePath);
            command.ExecuteNonQuery();
        }

        var copy = new CatalogDatabase(databasePath);
        copy.Initialize(paths.BackupsDirectory);
        if (excluded.Count > 0)
        {
            new TitleRepository(copy).Delete([.. excluded]);
        }

        var relative = _files.AllFiles()
            .Where(f => !excluded.Contains(f.TitleId) && IsUnder(f.FullPath, root))
            .ToDictionary(f => f.Id, f => Path.GetRelativePath(root, f.FullPath));
        Sanitize(copy, relative);

        // 2. Les images, telles quelles : leurs chemins dans la base sont relatifs à leur dossier.
        progress?.Report(Tr.T("Copie des affiches et des photos…"));
        var images = CopyDirectory(_imagesDirectory, paths.CatalogImagesDirectory(AppPaths.DefaultCatalogName));

        // 3. Le lecteur et l'exécutable.
        if (plan.HasPlayer)
        {
            progress?.Report(Tr.T("Copie de VLC…"));
            CopyDirectory(PlayerDirectory, Path.Combine(dataRoot, PlayerFolderName));
        }

        progress?.Report(Tr.T("Copie de Cinedex.exe…"));
        File.Copy(ExecutablePath!, Path.Combine(root, ExecutableFileName), overwrite: true);

        // 4. La page web et le mode d'emploi, puis le marqueur, en dernier : sans lui, le disque n'est pas un coffret.
        progress?.Report(Tr.T("Écriture de la page du catalogue…"));
        var page = GiftCatalogPage.Build(
            plan.Name,
            new TitleRepository(copy),
            _people is null ? null : new PeopleRepository(copy),
            relative,
            new MediaFileRepository(copy),
            new ImageStore(copy, paths.CatalogImagesDirectory(AppPaths.DefaultCatalogName)),
            root);
        File.WriteAllText(Path.Combine(root, PageFileName), page, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, ReadMeFileName), ReadMe(plan.Name, plan.HasPlayer), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(dataRoot, AppPaths.GiftMarker), plan.Name, new UTF8Encoding(false));
        SqliteConnection.ClearAllPools();

        var titles = new TitleRepository(copy).Query(new Core.Titles.TitleQuery()).Count;
        return new GiftResult(root, titles, relative.Count, images, SizeOf(dataRoot, 1));
    }

    /// <summary>
    /// Retire de la copie ce qui est personnel, et rend relatifs à la racine du disque les chemins des fichiers.
    /// Public pour les tests : c'est la garantie qu'aucune donnée privée ne part sur le disque offert.
    /// </summary>
    public static void Sanitize(CatalogDatabase copy, IReadOnlyDictionary<long, string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(relativePaths);
        using var connection = copy.Open();
        using var transaction = connection.BeginTransaction();
        void Run(string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        Run("DELETE FROM loan;");
        Run("DELETE FROM viewing;");
        Run("DELETE FROM match_review;");
        Run("DELETE FROM saved_filter;");
        Run("UPDATE title SET is_favorite = 0, is_shortlisted = 0, user_rating = NULL, comments = NULL;");
        Run("UPDATE copy SET price = NULL, location = NULL;");

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE media_file SET path = $path, scan_root_id = NULL WHERE id = $id;";
            var path = update.Parameters.Add("$path", SqliteType.Text);
            var id = update.Parameters.Add("$id", SqliteType.Integer);
            foreach (var (fileId, relative) in relativePaths)
            {
                path.Value = relative;
                id.Value = fileId;
                update.ExecuteNonQuery();
            }
        }

        // Un fichier qui n'a pas de chemin relatif n'est pas sur le disque : il ne doit pas pointer vers le PC d'origine.
        Run("DELETE FROM media_file WHERE scan_root_id IS NOT NULL OR path LIKE '_:%' OR path LIKE '\\%' OR path LIKE '/%';");
        Run("DELETE FROM scan_root;");

        // Réglages : rien de l'original (clés de recherche, dossiers, proxy, sauvegardes…), seulement le lecteur du coffret.
        Run("DELETE FROM setting;");
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO setting (key, value) VALUES ($key, $value);";
            var key = insert.Parameters.Add("$key", SqliteType.Text);
            var value = insert.Parameters.Add("$value", SqliteType.Text);
            foreach (var (k, v) in new[]
                     {
                         (MediaContext.PlayerKey, Path.Combine(AppPaths.GiftFolderName, PlayerFolderName, "vlc.exe")),
                         (MediaContext.PlayerArgsKey, PlayerArguments),
                     })
            {
                key.Value = k;
                value.Value = v;
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        using var vacuum = connection.CreateCommand();
        vacuum.CommandText = "VACUUM;";
        vacuum.ExecuteNonQuery();
    }

    private static string ReadMe(string name, bool hasPlayer) => string.Join("\r\n",
    [
        Tr.F("{0} — vos films, avec leurs fiches", name),
        string.Empty,
        Tr.T("1. Double-cliquez sur Cinedex.exe, à la racine du disque."),
        Tr.T("   Windows peut afficher « Windows a protégé votre ordinateur » : cliquez sur « Informations complémentaires », puis sur « Exécuter quand même »."),
        Tr.T("   Rien n'est installé sur l'ordinateur : tout reste sur le disque."),
        hasPlayer
            ? Tr.T("2. Choisissez un film et cliquez sur « Lire » : il s'ouvre en plein écran dans VLC, fourni sur le disque (Échap pour quitter le plein écran).")
            : Tr.T("2. Choisissez un film et cliquez sur « Lire » : il s'ouvre avec le lecteur vidéo de l'ordinateur."),
        Tr.T("3. Vous pouvez marquer un film « vu », le mettre en favori ou le noter : c'est enregistré sur le disque."),
        string.Empty,
        Tr.T("Sur un Mac, ou si Cinedex.exe ne se lance pas : ouvrez Catalogue.html dans un navigateur. Vous y trouverez les fiches, et un lien vers chaque fichier vidéo (VLC le lit mieux qu'un navigateur)."),
        string.Empty,
        Tr.T("Ne déplacez et ne renommez ni le dossier « Cinedex » ni les dossiers des films : Cinedex retrouve les films par leur place sur le disque."),
        string.Empty,
        Tr.T("VLC est un logiciel libre de VideoLAN (licence GNU GPL, voir Cinedex\\VLC\\COPYING.txt) : https://www.videolan.org"),
        string.Empty,
    ]);

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    /// <summary>Un exécutable « fichier unique » : sans Cinedex.dll à côté (qui trahit une version de développement).</summary>
    private static bool IsSelfContained(string exe) =>
        !File.Exists(Path.Combine(Path.GetDirectoryName(exe) ?? ".", "Cinedex.dll"));

    private static long RoundedFile(string path, long cluster) =>
        File.Exists(path) ? (new FileInfo(path).Length + cluster - 1) / cluster * cluster : 0;

    private static long SizeOf(string? directory, long cluster)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return 0;
        }

        var total = 0L;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            total += (new FileInfo(file).Length + cluster - 1) / cluster * cluster;
        }

        // Chaque dossier occupe au moins une unité d'allocation.
        total += (Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).LongCount() + 1) * cluster;
        return total;
    }

    private static int CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        if (!Directory.Exists(source))
        {
            return 0;
        }

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            count++;
        }

        return count;
    }

    private static (long Free, long Cluster) ReadDiskInfo(string root)
    {
        var free = new DriveInfo(Path.GetPathRoot(root) ?? root).AvailableFreeSpace;
        var cluster = 4096L;
        if (OperatingSystem.IsWindows() &&
            NativeMethods.GetDiskFreeSpaceW(Path.GetPathRoot(root) ?? root, out var sectorsPerCluster, out var bytesPerSector, out _, out _))
        {
            cluster = (long)sectorsPerCluster * bytesPerSector;
        }

        return (free, cluster);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetDiskFreeSpaceW(string rootPathName, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
    }
}
