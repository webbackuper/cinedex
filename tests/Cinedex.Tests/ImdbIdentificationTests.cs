using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;

namespace Cinedex.Tests;

/// <summary>
/// « Mettre à jour via TMDb » pour un titre sans numéro TMDb mais avec un numéro IMDb : TMDb sait rendre
/// une fiche depuis un identifiant IMDb, ce qui évite de deviner par le titre et l'année. Un téléfilm ainsi
/// retrouvé vit dans la section « tv » de TMDb, mais reste un film au catalogue.
/// </summary>
public sealed class ImdbIdentificationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;

    public ImdbIdentificationTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task A_missing_tmdb_number_is_found_from_the_imdb_number()
    {
        var asked = new List<string>();
        var service = ServiceWith(asked);
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _repo.Update(_repo.Get(id)! with { ImdbId = "tt0113277" });

        var result = await service.UpdateTitlesAsync(
            [_repo.Get(id)!],
            ImportOptions.Default with { DownloadPoster = false, DownloadBackdrop = false },
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Updated);
        // Le numéro IMDb sert de clé : pas de recherche par titre, donc rien à deviner ni à faire vérifier.
        Assert.Contains(asked, p => p.Contains("/find/tt0113277", StringComparison.Ordinal));
        Assert.DoesNotContain(asked, p => p.Contains("/search/", StringComparison.Ordinal));
        Assert.Equal(0, result.ToReview);

        var detail = _repo.Get(id)!;
        Assert.Equal("movie/949", detail.Tmdb!.Text);
        Assert.Equal("Un film.", detail.Overview);
    }

    [Fact]
    public async Task A_tv_movie_found_by_its_imdb_number_stays_a_film()
    {
        var asked = new List<string>();
        var service = ServiceWith(asked, asTvOnly: true);
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Coup de foudre à Saint-Pétersbourg", 2019));
        _repo.Update(_repo.Get(id)! with { ImdbId = "tt9999999" });

        var result = await service.UpdateTitlesAsync(
            [_repo.Get(id)!],
            ImportOptions.Default with { DownloadPoster = false, DownloadBackdrop = false },
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Updated);
        var detail = _repo.Get(id)!;
        Assert.Equal(TitleKind.Movie, detail.Kind);      // il reste un film, là où son propriétaire l'a rangé
        Assert.Equal("tv/322888", detail.Tmdb!.Text);    // mais sa fiche vient de la section « tv »
        Assert.Equal("Un téléfilm.", detail.Overview);
    }

    private Cinedex.App.Services.MetadataService ServiceWith(List<string> asked, bool asTvOnly = false)
    {
        var tmdb = new TmdbClient(
            new HttpClient(new Router(asked, asTvOnly)),
            _ => ValueTask.FromResult<string?>("jeton"));
        return new Cinedex.App.Services.MetadataService(
            tmdb,
            new MetadataRepository(_db),
            new ImageStore(_db, Path.Combine(_temp.Path, "img")),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Cinedex.App.Services.MetadataService>.Instance);
    }

    /// <summary>Un TMDb de façade : la recherche par identifiant IMDb, puis la fiche de la section voulue.</summary>
    private sealed class Router(List<string> asked, bool asTvOnly) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            asked.Add(path);
            var body = path.Contains("/find/", StringComparison.Ordinal)
                ? asTvOnly
                    ? """{"movie_results":[],"tv_results":[{"id":322888,"name":"Coup de foudre à Saint-Pétersbourg","first_air_date":"2019-04-20"}]}"""
                    : """{"movie_results":[{"id":949,"title":"Heat","release_date":"1995-12-15"}],"tv_results":[]}"""
                : path.Contains("/tv/", StringComparison.Ordinal)
                    ? """{"id":322888,"name":"Coup de foudre à Saint-Pétersbourg","first_air_date":"2019-04-20","overview":"Un téléfilm.","episode_run_time":[95]}"""
                    : """{"id":949,"title":"Heat","release_date":"1995-12-15","overview":"Un film.","runtime":170}""";

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
