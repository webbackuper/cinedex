namespace Cinedex.UiTests;

/// <summary>
/// Le complément de la fiche (générique, films proches, infos techniques) arrive après un court arrêt, pour
/// que le parcours au clavier ne paie pas la lecture complète de chaque fiche traversée.
/// </summary>
public sealed class DetailLoadingUiTests(UiSession ui)
{
    [Fact]
    public Task Browsing_shows_the_record_at_once_and_its_cast_once_settled() => ui.RunAsync(async () =>
    {
        using var catalog = new UiCatalog(0, people: true);
        var vm = catalog.ViewModel;
        var first = vm.Titles.First(t => t.Title == "Film 1");
        var second = vm.Titles.First(t => t.Title == "Film 2");

        vm.SelectedTitles.Add(first);
        UiCatalog.Pump();

        // La fiche elle-même est là tout de suite ; le générique attend qu'on s'arrête.
        Assert.Equal(first.Id, vm.Record.Detail!.Id);
        Assert.Empty(vm.Record.Cast);

        // On passe au titre suivant avant la fin du délai : le premier générique n'est jamais chargé.
        vm.SelectedTitles.Clear();
        vm.SelectedTitles.Add(second);
        UiCatalog.Pump();
        Assert.Equal(second.Id, vm.Record.Detail!.Id);
        Assert.Empty(vm.Record.Cast);

        // Le délai est une vraie minuterie : il faut laisser tourner le répartiteur, pas seulement vider sa file.
        await UiCatalog.Wait(600);

        Assert.Equal(6, vm.Record.Cast.Count);
        Assert.Single(vm.Record.Directors);
        Assert.True(vm.Record.HasSelectionPeople);
        // « Voir aussi » nomme bien les personnes du film affiché, pas celles du précédent.
        Assert.Contains(vm.Record.SelectionPeople, p => p.Label.Contains(vm.Record.Directors[0].Name, StringComparison.Ordinal));
    });

    [Fact]
    public Task Acting_before_the_delay_loads_the_cast_on_the_spot() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(0, people: true);
        var vm = catalog.ViewModel;

        vm.SelectedTitles.Add(vm.Titles.First(t => t.Title == "Film 3"));
        UiCatalog.Pump();
        Assert.Empty(vm.Record.Cast);

        // Un double-clic ou un clic droit dans le délai : ce qui s'ouvre a besoin du générique maintenant.
        vm.Record.EnsureLoaded();

        Assert.Equal(6, vm.Record.Cast.Count);
        Assert.Single(vm.Record.Directors);
    });
}
