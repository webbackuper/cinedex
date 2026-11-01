using Cinedex.App.Security;
using Cinedex.App.ViewModels;
using Cinedex.Data;

namespace Cinedex.Tests;

public sealed class SecretStoreTests
{
    [Fact]
    public void Platform_store_round_trips_a_secret()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Coffre Windows uniquement en local ; les autres coffres exigent une session graphique.");
        var store = SecretStore.ForCurrentPlatform();
        var name = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.Read(name));
            store.Write(name, "valeur-é-123");
            Assert.Equal("valeur-é-123", store.Read(name));
        }
        finally
        {
            store.Delete(name);
        }

        Assert.Null(store.Read(name));
    }

    [Fact]
    public void Settings_store_the_token_in_the_vault_never_in_the_catalog()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var settingsStore = new SettingsStore(db);
        var vault = new MemoryVault();
        var vm = new SettingsViewModel(vault, settingsStore) { NewToken = "eyJ" + new string('a', 60) };

        Assert.True(vm.Save());

        Assert.True(vm.HasToken);
        Assert.Equal(string.Empty, vm.NewToken);
        Assert.StartsWith("eyJ", vault.Read(SecretStore.TmdbToken), StringComparison.Ordinal);
        foreach (var file in new[] { db.DatabasePath, db.DatabasePath + "-wal" }.Where(File.Exists))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
            Assert.DoesNotContain("eyJaaaa", reader.ReadToEnd(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("court", "fr-FR", "FR")]
    [InlineData("", "francais", "FR")]
    [InlineData("", "fr-FR", "france")]
    public void Settings_reject_invalid_input(string token, string language, string country)
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var vm = new SettingsViewModel(new MemoryVault(), new SettingsStore(db)) { NewToken = token, Language = language, Country = country };

        Assert.False(vm.Save());
        Assert.NotNull(vm.Error);
    }

    private sealed class MemoryVault : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string name) => _values.GetValueOrDefault(name);

        public void Write(string name, string secret) => _values[name] = secret;

        public void Delete(string name) => _values.Remove(name);
    }
}
