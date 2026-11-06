using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// Parcours du catalogue par genre, décennie, pays et langue depuis le bandeau gauche. L'exigence : un clic
/// ne montre que les titres concernés, les listes ne proposent que ce que le catalogue porte, et choisir
/// ailleurs relâche la sélection.
/// </summary>
public sealed class FacetTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;
    private readonly MainWindowViewModel _vm;

    public FacetTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
        _vm = new MainWindowViewModel(_repo, new AppPaths(_temp.Path, isPortable: false), new MainWindowViewModelTests.FakeDialogs());
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_genre_shows_only_its_titles_and_leaving_it_shows_everything_again()
    {
        Film("Heat", 1995, ["crime"], "US", "en");
        Film("Amélie", 2001, ["comedy"], "FR", "fr");
        _vm.Load();

        var genres = _vm.Facets.Single(f => f.Label == "Genres");
        genres.SelectedItem = genres.Items.Single(i => i.Label == "Policier");
        Assert.Equal(["Heat"], _vm.Titles.Select(t => t.Title));

        // Revenir sur une vue de la bibliothèque relâche le genre, même si le filtre ne change pas.
        _vm.LeaveSideViews();
        Assert.Null(genres.SelectedItem);
        Assert.Equal(2, _vm.Titles.Count);
    }

    [Fact]
    public void A_decade_covers_its_ten_years()
    {
        Film("Alien", 1979, ["sci-fi"], "GB", "en");
        Film("Blade Runner", 1982, ["sci-fi"], "US", "en");
        Film("Matrix", 1999, ["sci-fi"], "US", "en");
        _vm.Load();

        var decades = _vm.Facets.Single(f => f.Label == "Décennies");
        Assert.Equal(["1990 – 1999", "1980 – 1989", "1970 – 1979"], decades.Items.Select(i => i.Label));

        decades.SelectedItem = decades.Items.Single(i => i.Label == "1990 – 1999");
        Assert.Equal(["Matrix"], _vm.Titles.Select(t => t.Title));
    }

    [Fact]
    public void Countries_and_languages_are_named_not_coded_and_only_the_ones_present_are_listed()
    {
        Film("Amélie", 2001, ["comedy"], "FR", "fr");
        Film("Heat", 1995, ["crime"], "US", "en");
        _vm.Load();

        // Les noms suivent la langue de l'interface : l'attente se calcule, elle ne se code pas en dur.
        var france = TitleText.CountryName("FR");
        var french = TitleText.LanguageName("fr");
        Assert.NotEqual("FR", france);
        Assert.NotEqual("fr", french);

        var countries = _vm.Facets.Single(f => f.Label == "Pays");
        Assert.Equal(2, countries.Items.Count);
        Assert.DoesNotContain(countries.Items, i => i.Label == "FR");

        countries.SelectedItem = countries.Items.Single(i => i.Label == france);
        Assert.Equal(["Amélie"], _vm.Titles.Select(t => t.Title));

        var languages = _vm.Facets.Single(f => f.Label == "Langues");
        Assert.Equal(2, languages.Items.Count);
        languages.SelectedItem = languages.Items.Single(i => i.Label == french);
        Assert.Equal(["Amélie"], _vm.Titles.Select(t => t.Title));

        // Deux sous-menus ne se cumulent pas : le dernier choisi remplace l'autre.
        Assert.Null(countries.SelectedItem);
    }

    [Fact]
    public void An_empty_catalogue_shows_no_browse_menu()
    {
        _vm.Load();

        Assert.Empty(_vm.Facets);
    }

    private void Film(string title, int year, string[] genres, string country, string language)
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, title, year));
        var detail = _repo.Get(id)!;
        _repo.Update(detail with { GenreCodes = genres, Countries = country, SpokenLanguages = language });
    }
}
