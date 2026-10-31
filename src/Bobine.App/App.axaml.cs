using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bobine.App.ViewModels;
using Bobine.App.Views;
using Bobine.Core;
using Bobine.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Bobine.App;

public sealed class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = ConfigureServices(AppPaths.Resolve());
            var main = _services.GetRequiredService<MainWindowViewModel>();
            main.Load();
            desktop.MainWindow = new MainWindow { DataContext = main };
            desktop.ShutdownRequested += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static ServiceProvider ConfigureServices(AppPaths paths)
    {
        paths.EnsureCreated();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "bobine-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(serilog, dispose: true));
        services.AddSingleton(paths);
        services.AddSingleton(sp =>
        {
            var database = new CatalogDatabase(
                paths.CatalogDatabasePath(AppPaths.DefaultCatalogName),
                sp.GetRequiredService<ILogger<CatalogDatabase>>());
            database.Initialize(paths.BackupsDirectory);
            return database;
        });
        services.AddSingleton<TitleRepository>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
