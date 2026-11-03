using Cinedex.App.Security;
using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly SettingsStore _store;

    public AppSettingsTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _store = new SettingsStore(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Extensions_are_normalized()
    {
        Assert.Equal([".avi", ".mkv", ".mp4"], ScanSettings.NormalizeExtensions("mkv, .MP4  *.avi mkv bad/ext"));
    }

    [Fact]
    public void Settings_tabs_round_trip_and_drive_the_scan()
    {
        var vm = new SettingsViewModel(new NoVault(), _store, _temp.Path);
        Assert.Contains(".mkv", vm.ExtensionsText, StringComparison.Ordinal);
        Assert.Equal(20, vm.MinimumMegabytes);

        vm.ExtensionsText = "mkv";
        vm.MinimumMegabytes = 0;
        vm.IgnoreKeywordsText = "bonus";
        vm.DefaultUseTmdb = true;
        vm.HideAdult = true;
        vm.MainFilterToggles.Single(t => t.Value == QuickFilter.Shortlist).IsShown = false;
        foreach (var t in vm.DefinitionFilterToggles)
        {
            t.IsShown = false;
        }

        vm.SelectedTheme = vm.ThemeChoices.Single(c => c.Value == AppTheme.Dark);
        Assert.True(vm.Save());

        var scan = ScanSettings.Load(_store);
        Assert.Equal([".mkv"], scan.Extensions);
        Assert.True(scan.UseTmdb);
        Assert.Equal(AppTheme.Dark, Appearance.Load(_temp.Path));

        var folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "Films")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "Heat (1995).mkv"), new byte[10]);
        File.WriteAllBytes(Path.Combine(folder, "Ran (1985).avi"), new byte[10]);
        File.WriteAllBytes(Path.Combine(folder, "Heat bonus.mkv"), new byte[10]);
        Assert.Equal(["Heat"], LibraryScanner.Scan(folder, scan.ToScanOptions(), cancellationToken: TestContext.Current.CancellationToken).Select(c => c.BestTitle));

        // Bandeau et contenu adulte appliqués à la fenêtre principale.
        var titles = new TitleRepository(_db);
        titles.Add(new NewTitle(TitleKind.Movie, "Visible", 2000));
        var adult = titles.Add(new NewTitle(TitleKind.Movie, "Adulte", 2000));
        titles.Update(titles.Get(adult)! with { IsAdult = true });
        var files = new MediaFileRepository(_db);
        var import = new LibraryImportService(titles, files, new ImageStore(_db, Path.Combine(_temp.Path, "img")), null,
            () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        var main = new MainWindowViewModel(titles, new AppPaths(_temp.Path, isPortable: false), new NoDialogs(), media: new MediaContext(files, import, _store));
        main.Load();

        Assert.DoesNotContain(main.Filters, f => f.Value == QuickFilter.Shortlist);
        Assert.False(main.HasDefinitionFilters);
        Assert.Equal(["Visible"], main.Titles.Select(t => t.Title));
    }

    [Fact]
    public void Metadata_tab_offers_to_update_the_titles_never_imported_from_tmdb()
    {
        var titles = new TitleRepository(_db);
        var scanned = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var manual = titles.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        var fromNfo = titles.Add(new NewTitle(TitleKind.Movie, "Brazil", 1985));
        var fetched = titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        using (var connection = _db.Open())
        {
            // Lu dans un .nfo : identifiants connus et date posée, mais rien de descriptif n'a été rapatrié.
            connection.Execute(
                "UPDATE title SET tmdb_id = 68, imdb_id = 'tt0088846', metadata_fetched_at = '2026-01-02T03:04:05.000Z' WHERE id = @fromNfo;",
                new { fromNfo });
            connection.Execute(
                "UPDATE title SET tmdb_id = 11645, overview = 'Un seigneur vieillissant partage son royaume.' WHERE id = @fetched;",
                new { fetched });
        }

        Assert.Equal([manual, fromNfo, scanned], titles.WithoutMetadataIds());

        var vm = new SettingsViewModel(new NoVault(), _store, _temp.Path, new StatisticsRepository(_db).Compute());
        Assert.Equal(3, vm.UnlinkedToTmdb);
        Assert.Contains("3 titre(s)", vm.UpdateUnlinkedLabel, StringComparison.Ordinal);
        Assert.True(vm.UpdateUnlinkedCommand.CanExecute(null));

        // Le bouton demande la fermeture des réglages, puis la fenêtre principale lance la mise à jour.
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        vm.UpdateUnlinkedCommand.Execute(null);
        Assert.True(closed);
        Assert.True(vm.PendingUpdateUnlinked);

        // Toutes les fiches complètes : rien à proposer.
        var allFetched = new SettingsViewModel(new NoVault(), _store, _temp.Path,
            new Cinedex.Data.CatalogStatistics { Films = 2, LinkedToTmdb = 2, WithoutMetadata = 0 });
        Assert.False(allFetched.UpdateUnlinkedCommand.CanExecute(null));
    }

    [Fact]
    public void Films_without_metadata_have_their_own_sidebar_view()
    {
        var titles = new TitleRepository(_db);
        titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var fromNfo = titles.Add(new NewTitle(TitleKind.Movie, "Brazil", 1985));
        var byGenre = titles.Add(new NewTitle(TitleKind.Movie, "Dune", 1984));
        var fetched = titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        using (var connection = _db.Open())
        {
            // Cas de JLB : le .nfo a posé les identifiants et la date, sans rien de descriptif. « Brazil » reste listé.
            connection.Execute(
                "UPDATE title SET tmdb_id = 68, metadata_fetched_at = '2026-01-02T03:04:05.000Z' WHERE id = @fromNfo;",
                new { fromNfo });
            // Un genre suffit à sortir de la liste, même sans synopsis.
            connection.Execute(
                "INSERT INTO title_genre (title_id, genre_id) SELECT @byGenre, id FROM genre LIMIT 1;",
                new { byGenre });
            connection.Execute("UPDATE title SET overview = 'Un seigneur vieillissant.' WHERE id = @fetched;", new { fetched });
        }

        var files = new MediaFileRepository(_db);
        var import = new LibraryImportService(titles, files, new ImageStore(_db, Path.Combine(_temp.Path, "img")), null,
            () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        var main = new MainWindowViewModel(titles, new AppPaths(_temp.Path, isPortable: false), new NoDialogs(), media: new MediaContext(files, import, _store));
        main.Load();

        main.SelectedFilter = main.Filters.Single(f => f.Value == QuickFilter.NoMetadata);
        Assert.Equal(["Brazil", "Heat"], main.Titles.Select(t => t.Title));

        // Une vraie fiche rapatriée (le chemin suivi par l'import et la mise à jour) sort de la liste.
        new MetadataRepository(_db).Create(
            new Cinedex.Core.Metadata.MetadataRecord
            {
                Kind = TitleKind.Movie,
                SourceId = 348,
                Title = "Alien",
                Year = 1979,
                Overview = "L'équipage du Nostromo répond à un signal de détresse.",
            },
            Cinedex.Core.Metadata.ImportOptions.Default);
        main.Load();
        main.SelectedFilter = main.Filters.Single(f => f.Value == QuickFilter.NoMetadata);
        Assert.Equal(["Brazil", "Heat"], main.Titles.Select(t => t.Title));

        // Le réglage « Bandeau gauche » propose de masquer cette vue, comme les autres.
        Assert.Contains(new SettingsViewModel(new NoVault(), _store, _temp.Path).MainFilterToggles, t => t.Value == QuickFilter.NoMetadata);
    }

    [Fact]
    public void Wishlist_is_no_longer_offered_in_the_sidebar()
    {
        Assert.DoesNotContain(MainWindowViewModel.AllMainFilters, f => f.Value == QuickFilter.Wishlist);
        Assert.DoesNotContain(new SettingsViewModel(new NoVault(), _store, _temp.Path).MainFilterToggles, t => t.Value == QuickFilter.Wishlist);
    }

    [Fact]
    public void Detail_panel_width_is_remembered_and_kept_in_bounds()
    {
        MainWindowViewModel Open()
        {
            var titles = new TitleRepository(_db);
            var files = new MediaFileRepository(_db);
            var import = new LibraryImportService(titles, files, new ImageStore(_db, Path.Combine(_temp.Path, "img")), null,
                () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
            var vm = new MainWindowViewModel(titles, new AppPaths(_temp.Path, isPortable: false), new NoDialogs(), media: new MediaContext(files, import, _store));
            vm.Load();
            return vm;
        }

        Assert.Equal(MainWindowViewModel.DefaultDetailWidth, Open().DetailWidth);

        Open().DetailWidth = 520;
        Assert.Equal(520, Open().DetailWidth);

        Open().DetailWidth = 5000;
        Assert.Equal(MainWindowViewModel.MaxDetailWidth, Open().DetailWidth);
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

    private sealed class NoDialogs : IDialogs
    {
        public Task<bool> EditTitleAsync(TitleEditorViewModel editor) => Task.FromResult(false);

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);

        public Task InformAsync(string title, string message) => Task.CompletedTask;

        public Task<Cinedex.Core.Metadata.MetadataSearchResult?> SearchMetadataAsync(MetadataSearchViewModel search) =>
            Task.FromResult<Cinedex.Core.Metadata.MetadataSearchResult?>(null);

        public Task<bool> EditSettingsAsync(SettingsViewModel settings) => Task.FromResult(false);

        public Task<bool> RuleSearchAsync(RuleSearchViewModel search) => Task.FromResult(false);

        public Task<bool> BulkEditAsync(BulkEditViewModel edit) => Task.FromResult(false);

        public Task<bool> ImportFolderAsync(ImportFolderViewModel import) => Task.FromResult(false);

        public Task ManageCatalogsAsync(CatalogsViewModel catalogs) => Task.CompletedTask;

        public Task<bool> ShowSeriesAsync(SeriesViewModel series) => Task.FromResult(false);

        public Task<bool> ShowPersonAsync(PersonViewModel person) => Task.FromResult(false);

        public Task<bool> ShowMatchReviewAsync(MatchReviewViewModel review) => Task.FromResult(false);

        public Task<bool> ShowCollectionsAsync(CollectionsViewModel collections) => Task.FromResult(false);

        public Task<bool> ShowSeriesTrackingAsync(SeriesTrackingViewModel tracking) => Task.FromResult(false);

        public void ShowHelp(HelpViewModel help)
        {
        }
    }
}
