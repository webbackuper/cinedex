using Cinedex.App.Services;

namespace Cinedex.Tests;

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

    [Fact]
    public void Window_opens_maximized_by_default_and_remembers_the_chosen_mode()
    {
        using var temp = new TempDirectory();
        Assert.Equal(StartupWindowMode.Maximized, WindowPlacement.Load(temp.Path).Mode);

        new WindowPlacement { Mode = StartupWindowMode.LastSize, X = 10, Y = 20, Width = 1200, Height = 700, WasMaximized = false }.Save(temp.Path);
        var loaded = WindowPlacement.Load(temp.Path);
        Assert.Equal(StartupWindowMode.LastSize, loaded.Mode);
        Assert.Equal(1200, loaded.Width);
        Assert.False(loaded.WasMaximized);
    }
}
