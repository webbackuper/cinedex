using Bobine.App.Services;

namespace Bobine.Tests;

public sealed class AppearanceTests
{
    [Fact]
    public void Theme_choice_is_remembered_and_defaults_to_system()
    {
        using var temp = new TempDirectory();
        Assert.Equal(AppTheme.System, Appearance.Load(temp.Path));

        Appearance.Save(temp.Path, AppTheme.Dark);
        Assert.Equal(AppTheme.Dark, Appearance.Load(temp.Path));

        File.WriteAllText(Path.Combine(temp.Path, "appearance.json"), "{ corrompu");
        Assert.Equal(AppTheme.System, Appearance.Load(temp.Path));
    }
}
