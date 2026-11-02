using Avalonia;

namespace Cinedex.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Installation, mise à jour et désinstallation (Velopack) : traitées ici avant tout affichage.
        Velopack.VelopackApp.Build().Run();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Utilisé aussi par le concepteur visuel et les tests sans affichage.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
