using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cinedex.App.ViewModels;
using Cinedex.App.Views;
using Cinedex.Core;
using Cinedex.App.Services;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Cinedex.Metadata.Tmdb;
using Cinedex.Tests;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: AssemblyFixture(typeof(Cinedex.UiTests.UiSession))]

namespace Cinedex.UiTests;

/// <summary>
/// Application Cinedex réelle (styles, thèmes, fenêtres) sur la plate-forme Avalonia sans affichage, avec le moteur
/// de rendu Skia : les fenêtres sont mesurées, disposées et dessinées comme à l'écran, et pilotées au clavier et à
/// la souris comme par un utilisateur.
/// </summary>
public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Cinedex.App.App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}

/// <summary>Une seule session Avalonia pour tous les tests d'interface (le fil de l'interface est unique).</summary>
public sealed class UiSession : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));

    public Task Run(Action action) => Session.Dispatch(action, TestContext.Current.CancellationToken);

    /// <summary>Test qui attend le temps réel (minuteries de l'interface, comme la recherche à la frappe).</summary>
    public Task RunAsync(Func<Task> action) => Session.Dispatch(action, TestContext.Current.CancellationToken);

    /// <summary>
    /// La session n'est pas arrêtée explicitement : son arrêt attend la fin du fil de l'interface, qui garde des
    /// minuteries de la fenêtre principale ; la fin du processus de test la libère.
    /// </summary>
    public void Dispose()
    {
    }
}

/// <summary>Un catalogue de test ouvert dans une vraie fenêtre principale.</summary>
internal sealed class UiCatalog : IDisposable
{
    private readonly TempDirectory _temp = new();

    /// <param name="people">Ajoute des films avec distribution et réalisateurs (galerie des personnes).</param>
    public UiCatalog(int titles, bool people = false)
    {
        var paths = new AppPaths(_temp.Path, isPortable: true);
        paths.EnsureCreated();
        Database = new CatalogDatabase(paths.CatalogDatabasePath("tests"));
        Database.Initialize(paths.BackupsDirectory);
        if (titles > 0)
        {
            CatalogSeeder.Seed(Database, titles);
        }

        // Affiches du catalogue généré : l'icône de Cinedex, sous les noms attendus par le générateur.
        var images = paths.CatalogImagesDirectory("tests");
        Directory.CreateDirectory(Path.Combine(images, "seed"));
        for (var i = 0; i < 6; i++)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "poster.png"), Path.Combine(images, "seed", $"{i}.jpg"), overwrite: true);
        }

        Titles = new TitleRepository(Database);
        Window = new MainWindow { Width = 1600, Height = 1000 };
        ViewModel = new MainWindowViewModel(Titles, paths, Window, images: new ImageStore(Database, images), media: people ? SeedPeople(images) : null);
        Window.DataContext = ViewModel;
        var watch = Stopwatch.StartNew();
        ViewModel.Load();
        Window.Show();
        Window.Activate();
        Pump();
        OpenedIn = watch.Elapsed;
    }

    public CatalogDatabase Database { get; }

    /// <summary>Douze films, chacun avec un réalisateur et six acteurs pris parmi quarante (TMDb injoignable).</summary>
    private MediaContext SeedPeople(string images)
    {
        var metadata = new MetadataRepository(Database);
        for (var film = 0; film < 12; film++)
        {
            var credits = new List<MetadataCredit> { new(CreditRole.Director, 9000 + (film % 5), $"Réalisateur {film % 5 + 1}", null, 0, null) };
            credits.AddRange(Enumerable.Range(0, 6).Select(i => new MetadataCredit(CreditRole.Actor, 100 + ((film * 3) + i) % 40, $"Acteur {((film * 3) + i) % 40 + 1}", "Rôle", i, null)));
            metadata.Create(new MetadataRecord { Kind = TitleKind.Movie, SourceId = 50_000 + film, Title = $"Film {film + 1}", Year = 1990 + film, Credits = credits }, ImportOptions.Default);
        }

        var store = new ImageStore(Database, images);
        var offline = new TmdbClient(new HttpClient(new OfflineHandler()), _ => ValueTask.FromResult<string?>(null));
        var people = new PeopleService(new PeopleRepository(Database), offline, store, NullLogger<PeopleService>.Instance);
        var files = new MediaFileRepository(Database);
        var import = new LibraryImportService(Titles, files, store, null, () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        return new MediaContext(files, import, new SettingsStore(Database), People: people);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    public TitleRepository Titles { get; }

    public MainWindow Window { get; }

    public MainWindowViewModel ViewModel { get; }

    /// <summary>Temps entre le chargement du catalogue et la première image dessinée.</summary>
    public TimeSpan OpenedIn { get; }

    public T Find<T>(Func<T, bool>? predicate = null)
        where T : Visual => Window.GetVisualDescendants().OfType<T>().First(c => predicate?.Invoke(c) ?? true);

    public IEnumerable<T> All<T>()
        where T : Visual => Window.GetVisualDescendants().OfType<T>();

    /// <summary>Laisse l'interface travailler : files d'attente, minuteries, mise en page et rendu.</summary>
    public static void Pump(int milliseconds = 0)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (milliseconds > 0)
            {
                Thread.Sleep(15);
            }
        }
        while (DateTime.UtcNow < until);

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Attend en laissant tourner la boucle de l'interface : les minuteries (DispatcherTimer) se déclenchent.</summary>
    public static async Task Wait(int milliseconds)
    {
        await Task.Delay(milliseconds, TestContext.Current.CancellationToken);
        Pump();
    }

    /// <summary>Capture de la fenêtre dans le dossier désigné par CINEDEX_UI_SHOTS (revue visuelle des tests).</summary>
    public static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("CINEDEX_UI_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        Pump();
        using var frame = window.CaptureRenderedFrame();
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        frame?.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>Temps mesurés : fichier désigné par CINEDEX_PERF_LOG s'il est défini.</summary>
    public static void Report(string line)
    {
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable("CINEDEX_PERF_LOG") is { Length: > 0 } log)
        {
            File.AppendAllText(log, line + Environment.NewLine);
        }
    }

    public void Dispose()
    {
        Window.Close();
        _temp.Dispose();
    }
}
