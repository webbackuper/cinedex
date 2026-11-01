using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bobine.App.Security;
using Bobine.App.Services;
using Bobine.App.ViewModels;
using Bobine.App.Views;
using Bobine.Core;
using Bobine.Data;
using Bobine.Metadata.Tmdb;
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
            var window = new MainWindow();
            var services = _services;
            var main = new MainWindowViewModel(
                services.GetRequiredService<TitleRepository>(),
                services.GetRequiredService<AppPaths>(),
                window,
                services.GetRequiredService<MetadataService>(),
                services.GetRequiredService<ImageStore>(),
                () => new SettingsViewModel(services.GetRequiredService<ISecretStore>(), services.GetRequiredService<SettingsStore>()),
                services.GetRequiredService<MediaContext>());
            main.Load();
            window.DataContext = main;
            desktop.MainWindow = window;
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
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            serilog.Fatal(e.ExceptionObject as Exception, "Erreur non gérée");
            serilog.Dispose();
        };
        TaskScheduler.UnobservedTaskException += (_, e) => serilog.Error(e.Exception, "Erreur non observée dans une tâche");

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
        services.AddSingleton<MetadataRepository>();
        services.AddSingleton(sp => new ImageStore(
            sp.GetRequiredService<CatalogDatabase>(),
            paths.CatalogImagesDirectory(AppPaths.DefaultCatalogName)));
        services.AddSingleton(SecretStore.ForCurrentPlatform());
        services.AddSingleton(_ => TmdbClient.CreateHttpClient());
        services.AddSingleton(sp =>
        {
            var secrets = sp.GetRequiredService<ISecretStore>();
            var settings = sp.GetRequiredService<SettingsStore>();
            // Le jeton est relu à chaque appel : un changement dans les réglages s'applique sans redémarrer.
            return new TmdbClient(
                sp.GetRequiredService<HttpClient>(),
                _ => ValueTask.FromResult(secrets.Read(SecretStore.TmdbToken)),
                new TmdbOptions
                {
                    Language = settings.Get(SettingsViewModel.LanguageKey) ?? "fr-FR",
                    CertificationCountry = settings.Get(SettingsViewModel.CountryKey) ?? "FR",
                },
                sp.GetRequiredService<ILogger<TmdbClient>>());
        });
        services.AddSingleton<MetadataService>();
        services.AddSingleton<MediaFileRepository>();
        services.AddSingleton<SeriesRepository>();
        services.AddSingleton(sp => new LibraryImportService(
            sp.GetRequiredService<TitleRepository>(),
            sp.GetRequiredService<MediaFileRepository>(),
            sp.GetRequiredService<ImageStore>(),
            sp.GetRequiredService<MetadataService>(),
            // Relu à chaque import : un chemin MediaInfo ou ffprobe changé dans les réglages s'applique sans redémarrer.
            () => new Bobine.Media.MediaProbe(
                sp.GetRequiredService<SettingsStore>().Get(MediaContext.FfprobeKey),
                sp.GetRequiredService<SettingsStore>().Get(MediaContext.MediaInfoKey)),
            sp.GetRequiredService<ILogger<LibraryImportService>>(),
            sp.GetRequiredService<SeriesRepository>()));
        services.AddSingleton(sp => new MediaContext(
            sp.GetRequiredService<MediaFileRepository>(),
            sp.GetRequiredService<LibraryImportService>(),
            sp.GetRequiredService<SettingsStore>()));
        return services.BuildServiceProvider();
    }
}
