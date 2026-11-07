using Cinedex.Core.Metadata;
using Cinedex.Metadata.Tmdb;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// Référence TMDb : numéro et section. TMDb range les téléfilms sous « tv », avec des numéros indépendants
/// de ceux des films ; le numéro seul ne suffit donc pas à retrouver une fiche, et la section doit survivre
/// à l'enregistrement pour que la mise à jour suivante retombe au bon endroit.
/// </summary>
public sealed class TmdbReferenceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;

    public TmdbReferenceTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("322888", "movie/322888")]
    [InlineData("tv/322888", "tv/322888")]
    [InlineData("TV/322888", "tv/322888")]
    [InlineData("movie/949", "movie/949")]
    [InlineData(" tv/322888 ", "tv/322888")]
    [InlineData("https://www.themoviedb.org/tv/322888-coup-de-foudre-a-saint-petersbourg?language=fr-FR", "tv/322888")]
    [InlineData("https://www.themoviedb.org/movie/949", "movie/949")]
    [InlineData("themoviedb.org/tv/322888", "tv/322888")]
    public void A_reference_is_read_from_what_a_human_would_write(string text, string expected)
    {
        Assert.True(TmdbReference.TryParse(text, TitleKind.Movie, out var reference));
        Assert.Equal(expected, reference.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("tv/0")]
    [InlineData("série/322888")]
    [InlineData("tt0113277")]
    [InlineData("https://exemple.invalide/tv/322888")]
    public void What_is_not_a_reference_is_refused(string? text) =>
        Assert.False(TmdbReference.TryParse(text, TitleKind.Movie, out _));

    [Fact]
    public void A_bare_number_stays_in_the_section_of_the_title()
    {
        Assert.True(TmdbReference.TryParse("949", TitleKind.Movie, out var film));
        Assert.Equal(TitleKind.Movie, film.Kind);

        Assert.True(TmdbReference.TryParse("1396", TitleKind.Series, out var series));
        Assert.Equal(TitleKind.Series, series.Kind);
    }

    [Fact]
    public void The_section_is_only_written_when_it_surprises()
    {
        // Un film rattaché à la section « tv » : la section est gardée, sans quoi la prochaine mise à jour
        // irait chercher ce numéro parmi les films et tomberait sur un autre titre.
        var teleFilm = _repo.Add(new NewTitle(TitleKind.Movie, "Coup de foudre à Saint-Pétersbourg", 2019));
        _repo.Update(_repo.Get(teleFilm)! with { TmdbId = 322888, TmdbKind = TitleKind.Series });

        var reloaded = _repo.Get(teleFilm)!;
        Assert.Equal(TitleKind.Movie, reloaded.Kind);
        Assert.Equal("tv/322888", reloaded.Tmdb!.Text);
        Assert.Equal("tv/322888", reloaded.Tmdb.TextFor(TitleKind.Movie));

        // Un film ordinaire ne garde rien de plus : la section va de soi.
        var film = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _repo.Update(_repo.Get(film)! with { TmdbId = 949, TmdbKind = TitleKind.Movie });
        using (var connection = _db.Open())
        {
            Assert.Null(connection.ExecuteScalar<string?>("SELECT tmdb_kind FROM title WHERE id = @film;", new { film }));
        }

        Assert.Equal("movie/949", _repo.Get(film)!.Tmdb!.Text);
        Assert.Equal("949", _repo.Get(film)!.Tmdb!.TextFor(TitleKind.Movie));
    }

    [Fact]
    public void Two_entries_with_the_same_number_in_different_sections_are_not_duplicates()
    {
        var film = _repo.Add(new NewTitle(TitleKind.Movie, "Un film", 2000));
        var teleFilm = _repo.Add(new NewTitle(TitleKind.Movie, "Un téléfilm", 2019));
        _repo.Update(_repo.Get(film)! with { TmdbId = 322888, TmdbKind = TitleKind.Movie });
        _repo.Update(_repo.Get(teleFilm)! with { TmdbId = 322888, TmdbKind = TitleKind.Series });

        Assert.Empty(new MaintenanceRepository(_db).FindDuplicateTitles());

        // Deux entrées de la même section et du même numéro, en revanche, sont bien des doublons.
        var second = _repo.Add(new NewTitle(TitleKind.Movie, "Le même téléfilm", 2019));
        _repo.Update(_repo.Get(second)! with { TmdbId = 322888, TmdbKind = TitleKind.Series });
        var group = Assert.Single(new MaintenanceRepository(_db).FindDuplicateTitles());
        Assert.Equal([teleFilm, second], group.Titles.Select(t => t.Id).Order());
    }

    /// <summary>
    /// Un téléfilm pris dans la section « tv » de TMDb : la fiche se remplit, le titre reste un film du
    /// catalogue, et la mise à jour suivante retourne d'elle-même dans la section « tv ».
    /// </summary>
    [Fact]
    public async Task A_tv_reference_fills_a_film_without_turning_it_into_a_series()
    {
        var asked = new List<string>();
        var tmdb = new TmdbClient(
            new HttpClient(new Recorder(asked, """
                {"id":322888,"name":"Coup de foudre à Saint-Pétersbourg","first_air_date":"2019-04-20",
                 "overview":"Un téléfilm.","episode_run_time":[95]}
                """)),
            _ => ValueTask.FromResult<string?>("jeton"));
        var images = new ImageStore(_db, Path.Combine(_temp.Path, "img"));
        var service = new Cinedex.App.Services.MetadataService(
            tmdb, new MetadataRepository(_db), images,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Cinedex.App.Services.MetadataService>.Instance);

        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Coup de foudre à Saint-Petersbourg", null));
        Assert.True(TmdbReference.TryParse("tv/322888", TitleKind.Movie, out var reference));

        Assert.True(await service.UpdateAsync(id, reference.Kind, reference.Id, ImportOptions.Default with { KeepKind = true, DownloadPoster = false, DownloadBackdrop = false }, TestContext.Current.CancellationToken));

        var detail = _repo.Get(id)!;
        Assert.Equal(TitleKind.Movie, detail.Kind);          // il reste un film au catalogue
        Assert.Equal("tv/322888", detail.Tmdb!.Text);        // mais sa fiche vient de la section « tv »
        Assert.Equal("Un téléfilm.", detail.Overview);
        Assert.Equal(2019, detail.Year);
        Assert.Contains(asked, p => p.StartsWith("/3/tv/322888", StringComparison.Ordinal));
        Assert.DoesNotContain(asked, p => p.Contains("/movie/", StringComparison.Ordinal));

        // La passe suivante repart de la référence enregistrée, donc de « tv », sans qu'on la lui redonne.
        asked.Clear();
        var again = detail.Tmdb!;
        Assert.True(await service.UpdateAsync(id, again.Kind, again.Id, ImportOptions.Default with { KeepKind = true, DownloadPoster = false, DownloadBackdrop = false }, TestContext.Current.CancellationToken));
        Assert.Contains(asked, p => p.StartsWith("/3/tv/322888", StringComparison.Ordinal));
        Assert.Equal(TitleKind.Movie, _repo.Get(id)!.Kind);
    }

    private sealed class Recorder(List<string> asked, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            asked.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
