using System.Diagnostics;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Catalogue de 12 000 films : les requêtes de la liste principale doivent rester rapides (budgets larges pour
/// les machines d'intégration continue ; les temps mesurés sont écrits dans la sortie du test).
/// </summary>
public sealed class PerformanceTests : IClassFixture<PerformanceTests.BigCatalog>
{
    public const int Titles = 12_000;

    private readonly BigCatalog _catalog;

    public PerformanceTests(BigCatalog catalog) => _catalog = catalog;

    public sealed class BigCatalog : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public BigCatalog()
        {
            Database = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
            Database.Initialize(_temp.Path);
            CatalogSeeder.Seed(Database, Titles);
            // CINEDEX_PERF_DB=<dossier> : garde une copie du catalogue généré (analyse des requêtes, captures).
            if (Environment.GetEnvironmentVariable("CINEDEX_PERF_DB") is { Length: > 0 } copy)
            {
                using var connection = Database.Open();
                Database.BackupTo(connection, copy, "perf");
            }
        }

        public CatalogDatabase Database { get; }

        public void Dispose() => _temp.Dispose();
    }

    /// <summary>Temps mesurés : sortie du test, et fichier désigné par CINEDEX_PERF_LOG s'il est défini.</summary>
    private static void Report(string line)
    {
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable("CINEDEX_PERF_LOG") is { Length: > 0 } log)
        {
            File.AppendAllText(log, line + Environment.NewLine);
        }
    }

    private static long Time(Action action)
    {
        action(); // Premier passage : caches de SQLite et de .NET.
        var watch = Stopwatch.StartNew();
        action();
        return watch.ElapsedMilliseconds;
    }

    [Theory]
    [InlineData(QuickFilter.All, SortField.Title, null, 1500)]
    [InlineData(QuickFilter.All, SortField.Year, null, 1500)]
    [InlineData(QuickFilter.All, SortField.Number, null, 1500)]
    [InlineData(QuickFilter.Seen, SortField.Title, null, 1500)]
    [InlineData(QuickFilter.HighDefinition, SortField.Title, null, 1500)]
    [InlineData(QuickFilter.UltraHighDefinition, SortField.Title, null, 1500)]
    [InlineData(QuickFilter.Loaned, SortField.Title, null, 800)]
    [InlineData(QuickFilter.All, SortField.Title, "retour", 800)]
    public void Main_list_queries_stay_fast(QuickFilter filter, SortField sort, string? text, int budgetMs)
    {
        var titles = new TitleRepository(_catalog.Database);
        IReadOnlyList<TitleSummary> result = [];
        var elapsed = Time(() => result = titles.Query(new TitleQuery(text, filter, sort)));

        Report($"{filter} / {sort} / {text ?? "-"} : {result.Count} titres en {elapsed} ms");
        Assert.NotEmpty(result);
        Assert.True(elapsed < budgetMs, $"{elapsed} ms (budget {budgetMs} ms)");
    }

    [Fact]
    public void Statistics_and_duplicates_stay_fast()
    {
        var statistics = Time(() => new StatisticsRepository(_catalog.Database).Compute());
        var duplicates = Time(() => new MaintenanceRepository(_catalog.Database).FindDuplicateTitles());
        Report($"Statistiques {statistics} ms, doublons {duplicates} ms");
        Assert.True(statistics < 3000, $"statistiques {statistics} ms");
        Assert.True(duplicates < 3000, $"doublons {duplicates} ms");
    }
}
