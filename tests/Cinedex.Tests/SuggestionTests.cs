using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Suggestions de TMDb pour un titre. Elles sont gardées en base après le seul appel qui les récupère, et
/// elles disent lesquelles sont déjà au catalogue : c'est ce qui distingue une découverte d'un doublon.
/// </summary>
public sealed class SuggestionTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly SuggestionRepository _suggestions;

    public SuggestionTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _suggestions = new SuggestionRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_title_is_asked_once_even_when_tmdb_proposes_nothing()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        Assert.False(_suggestions.WasFetched(id));

        _suggestions.Save(id, []);

        Assert.True(_suggestions.WasFetched(id));
        Assert.Empty(_suggestions.For(id));
    }

    [Fact]
    public void The_suggestions_say_which_ones_are_already_in_the_catalogue()
    {
        var heat = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var owned = _titles.Add(new NewTitle(TitleKind.Movie, "Le Parrain", 1972));
        _titles.Update(_titles.Get(owned)! with { TmdbId = 238 });

        _suggestions.Save(heat, [
            new MetadataSearchResult(TitleKind.Movie, 238, "Le Parrain", null, 1972, null, "/a.jpg", 8.7),
            new MetadataSearchResult(TitleKind.Movie, 111, "Un film absent", null, 1999, null, null, 7.1),
        ]);

        var found = _suggestions.For(heat);

        Assert.Equal(["Le Parrain", "Un film absent"], found.Select(s => s.Name));
        Assert.Equal(owned, found[0].OwnedTitleId);
        Assert.True(found[0].IsOwned);
        Assert.False(found[1].IsOwned);
    }

    [Fact]
    public void Asking_again_replaces_what_was_kept()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _suggestions.Save(id, [new MetadataSearchResult(TitleKind.Movie, 1, "Ancien", null, 1990, null, null, null)]);

        _suggestions.Save(id, [new MetadataSearchResult(TitleKind.Movie, 2, "Nouveau", null, 2000, null, null, null)]);

        Assert.Equal(["Nouveau"], _suggestions.For(id).Select(s => s.Name));
    }
}
