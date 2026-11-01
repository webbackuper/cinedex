using System.Net;
using System.Text;
using Bobine.App.Security;
using Bobine.App.Services;
using Bobine.App.ViewModels;
using Bobine.Core.Metadata;
using Bobine.Core.Titles;
using Bobine.Data;
using Bobine.Metadata.Tmdb;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bobine.Tests;

public sealed class MaxActorsTests
{
    private const string Movie = """
        {"id":949,"title":"Heat","release_date":"1995-12-15",
         "credits":{"cast":[
           {"id":1,"name":"A","character":"a","order":0},{"id":2,"name":"B","character":"b","order":1},
           {"id":3,"name":"C","character":"c","order":2},{"id":4,"name":"D","character":"d","order":3}],"crew":[]}}
        """;

    [Fact]
    public async Task Number_of_actors_set_in_settings_is_applied_to_imports()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
        db.Initialize(temp.Path);
        var settings = new SettingsStore(db);
        var vm = new SettingsViewModel(new Vault(), settings);
        Assert.Equal(MetadataService.DefaultMaxActors, vm.MaxActorsNumber);

        vm.MaxActorsNumber = 0;
        Assert.False(vm.Save());
        vm.MaxActorsNumber = 2;
        Assert.True(vm.Save());

        var tmdb = new TmdbClient(new HttpClient(new Handler()), _ => ValueTask.FromResult<string?>("token"));
        var service = new MetadataService(tmdb, new MetadataRepository(db), new ImageStore(db, Path.Combine(temp.Path, "img")), NullLogger<MetadataService>.Instance, settings: settings);
        var (id, _) = await service.ImportAsync(new MetadataSearchResult(TitleKind.Movie, 949, "Heat", null, 1995, null, null, null), ImportOptions.Default, TestContext.Current.CancellationToken);

        using var connection = db.Open();
        Assert.Equal(2, connection.ExecuteScalar<int>("SELECT count(*) FROM credit WHERE title_id = @id AND role = 'actor';", new { id }));
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/movie/949", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Movie, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class Vault : ISecretStore
    {
        public string? Read(string name) => null;

        public void Write(string name, string secret)
        {
        }

        public void Delete(string name)
        {
        }
    }
}
