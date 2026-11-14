using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>
/// Les onglets Collection et Recommandations de la fiche. Vus depuis le film, ils disent ce que la saga
/// contient et ce qui en manque, et ce que l'on pourrait regarder ensuite, sans quitter la fiche.
/// </summary>
public sealed class TitleCardTabsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly CollectionRepository _collections;
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();

    public TitleCardTabsTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _collections = new CollectionRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task The_collection_tab_shows_the_saga_including_what_is_missing()
    {
        // Une trilogie : deux films au catalogue, le troisième connu de TMDb mais absent.
        var saga = _titles.CreateCollection("Retour vers le futur - Saga");
        _collections.SaveDetails(saga, new CollectionRecord(
            264, "Retour vers le futur - Saga", "Une trilogie.", null, null,
            [new CollectionPart(105, "Retour vers le futur", 1985, null),
             new CollectionPart(165, "Retour vers le futur 2", 1989, null),
             new CollectionPart(196, "Retour vers le futur 3", 1990, null)]));

        var first = _titles.Add(new NewTitle(TitleKind.Movie, "Retour vers le futur", 1985));
        var second = _titles.Add(new NewTitle(TitleKind.Movie, "Retour vers le futur 2", 1989));
        _titles.Update(_titles.Get(first)! with { TmdbId = 105, TmdbKind = TitleKind.Movie });
        _titles.Update(_titles.Get(second)! with { TmdbId = 165, TmdbKind = TitleKind.Movie });
        _titles.ApplyBulk([first, second], new BulkEdit { CollectionId = saga });

        var editor = await OpenCardOf(first);

        Assert.True(editor.HasCollectionFilms);
        Assert.Equal("Retour vers le futur - Saga", editor.CollectionTitle);
        // Rangés par année, comme une saga se regarde.
        Assert.Equal(
            ["Retour vers le futur", "Retour vers le futur 2", "Retour vers le futur 3"],
            editor.CollectionFilms.Select(f => f.Title));

        var current = editor.CollectionFilms[0];
        Assert.True(current.IsCurrent);
        Assert.False(current.CanOpen);                      // la fiche ouverte ne s'ouvre pas elle-même
        Assert.Contains("Cette fiche", current.Subtitle, StringComparison.Ordinal);

        var sibling = editor.CollectionFilms[1];
        Assert.True(sibling.CanOpen);
        Assert.Equal(second, sibling.TitleId);

        var missing = editor.CollectionFilms[2];
        Assert.False(missing.IsOwned);
        Assert.False(missing.CanOpen);
        Assert.Null(missing.TitleId);
        Assert.Contains("Manquant", missing.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_film_alone_in_its_collection_gets_no_tab()
    {
        // Un coffret fait à la main, un seul film dedans : l'onglet n'apprendrait rien.
        var boxed = _titles.CreateCollection("Mon coffret");
        var only = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _titles.ApplyBulk([only], new BulkEdit { CollectionId = boxed });

        var editor = await OpenCardOf(only);

        Assert.False(editor.HasCollectionFilms);
        Assert.Null(editor.CollectionTitle);
    }

    [Fact]
    public async Task A_film_without_a_collection_gets_no_tab()
    {
        var alone = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));

        var editor = await OpenCardOf(alone);

        Assert.False(editor.HasCollectionFilms);
    }

    [Fact]
    public void The_recommendations_tab_holds_both_sources_and_hides_when_empty()
    {
        var heat = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var casino = _titles.Add(new NewTitle(TitleKind.Movie, "Casino", 1995));

        // Rien à montrer et aucun moyen de demander : pas d'onglet.
        Assert.False(Editor(_titles.Get(heat)!, new TitleEditorExtras()).HasRecommendations);

        // Le bouton seul suffit à justifier l'onglet : c'est là qu'on va chercher les suggestions.
        var asked = 0;
        var withButton = Editor(_titles.Get(heat)!, new TitleEditorExtras(
            FetchSuggestions: () =>
            {
                asked++;
                return Task.FromResult<IReadOnlyList<TmdbSuggestion>>(
                    [new TmdbSuggestion(TitleKind.Movie, 524, "Casino", 1995, null, 8.2, casino)]);
            }));
        Assert.True(withButton.HasRecommendations);
        Assert.False(withButton.HasSuggestions);
        Assert.True(withButton.FetchSuggestionsCommand.CanExecute(null));

        withButton.FetchSuggestionsCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.True(withButton.HasSuggestions);
        Assert.Equal("Casino", withButton.Suggestions[0].Name);
        // Un seul appel par titre : le résultat est gardé, donc le bouton se referme.
        Assert.False(withButton.FetchSuggestionsCommand.CanExecute(null));

        // « Vous aimerez aussi » vient de la base, donc sans bouton ni réseau.
        var withSimilar = Editor(_titles.Get(heat)!, new TitleEditorExtras(
            Similar: [new SimilarTitle(_titles.Query(new TitleQuery(Ids: [casino]))[0], "même année")]));
        Assert.True(withSimilar.HasRecommendations);
        Assert.True(withSimilar.HasSimilar);
        Assert.Equal("même année", withSimilar.Similar[0].Reason);
    }

    [Fact]
    public async Task A_suggestion_we_do_not_own_is_handed_back_to_the_window()
    {
        var heat = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        TmdbSuggestion? handed = null;
        var editor = Editor(_titles.Get(heat)!, new TitleEditorExtras(
            AddSuggestion: s =>
            {
                handed = s;
                return Task.CompletedTask;
            }));

        var wanted = new TmdbSuggestion(TitleKind.Movie, 524, "Casino", 1995, null, 8.2, null);
        await editor.AddSuggestionAsync(wanted);

        // La fiche ne demande rien à TMDb elle-même : c'est la fenêtre qui pose la question et importe.
        Assert.Same(wanted, handed);
    }

    private TitleEditorViewModel Editor(TitleDetail title, TitleEditorExtras extras) =>
        new(title, _titles.Genres(), _titles.RefItems("medium"), _titles.RefItems("case"), _titles.RefItems("version"), _titles.Collections(), extras);

    /// <summary>Ouvre la fiche du titre par la fenêtre principale, et rend l'éditeur qu'elle a construit.</summary>
    private async Task<TitleEditorViewModel> OpenCardOf(long titleId)
    {
        var tmdb = new TmdbClient(new HttpClient(new Offline()), _ => ValueTask.FromResult<string?>("jeton"));
        var images = new ImageStore(_db, Path.Combine(_temp.Path, "img"));
        var settings = new SettingsStore(_db);
        var collections = new CollectionService(_collections, tmdb, images, NullLogger<CollectionService>.Instance, settings);
        var metadata = new MetadataService(
            tmdb, new MetadataRepository(_db), images, NullLogger<MetadataService>.Instance,
            settings: settings, collections: collections);

        var vm = new MainWindowViewModel(_titles, new AppPaths(_temp.Path, isPortable: false), _dialogs, metadata, images);
        vm.Load();
        vm.SelectedTitles.Clear();
        vm.SelectedTitles.Add(vm.Titles.Single(t => t.Id == titleId));

        await vm.EditCommand.ExecuteAsync(null);

        return _dialogs.Edited ?? throw new InvalidOperationException("La fiche ne s'est pas ouverte.");
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Aucun appel réseau attendu : les onglets lisent la base.");
    }
}
