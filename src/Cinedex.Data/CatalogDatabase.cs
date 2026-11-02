using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Cinedex.Core.Localization;

namespace Cinedex.Data;

/// <summary>
/// Accès à la base SQLite d'un catalogue : ouverture avec les pragmas de sûreté, migrations, contrôle d'intégrité.
/// </summary>
public sealed class CatalogDatabase
{
    private readonly string _connectionString;
    private readonly ILogger<CatalogDatabase> _logger;

    public CatalogDatabase(string databasePath, ILogger<CatalogDatabase>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _logger = logger ?? NullLogger<CatalogDatabase>.Instance;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000; PRAGMA trusted_schema = OFF;");
        return connection;
    }

    /// <summary>
    /// Crée la base si besoin, vérifie son intégrité et applique les migrations en attente.
    /// Une copie de sauvegarde est faite avant toute migration d'une base existante.
    /// </summary>
    public MigrationResult Initialize(string backupDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var existed = File.Exists(DatabasePath);

        using var connection = Open();
        if (existed)
        {
            var integrity = connection.ExecuteScalar<string>("PRAGMA quick_check;");
            if (!string.Equals(integrity, "ok", StringComparison.Ordinal))
            {
                throw new CatalogCorruptedException(DatabasePath, integrity ?? "résultat vide");
            }
        }

        var migrator = new Migrator(_logger);
        var pending = Migrator.PendingMigrations(connection);
        string? backupPath = null;
        if (existed && pending.Count > 0)
        {
            backupPath = BackupTo(connection, backupDirectory, $"avant-migration-v{pending[0].Version}");
        }

        var applied = migrator.Apply(connection, pending);
        return new MigrationResult(Migrator.CurrentVersion(connection), applied, backupPath);
    }

    /// <summary>Copie cohérente de la base ouverte (API de sauvegarde en ligne de SQLite).</summary>
    public string BackupTo(SqliteConnection source, string backupDirectory, string label)
    {
        ArgumentNullException.ThrowIfNull(source);
        Directory.CreateDirectory(backupDirectory);
        var fileName = $"{Path.GetFileNameWithoutExtension(DatabasePath)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{label}.db";
        var target = Path.Combine(backupDirectory, fileName);
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        _logger.LogInformation("Sauvegarde créée : {BackupPath}", target);
        return target;
    }
}

public sealed record MigrationResult(int SchemaVersion, IReadOnlyList<int> AppliedVersions, string? BackupPath);

public sealed class CatalogCorruptedException(string path, string detail)
    : Exception(Tr.F("La base « {0} » est endommagée : {1}", path, detail))
{
    public string DatabasePath { get; } = path;
}
