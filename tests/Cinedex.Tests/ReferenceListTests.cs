using System.IO.Compression;
using System.Text;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Ratings;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// Listes de référence. Ce qui a de la valeur n'est pas la liste mais ce qu'elle dit du catalogue : combien
/// vous en avez, lesquels manquent. Le rapprochement se fait sur les identifiants, jamais sur le titre.
/// </summary>
public sealed class ReferenceListTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly ReferenceListRepository _lists;

    public ReferenceListTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _lists = new ReferenceListRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_list_counts_what_the_catalogue_owns_and_what_it_lacks()
    {
        Owned("Le Parrain", 1972, "tt0068646");
        Owned("Un autre film", 1999, "tt9999999");

        _lists.Replace("Top 250", "imdb_top250", "estimé", [
            Entry(1, "tt0068646", "The Godfather", 1972),
            Entry(2, "tt0111161", "The Shawshank Redemption", 1994),
        ]);

        var list = _lists.Find("Top 250")!;
        Assert.Equal(2, list.Total);
        Assert.Equal(1, list.Owned);
        Assert.Equal(1, list.Missing);

        var entries = _lists.Entries(list.Id);
        Assert.True(entries[0].IsOwned);
        Assert.False(entries[1].IsOwned);
    }

    [Fact]
    public void The_same_title_and_year_is_not_a_match_without_an_identifier()
    {
        // Deux films différents peuvent porter le même titre la même année : seul l'identifiant fait foi.
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "The Godfather", 1972));
        Assert.Null(_titles.Get(id)!.ImdbId);

        _lists.Replace("Top 250", "imdb_top250", null, [Entry(1, "tt0068646", "The Godfather", 1972)]);

        Assert.Equal(0, _lists.Find("Top 250")!.Owned);
    }

    [Fact]
    public void A_wishlist_entry_does_not_count_as_owned()
    {
        var id = Owned("Le Parrain", 1972, "tt0068646");
        _titles.SetOwned([id], false);

        _lists.Replace("Top 250", "imdb_top250", null, [Entry(1, "tt0068646", "The Godfather", 1972)]);

        Assert.Equal(0, _lists.Find("Top 250")!.Owned);
    }

    [Fact]
    public void Rebuilding_replaces_the_list_instead_of_adding_to_it()
    {
        _lists.Replace("Top 250", "imdb_top250", null, [Entry(1, "tt0068646", "The Godfather", 1972)]);
        _lists.Replace("Top 250", "imdb_top250", null, [Entry(1, "tt0111161", "The Shawshank Redemption", 1994)]);

        var list = _lists.Find("Top 250")!;
        Assert.Equal(1, list.Total);
        Assert.Equal("The Shawshank Redemption", _lists.Entries(list.Id)[0].Title);
    }

    [Fact]
    public void The_list_view_shows_the_titles_of_the_catalogue_that_belong_to_it()
    {
        var godfather = Owned("Le Parrain", 1972, "tt0068646");
        Owned("Un film hors liste", 1999, "tt9999999");
        _lists.Replace("Top 250", "imdb_top250", null, [Entry(1, "tt0068646", "The Godfather", 1972)]);

        var rows = _titles.Query(new TitleQuery(Rules: new RuleSearch([new SearchRule("list", RuleOperator.Equals, "Top 250")])));

        var only = Assert.Single(rows);
        Assert.Equal(godfather, only.Id);
    }

    [Fact]
    public async Task The_chart_keeps_well_voted_feature_films_and_favours_the_widely_seen()
    {
        var chart = new ImdbTopChart(new HttpClient(), _temp.Path);
        var ratings = Path.Combine(_temp.Path, "title.ratings.tsv.gz");
        Gzip(ratings, """
            tconst	averageRating	numVotes
            tt0000001	9.9	100
            tt0000002	8.5	2000000
            tt0000003	9.0	30000
            tt0000004	9.5	900000
            tt0000005	9.9	800000
            tt0000006	6.0	100000
            tt0000007	6.0	100000
            tt0000008	6.0	100000
            """);
        Gzip(chart.BasicsPath, """
            tconst	titleType	primaryTitle	originalTitle	isAdult	startYear	endYear	runtimeMinutes	genres
            tt0000001	movie	Trop peu vu	Trop peu vu	0	1990	\N	100	Drama
            tt0000002	movie	Vu par tous	Vu par tous	0	1995	\N	120	Crime
            tt0000003	movie	Juste au seuil	Juste au seuil	0	2000	\N	110	Drama
            tt0000004	tvSeries	Une série	Une série	0	2005	\N	45	Drama
            tt0000005	movie	Pour adultes	Pour adultes	1	2010	\N	80	Adult
            tt0000006	movie	Moyen un	Moyen un	0	2011	\N	95	Drama
            tt0000007	movie	Moyen deux	Moyen deux	0	2012	\N	95	Drama
            tt0000008	movie	Moyen trois	Moyen trois	0	2013	\N	95	Drama
            """);

        var top = await chart.ComputeAsync(ratings, 10, cancellationToken: TestContext.Current.CancellationToken);

        // Le film trop peu voté, la série et le film pour adultes n'entrent pas au classement.
        Assert.DoesNotContain(top, e => e.ImdbId is "tt0000001" or "tt0000004" or "tt0000005");

        // Noté un peu moins haut mais vu par deux millions de personnes, il passe devant celui qui est à peine
        // au-dessus du seuil de votes : c'est tout l'objet de la moyenne bayésienne.
        Assert.Equal("tt0000002", top[0].ImdbId);
        Assert.Equal("tt0000003", top[1].ImdbId);
        Assert.Equal(1, top[0].Rank);
        Assert.True(top[1].Weighted < top[1].Rating, "la note du film à peine éligible doit être tirée vers la moyenne");
    }

    private static void Gzip(string path, string content)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        gzip.Write(Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n") + "\n"));
    }

    private static ReferenceEntry Entry(int rank, string imdbId, string title, int year) =>
        new(rank, imdbId, null, title, year, null, null, null);

    private long Owned(string title, int year, string imdbId)
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, title, year));
        using var connection = _db.Open();
        connection.Execute("UPDATE title SET imdb_id = @imdbId WHERE id = @id;", new { id, imdbId });
        return id;
    }
}
