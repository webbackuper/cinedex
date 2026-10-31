using System.Globalization;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Bobine.Data;

/// <summary>
/// Applique les scripts <c>Migrations/NNNN_nom.sql</c> embarqués, dans l'ordre, chacun dans sa transaction.
/// La version courante est tenue dans <c>PRAGMA user_version</c>.
/// </summary>
internal sealed class Migrator(ILogger logger)
{
    private const string ResourcePrefix = "Bobine.Data.Migrations.";

    internal sealed record Migration(int Version, string Name, string Sql);

    public static IReadOnlyList<Migration> All { get; } = LoadMigrations();

    public static int CurrentVersion(SqliteConnection connection) => connection.ExecuteScalar<int>("PRAGMA user_version;");

    public static IReadOnlyList<Migration> PendingMigrations(SqliteConnection connection)
    {
        var current = CurrentVersion(connection);
        var latest = All.Count == 0 ? 0 : All[^1].Version;
        if (current > latest)
        {
            throw new InvalidOperationException(
                $"La base est en version de schéma {current}, plus récente que cette application ({latest}). Mettez Bobine à jour.");
        }

        return All.Where(m => m.Version > current).ToList();
    }

    public IReadOnlyList<int> Apply(SqliteConnection connection, IReadOnlyList<Migration> migrations)
    {
        var applied = new List<int>();
        foreach (var migration in migrations)
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(migration.Sql, transaction: transaction);
            // PRAGMA n'accepte pas de paramètre ; la valeur vient d'un entier parsé, pas d'une saisie.
            connection.Execute($"PRAGMA user_version = {migration.Version.ToString(CultureInfo.InvariantCulture)};", transaction: transaction);
            transaction.Commit();
            logger.LogInformation("Migration {Version} ({Name}) appliquée", migration.Version, migration.Name);
            applied.Add(migration.Version);
        }

        return applied;
    }

    private static List<Migration> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = new List<Migration>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var fileName = resource[ResourcePrefix.Length..];
            var separator = fileName.IndexOf('_', StringComparison.Ordinal);
            if (separator <= 0 || !int.TryParse(fileName[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            {
                throw new InvalidOperationException($"Nom de migration invalide : {fileName}");
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            migrations.Add(new Migration(version, Path.GetFileNameWithoutExtension(fileName[(separator + 1)..]), reader.ReadToEnd()));
        }

        migrations.Sort((a, b) => a.Version.CompareTo(b.Version));
        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
            {
                throw new InvalidOperationException($"Les migrations doivent être numérotées sans trou à partir de 1 (trouvé {migrations[i].Version}).");
            }
        }

        return migrations;
    }
}
