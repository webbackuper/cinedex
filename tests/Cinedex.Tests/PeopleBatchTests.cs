using System.Net;
using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core.People;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>
/// La mise à jour des fiches d'acteurs et de réalisateurs va par lots. Chaque lancement doit prendre la suite
/// du précédent : le lot était pris dans les personnes affichées, toujours les mêmes, si bien qu'un second
/// lancement ne trouvait plus rien alors que le reste attendait.
/// </summary>
public sealed class PeopleBatchTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly PeopleRepository _people;

    public PeopleBatchTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _people = new PeopleRepository(_db);
        var title = new TitleRepository(_db).Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        using var connection = _db.Open();
        for (var i = 1; i <= 7; i++)
        {
            connection.Execute("INSERT INTO person (id, name, tmdb_id) VALUES (@i, @name, @tmdb);", new { i, name = $"Personne {i}", tmdb = 1000 + i });
            connection.Execute("INSERT INTO credit (title_id, person_id, role, position) VALUES (@title, @i, 'actor', @i);", new { title, i });
        }

        // Une personne sans numéro TMDb : rien à lui demander, elle n'entre dans aucun lot.
        connection.Execute("INSERT INTO person (id, name) VALUES (99, 'Sans numéro');");
        connection.Execute("INSERT INTO credit (title_id, person_id, role, position) VALUES (@title, 99, 'actor', 99);", new { title });
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Each_batch_takes_what_the_previous_one_left()
    {
        Assert.Equal(7, _people.PendingDetailCount(null, null));

        var first = _people.PendingDetailIds(null, null, 3);
        Assert.Equal(3, first.Count);
        foreach (var id in first)
        {
            _people.SaveDetails(id, new PersonDetail { Id = id, Name = "x", Biography = "Lue." });
        }

        var second = _people.PendingDetailIds(null, null, 3);
        Assert.Equal(3, second.Count);
        Assert.Empty(first.Intersect(second));
        Assert.Equal(4, _people.PendingDetailCount(null, null));

        // Introuvable sur TMDb : elle sort de la file, au lieu d'y revenir à chaque passe.
        _people.MarkDetailsUnavailable(second[0]);
        Assert.Equal(3, _people.PendingDetailCount(null, null));
        Assert.DoesNotContain(second[0], _people.PendingDetailIds(null, null, 10));
    }

    [Fact]
    public void The_queue_follows_the_search_and_the_role()
    {
        Assert.Equal([3L], _people.PendingDetailIds("Personne 3", null, 10));
        Assert.Equal(0, _people.PendingDetailCount(null, "director"));
        Assert.Empty(_people.PendingDetailIds(null, "director", 10));
    }

    [Fact]
    public async Task Running_the_update_again_finishes_the_job_and_says_what_is_left()
    {
        var asked = new List<string>();
        var handler = new Reply(request =>
        {
            asked.Add(request.RequestUri!.AbsolutePath);
            var id = request.RequestUri.AbsolutePath.Split('/')[^1];
            return id == "1002"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"id":{{id}},"name":"Personne","biography":"Lue."}""", System.Text.Encoding.UTF8, "application/json"),
                };
        });
        var service = new PeopleService(_people, new TmdbClient(new HttpClient(handler), _ => ValueTask.FromResult<string?>("jeton")),
            new ImageStore(_db, Path.Combine(_temp.Path, "img")), NullLogger<PeopleService>.Instance);
        var browser = new PeopleBrowserViewModel(service);

        await browser.DownloadDetailsCommand.ExecuteAsync(null);

        // Sept personnes, toutes dans le premier lot : six fiches lues, une introuvable, plus rien à faire.
        Assert.Equal(7, asked.Count);
        Assert.Equal(0, _people.PendingDetailCount(null, null));
        Assert.Contains("6 fiche(s)", browser.Message, StringComparison.Ordinal);
        Assert.Contains("à jour", browser.Message, StringComparison.Ordinal);

        // Relancer n'appelle plus TMDb pour personne.
        asked.Clear();
        await browser.DownloadDetailsCommand.ExecuteAsync(null);
        Assert.Empty(asked);
    }

    private sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
