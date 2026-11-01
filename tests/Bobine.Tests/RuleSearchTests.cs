using Bobine.Core.Titles;
using Bobine.Data;

namespace Bobine.Tests;

public sealed class RuleSearchTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;

    public RuleSearchTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "bobine.db"));
        db.Initialize(_temp.Path);
        _titles = new TitleRepository(db);
        _files = new MediaFileRepository(db);
        Add("Intouchables", 2011, 8.5, "comedy");
        Add("Le Dîner de cons", 1998, 7.6, "comedy");
        Add("OSS 117", 2006, 6.9, "comedy");
        Add("Heat", 1995, 8.3, "crime");
        Add("Sans note", 2010, null, "comedy");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Comedy_with_imdb_above_7_since_2000()
    {
        var found = Find(true,
            new SearchRule("genre", RuleOperator.Equals, "comedy"),
            new SearchRule("imdb_rating", RuleOperator.Greater, "7"),
            new SearchRule("year", RuleOperator.GreaterOrEqual, "2000"));

        Assert.Equal(["Intouchables"], found);
    }

    [Fact]
    public void Any_rule_with_or_negations_and_empty_values()
    {
        Assert.Equal(["Heat", "Le Dîner de cons"], Find(false, new SearchRule("year", RuleOperator.Less, "2000"), new SearchRule("title", RuleOperator.Equals, "heat")));
        Assert.Equal(["Heat"], Find(true, new SearchRule("genre", RuleOperator.NotEquals, "comedy")));
        Assert.Equal(["Sans note"], Find(true, new SearchRule("imdb_rating", RuleOperator.IsEmpty)));
        Assert.Equal(["Heat", "Intouchables", "Le Dîner de cons"], Find(true, new SearchRule("imdb_rating", RuleOperator.GreaterOrEqual, "7,5")));
        Assert.Equal(["Heat", "Intouchables", "Le Dîner de cons", "OSS 117"], Find(true, new SearchRule("title", RuleOperator.NotContains, "note")));
    }

    [Fact]
    public void Files_flags_and_unknown_fields()
    {
        var heat = _titles.ListAll().Single(t => t.Title == "Heat").Id;
        _files.Attach(heat, Path.Combine(_temp.Path, "Heat.mkv"), 10);
        _titles.SetFavorite([heat], true);

        Assert.Equal(["Heat"], Find(true, new SearchRule("file_path", RuleOperator.Contains, "heat.mkv")));
        Assert.Equal(["Heat"], Find(true, new SearchRule("favorite", RuleOperator.IsTrue)));
        Assert.Equal(4, Find(true, new SearchRule("favorite", RuleOperator.IsFalse)).Count);
        Assert.Equal(["Heat"], Find(true, new SearchRule("parts", RuleOperator.Equals, "1")));

        // Champ inconnu ou valeur non numérique : la condition est ignorée, jamais injectée.
        Assert.Equal(5, Find(true, new SearchRule("t.title; DROP TABLE title", RuleOperator.Equals, "x"), new SearchRule("year", RuleOperator.Greater, "abc")).Count);
    }

    private List<string> Find(bool all, params SearchRule[] rules) =>
        _titles.Query(new TitleQuery(Rules: new RuleSearch(rules, all))).Select(t => t.Title).Order(StringComparer.Ordinal).ToList();

    private void Add(string title, int year, double? imdb, string genre)
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, title, year));
        _titles.Update(_titles.Get(id)! with { ImdbRating = imdb, GenreCodes = [genre] });
    }
}
