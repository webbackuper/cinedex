using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cinedex.App.Security;
using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.App.Views;
using Cinedex.Core;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Cinedex.Core.Localization;

namespace Cinedex.App;

public sealed class App : Application
{
    private ServiceProvider? _services;
    private Serilog.Core.Logger? _serilog;
    private AppPaths? _paths;
    private MainWindow? _window;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _paths = AppPaths.Resolve();
            _paths.EnsureCreated();
            _serilog = CreateLogger(_paths);
            Services.Appearance.Apply(Services.Appearance.Load(_paths.DataRoot));
            Cinedex.Core.Localization.Tr.Use(LanguagePreference.Load(_paths.DataRoot));
            UiTranslator.Install(this);
            _window = new MainWindow();
            _window.ApplyStartupPlacement(_paths.DataRoot);
            OpenCatalog(new CatalogManager(_paths).Current);
            desktop.MainWindow = _window;
            if (Environment.GetEnvironmentVariable("CINEDEX_SNAPSHOT") is { Length: > 0 } snapshot)
            {
                // Outil de développement : capture de la fenêtre principale (vérification des traductions), puis sortie.
                _window.Opened += async (_, _) =>
                {
                    // CINEDEX_SNAPSHOT_HELP : capture de la fenêtre d'aide à la place.
                    Avalonia.Controls.Window target = _window;
                    if (Environment.GetEnvironmentVariable("CINEDEX_SNAPSHOT_HELP") is { Length: > 0 })
                    {
                        target = new HelpWindow { DataContext = new HelpViewModel(HelpDocument.Load(Cinedex.Core.Localization.Tr.Current.Code)) };
                        target.Show(_window);
                    }

                    await Task.Delay(2500);
                    var size = new Avalonia.PixelSize((int)target.Bounds.Width, (int)target.Bounds.Height);
                    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                    bitmap.Render(target);
                    using (var file = File.Create(snapshot))
                    {
                        bitmap.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
                    desktop.Shutdown();
                };
            }
            desktop.ShutdownRequested += (_, _) =>
            {
                _services?.Dispose();
                _serilog.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Ouvre un catalogue : ferme le précédent, reconstruit les services sur la nouvelle base, recharge la fenêtre.</summary>
    private void OpenCatalog(string name)
    {
        var previous = _services;
        _services = ConfigureServices(_paths!, name, _serilog!, _window);
        previous?.Dispose();
        SqliteConnection.ClearAllPools();

        // Base créée au premier accès : on l'ouvre avant de la mémoriser comme catalogue courant (premier lancement).
        _services.GetRequiredService<CatalogDatabase>();
        new CatalogManager(_paths!).SetCurrent(name);

        var services = _services;
        var catalogs = new CatalogsViewModel(new CatalogManager(_paths!), name, OpenCatalog, services.GetRequiredService<SettingsStore>());
        var main = new MainWindowViewModel(
            services.GetRequiredService<TitleRepository>(),
            services.GetRequiredService<AppPaths>(),
            _window!,
            services.GetRequiredService<MetadataService>(),
            services.GetRequiredService<ImageStore>(),
            () => new SettingsViewModel(services.GetRequiredService<ISecretStore>(), services.GetRequiredService<SettingsStore>(), _paths!.DataRoot),
            services.GetRequiredService<MediaContext>(),
            catalogs);
        main.Load();
        _window!.DataContext = main;
        _window.Title = Tr.F("Cinedex · {0}", name);
    }

    private static Serilog.Core.Logger CreateLogger(AppPaths paths)
    {
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "cinedex-.log"),
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
        return serilog;
    }

    internal static ServiceProvider ConfigureServices(AppPaths paths, string catalog, Serilog.ILogger serilog, IApiLimitPrompt? prompt = null)
    {
        paths.EnsureCreated();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(serilog, dispose: false));
        services.AddSingleton(paths);
        services.AddSingleton(_ => new ApiGate(prompt));
        services.AddSingleton(sp =>
        {
            var database = new CatalogDatabase(paths.CatalogDatabasePath(catalog), sp.GetRequiredService<ILogger<CatalogDatabase>>());
            database.Initialize(paths.BackupsDirectory);
            return database;
        });
        services.AddSingleton<TitleRepository>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<MetadataRepository>();
        services.AddSingleton(sp => new ImageStore(sp.GetRequiredService<CatalogDatabase>(), paths.CatalogImagesDirectory(catalog)));
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
        services.AddSingleton(sp =>
        {
            var secrets = sp.GetRequiredService<ISecretStore>();
            return new Cinedex.Metadata.Ratings.OmdbClient(
                sp.GetRequiredService<HttpClient>(),
                _ => ValueTask.FromResult(secrets.Read(SecretStore.OmdbKey)));
        });
        services.AddSingleton(sp => new Cinedex.Metadata.Ratings.ImdbRatingsDataset(sp.GetRequiredService<HttpClient>(), paths.CacheDirectory));
        services.AddSingleton<RatingsService>();
        services.AddSingleton<PeopleRepository>();
        services.AddSingleton<PeopleService>();
        services.AddSingleton<MatchReviewRepository>();
        services.AddSingleton<MetadataService>();
        services.AddSingleton<MediaFileRepository>();
        services.AddSingleton<SeriesRepository>();
        services.AddSingleton<NfoService>();
        services.AddSingleton(sp => new LibraryImportService(
            sp.GetRequiredService<TitleRepository>(),
            sp.GetRequiredService<MediaFileRepository>(),
            sp.GetRequiredService<ImageStore>(),
            sp.GetRequiredService<MetadataService>(),
            // Relu à chaque import : un chemin MediaInfo ou ffprobe changé dans les réglages s'applique sans redémarrer.
            () => new Cinedex.Media.MediaProbe(
                sp.GetRequiredService<SettingsStore>().Get(MediaContext.FfprobeKey),
                sp.GetRequiredService<SettingsStore>().Get(MediaContext.MediaInfoKey)),
            sp.GetRequiredService<ILogger<LibraryImportService>>(),
            sp.GetRequiredService<SeriesRepository>(),
            sp.GetRequiredService<NfoService>(),
            sp.GetRequiredService<ApiGate>()));
        services.AddSingleton(sp => new MediaContext(
            sp.GetRequiredService<MediaFileRepository>(),
            sp.GetRequiredService<LibraryImportService>(),
            sp.GetRequiredService<SettingsStore>(),
            sp.GetRequiredService<NfoService>(),
            sp.GetRequiredService<RatingsService>(),
            sp.GetRequiredService<SeriesRepository>(),
            sp.GetRequiredService<PeopleService>(),
            sp.GetRequiredService<ApiGate>()));
        return services.BuildServiceProvider();
    }
}
