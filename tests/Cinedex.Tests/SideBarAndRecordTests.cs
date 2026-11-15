using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Le bandeau et la fiche de droite, pris seuls, hors de la fenêtre qui les héberge : c'est ce que leur
/// extraction a rendu possible. Le bandeau ne garde qu'un choix à la fois et dit ce qu'il demande ; la fiche
/// montre le titre qu'on lui donne.
/// </summary>
public sealed class SideBarAndRecordTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _titles;
    private readonly long _heat;
    private readonly long _alien;

    public SideBarAndRecordTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _titles = new TitleRepository(db);
        _heat = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _alien = _titles.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        _titles.ApplyBulk([_heat], new BulkEdit { AddGroups = ["Noël"] });
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_banner_asks_for_a_view_and_keeps_one_choice_at_a_time()
    {
        var bar = new SideBarViewModel(_titles, lists: null);
        var asked = new List<SideBarRequest>();
        bar.Requested += (_, request) => asked.Add(request);

        // Relire les listes n'est pas une demande.
        bar.Reload();
        Assert.Empty(asked);
        Assert.Equal(["Noël"], bar.ListNames);

        var decades = bar.Facets.Single(f => f.Items.Any(i => i.Label.Contains("1990", StringComparison.Ordinal)));
        var lists = bar.Facets.Single(f => f.Items.Any(i => i.GroupName == "Noël"));

        decades.SelectedItem = decades.Items.First(i => i.Label.Contains("1990", StringComparison.Ordinal));
        var first = Assert.Single(asked);
        Assert.Equal(SideBarSource.Facet, first.Source);
        Assert.NotNull(first.Rules);
        Assert.Null(first.ListName);
        Assert.False(bar.HasPersonalList);

        // Une liste personnelle : l'autre sous-menu est relâché, et la demande porte le nom de la liste.
        lists.SelectedItem = lists.Items.Single();
        Assert.Equal(2, asked.Count);
        Assert.Equal("Noël", asked[1].ListName);
        Assert.Null(decades.SelectedItem);
        Assert.True(bar.HasPersonalList);

        // Relâcher sans bruit : rien n'est demandé, la fenêtre a déjà décidé d'afficher autre chose.
        bar.ReleaseAll();
        Assert.Equal(2, asked.Count);
        Assert.Null(lists.SelectedItem);
        Assert.False(bar.HasPersonalList);
        Assert.False(bar.ReleaseDisc());
    }

    [Fact]
    public void The_banner_folds_to_a_rail_and_back()
    {
        var bar = new SideBarViewModel(_titles, lists: null);
        Assert.False(bar.IsRail);

        bar.ToggleRailCommand.Execute(null);
        Assert.True(bar.IsRail);

        bar.ToggleRailCommand.Execute(null);
        Assert.False(bar.IsRail);
    }

    [Fact]
    public void The_record_shows_the_title_it_is_given()
    {
        var record = new RecordViewModel(_titles, images: null, media: null, new CatalogExtras(), title => title);
        Assert.False(record.HasDetail);

        record.Show(_heat);

        Assert.True(record.HasDetail);
        Assert.Equal("Heat", record.Detail!.Title);
        Assert.False(record.IsSeries);
        Assert.False(record.HasTrailer);
        Assert.Empty(record.Cast);
        Assert.Empty(record.Similar);

        record.Show(_alien);
        Assert.Equal("Alien", record.Detail!.Title);

        // Rien à montrer : la sélection est vide ou multiple.
        record.Show(null);
        Assert.False(record.HasDetail);
        Assert.Null(record.PosterPath);
        Assert.Empty(record.DetailFiles);
    }
}
