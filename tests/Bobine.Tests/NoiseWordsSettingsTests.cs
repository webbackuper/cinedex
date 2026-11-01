using Bobine.App.Security;
using Bobine.App.Services;
using Bobine.App.ViewModels;
using Bobine.Data;
using Bobine.Media;

namespace Bobine.Tests;

public sealed class NoiseWordsSettingsTests
{
    [Fact]
    public void Noise_words_default_then_user_list_is_saved_and_used()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
        db.Initialize(temp.Path);
        var store = new SettingsStore(db);

        Assert.Same(FileNameParser.DefaultNoiseWords, MediaContext.NoiseWords(store));

        var vm = new SettingsViewModel(new NoVault(), store);
        Assert.Contains("TeleSync", vm.NoiseWordsText, StringComparison.Ordinal);
        vm.NoiseWordsText = "x265\r\nTeleSync\n\nÉdition Ultime\nx265";
        Assert.True(vm.Save());

        var saved = MediaContext.NoiseWords(store);
        Assert.Equal(["x265", "TeleSync", "Édition Ultime"], saved);
        Assert.Equal("Heat", FileNameParser.Parse("Heat Édition Ultime.mkv", saved).Title);
        Assert.Equal("Heat MULTI", FileNameParser.Parse("Heat MULTI.mkv", saved).Title);

        vm.ResetNoiseWords();
        Assert.True(vm.Save());
        Assert.Equal(FileNameParser.DefaultNoiseWords.Count, MediaContext.NoiseWords(store).Count);
    }

    private sealed class NoVault : ISecretStore
    {
        public string? Read(string name) => null;

        public void Write(string name, string secret)
        {
        }

        public void Delete(string name)
        {
        }
    }
}
