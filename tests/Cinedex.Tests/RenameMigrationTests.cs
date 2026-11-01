using Cinedex.App.Security;
using Cinedex.Core;

namespace Cinedex.Tests;

/// <summary>Reprise des données de l'application quand elle s'appelait encore « Bobine ».</summary>
public sealed class RenameMigrationTests
{
    [Fact]
    public void Old_catalog_databases_are_renamed_with_their_journal()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path, isPortable: true);
        var catalog = Directory.CreateDirectory(Path.Combine(paths.CatalogsDirectory, "films")).FullName;
        File.WriteAllText(Path.Combine(catalog, "bobine.db"), "db");
        File.WriteAllText(Path.Combine(catalog, "bobine.db-wal"), "wal");

        paths.EnsureCreated();

        Assert.Equal("db", File.ReadAllText(paths.CatalogDatabasePath("films")));
        Assert.Equal("wal", File.ReadAllText(paths.CatalogDatabasePath("films") + "-wal"));
        Assert.False(File.Exists(Path.Combine(catalog, "bobine.db")));
    }

    [Fact]
    public void Secrets_saved_under_the_old_name_move_to_the_new_one()
    {
        var current = new MemoryVault();
        var legacy = new MemoryVault();
        legacy.Write(SecretStore.TmdbToken, "jeton");
        var store = new MigratingSecretStore(current, legacy);

        Assert.Equal("jeton", store.Read(SecretStore.TmdbToken));
        Assert.Equal("jeton", current.Read(SecretStore.TmdbToken));
        Assert.Null(legacy.Read(SecretStore.TmdbToken));
        Assert.Null(store.Read(SecretStore.OmdbKey));
    }

    private sealed class MemoryVault : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string name) => _values.GetValueOrDefault(name);

        public void Write(string name, string secret) => _values[name] = secret;

        public void Delete(string name) => _values.Remove(name);
    }
}
