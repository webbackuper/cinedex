using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// La bande-annonce rapatriée de TMDb se lit depuis la fiche. L'exigence porte sur deux points : n'ouvrir
/// qu'une plateforme vidéo connue, et ne proposer le rattrapage que sur les fiches qui en manquent vraiment.
/// </summary>
public sealed class TrailerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;

    public TrailerTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abcdef12345", true)]
    [InlineData("https://youtu.be/abcdef12345", true)]
    [InlineData("https://vimeo.com/76979871", true)]
    [InlineData("http://www.youtube.com/watch?v=abcdef12345", false)]
    [InlineData("https://exemple.invalide/charge-utile", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_known_video_platform_over_https_is_opened(string? url, bool expected) =>
        Assert.Equal(expected, WebLinks.IsVideo(url));

    [Fact]
    public void The_catch_up_pass_targets_only_tmdb_linked_titles_without_a_trailer()
    {
        var missing = Linked("Heat", 603, trailer: null);
        Linked("Matrix", 604, trailer: "https://www.youtube.com/watch?v=vKQi3bBA1y8");
        var locked = Linked("Seven", 605, trailer: null);
        using (var connection = _db.Open())
        {
            connection.Execute("UPDATE title SET is_locked = 1 WHERE id = @locked;", new { locked });
        }

        // Jamais rattachée à TMDb : la recherche par titre est le travail de « sans infos », pas celui-ci.
        _repo.Add(new NewTitle(TitleKind.Movie, "Film scanné", 2001));

        Assert.Equal([missing], _repo.WithoutTrailerIds());
    }

    private long Linked(string title, long tmdbId, string? trailer)
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, title, 1995));
        using var connection = _db.Open();
        connection.Execute(
            "UPDATE title SET tmdb_id = @tmdbId, trailer_url = @trailer WHERE id = @id;",
            new { id, tmdbId, trailer });
        return id;
    }
}
