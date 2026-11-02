using System.IO.Compression;
using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>Sauvegardes (dumps), export de la liste, colonnes de la liste et style de l'apparence.</summary>
public sealed class BackupExportTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly SettingsStore _settings;
    private readonly string _images;
    private readonly string _dataRoot;

    public BackupExportTests()
    {
        _dataRoot = Directory.CreateDirectory(Path.Combine(_temp.Path, "data")).FullName;
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "catalogue", "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _settings = new SettingsStore(_db);
        _images = Directory.CreateDirectory(Path.Combine(_temp.Path, "catalogue", "images", "ab")).Parent!.FullName;
        File.WriteAllText(Path.Combine(_images, "ab", "abcd.jpg"), "image");
        File.WriteAllText(Path.Combine(_dataRoot, "appearance.json"), "{}");
    }

    public void Dispose() => _temp.Dispose();

    private BackupService Service() =>
        new(new MaintenanceRepository(_db), _settings, "Mes films", _dataRoot, Path.Combine(_temp.Path, "backups"), _images);

    [Fact]
    public void A_dump_contains_what_was_chosen_and_old_ones_are_pruned()
    {
        _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var service = Service();

        var full = service.CreateDump(new BackupSettings());
        using (var zip = ZipFile.OpenRead(full))
        {
            Assert.NotNull(zip.GetEntry("database/cinedex.db"));
            Assert.NotNull(zip.GetEntry("images/ab/abcd.jpg"));
            Assert.NotNull(zip.GetEntry("preferences/appearance.json"));
            Assert.NotNull(zip.GetEntry("manifest.json"));
        }

        var info = Assert.Single(service.List(service.DefaultFolder));
        Assert.Equal((true, true, true, 1), (info.HasDatabase, info.HasImages, info.HasPreferences, info.Titles));
        Assert.NotNull(service.LoadSettings().LastRunUtc);

        // Base seule ; trois sauvegardes au plus.
        var options = new BackupSettings { IncludeImages = false, IncludePreferences = false, Keep = 3 };
        for (var i = 0; i < 4; i++)
        {
            var path = service.CreateDump(options);
            File.Move(path, path.Replace(".zip", $"-{i}.zip", StringComparison.Ordinal)); // noms distincts dans la même seconde
        }

        service.CreateDump(options);
        Assert.Equal(3, service.List(service.DefaultFolder).Count);
        Assert.Throws<InvalidOperationException>(() => service.CreateDump(new BackupSettings { IncludeDatabase = false, IncludeImages = false, IncludePreferences = false }));
    }

    [Fact]
    public void Schedule_decides_when_a_dump_is_due()
    {
        var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(new BackupSettings().IsDue(now, atStartup: true));
        Assert.True(new BackupSettings { Schedule = BackupSchedule.AtStartup }.IsDue(now, atStartup: true));
        Assert.False(new BackupSettings { Schedule = BackupSchedule.AtStartup }.IsDue(now, atStartup: false));
        Assert.True(new BackupSettings { Schedule = BackupSchedule.Daily }.IsDue(now, false));
        Assert.False(new BackupSettings { Schedule = BackupSchedule.Weekly, LastRunUtc = now.AddDays(-3) }.IsDue(now, false));
        Assert.True(new BackupSettings { Schedule = BackupSchedule.Weekly, LastRunUtc = now.AddDays(-8) }.IsDue(now, false));

        new BackupSettings { Schedule = BackupSchedule.Monthly, Keep = 5, Folder = "D:\\Sauvegardes" }.Save(_settings);
        var loaded = BackupSettings.Load(_settings);
        Assert.Equal((BackupSchedule.Monthly, 5, "D:\\Sauvegardes"), (loaded.Schedule, loaded.Keep, loaded.Folder));
    }

    [Fact]
    public void Restore_replaces_the_database_and_adds_missing_images()
    {
        _titles.Add(new NewTitle(TitleKind.Movie, "Avant", 2000));
        var dump = Service().CreateDump(new BackupSettings { IncludePreferences = false });
        _titles.Add(new NewTitle(TitleKind.Movie, "Après", 2001));
        File.Delete(Path.Combine(_images, "ab", "abcd.jpg"));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var safety = BackupService.Restore(dump, _db.DatabasePath, _images, Path.Combine(_temp.Path, "backups"));

        Assert.True(File.Exists(safety));
        Assert.True(File.Exists(Path.Combine(_images, "ab", "abcd.jpg")));
        Assert.Equal(["Avant"], new TitleRepository(new CatalogDatabase(_db.DatabasePath)).ListAll().Select(t => t.Title));
    }

    [Fact]
    public void Export_writes_csv_tsv_and_xlsx_in_the_given_order()
    {
        var heat = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var ran = _titles.Add(new NewTitle(TitleKind.Movie, "Ran; le chaos", 1985));
        using (var connection = _db.Open())
        {
            connection.Execute("UPDATE title SET imdb_rating = 8.3 WHERE id = @heat;", new { heat });
        }

        var export = new ExportRepository(_db);
        var columns = ExportRepository.Columns.Where(c => c.Key is "title" or "year" or "imdb_rating" or "seen").ToList();

        var csv = Path.Combine(_temp.Path, "films.csv");
        Assert.Equal(2, export.Export(csv, ExportFormat.Csv, columns, [ran, heat]));
        var lines = File.ReadAllLines(csv);
        var separator = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        Assert.Equal(3, lines.Length);
        Assert.StartsWith(separator == ";" ? "\"Ran; le chaos\"" : "Ran; le chaos", lines[1], StringComparison.Ordinal);

        var tsv = Path.Combine(_temp.Path, "films.tsv");
        export.Export(tsv, ExportFormat.Tsv, columns, [heat]);
        Assert.Equal(4, File.ReadAllLines(tsv)[1].Split('\t').Length);

        var xlsx = Path.Combine(_temp.Path, "films.xlsx");
        export.Export(xlsx, ExportFormat.Xlsx, columns, [heat, ran]);
        using var zip = ZipFile.OpenRead(xlsx);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = reader.ReadToEnd();
        Assert.Contains("<v>1995</v>", sheet, StringComparison.Ordinal);
        Assert.Contains("<v>8.3</v>", sheet, StringComparison.Ordinal);
        Assert.Contains(">Heat<", sheet, StringComparison.Ordinal);
        Assert.True(sheet.IndexOf(">Heat<", StringComparison.Ordinal) < sheet.IndexOf(">Ran; le chaos<", StringComparison.Ordinal));
        Assert.Equal(["A", "Z", "AA", "AZ", "BA"], ((int[])[0, 25, 26, 51, 52]).Select(ExportRepository.ColumnName));
    }

    [Fact]
    public void List_columns_compose_the_subtitle_and_are_remembered()
    {
        var title = new TitleSummary(1, TitleKind.Series, "Twin Peaks", 1990, 50, null, true, false, false, Medium: "DVD") { Genres = "Drame" };

        Assert.Equal("1990 · Série · DVD · 50 min", new ListColumns().Subtitle(title).Replace("Series", "Série", StringComparison.Ordinal));
        var custom = new ListColumns { Hidden = new HashSet<ListColumn> { ListColumn.Medium, ListColumn.Runtime } };
        Assert.Equal("1990 · Série · Drame", custom.Subtitle(title));

        custom.Save(_settings);
        Assert.False(ListColumns.Load(_settings).Shows(ListColumn.Medium));
        Assert.True(ListColumns.Load(_settings).Shows(ListColumn.Genres));
        Assert.False(new ListColumns().Imdb);
    }

    [Fact]
    public void Appearance_style_is_remembered_with_the_theme()
    {
        Assert.Equal(new AppearanceStyle(), Appearance.LoadStyle(_dataRoot));
        Appearance.Save(_dataRoot, AppTheme.Dark);
        Appearance.SaveStyle(_dataRoot, new AppearanceStyle { Icons = IconSet.Duotone, Menu = MenuColor.Sage, Accent = AccentColor.Blue, Font = "Georgia" });
        Appearance.Save(_dataRoot, AppTheme.Light);

        var style = Appearance.LoadStyle(_dataRoot);
        Assert.Equal((IconSet.Duotone, MenuColor.Sage, AccentColor.Blue, "Georgia"), (style.Icons, style.Menu, style.Accent, style.Font));
        Assert.Equal(AppTheme.Light, Appearance.Load(_dataRoot));
    }

    [Fact]
    public void Every_icon_has_a_drawn_version_for_the_coherent_sets() =>
        Assert.All(Cinedex.App.Controls.Icon.AllKinds, kind => Assert.True(Cinedex.App.Controls.Icon.HasPath(kind), kind));
}
