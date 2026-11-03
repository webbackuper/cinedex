using System.Collections.ObjectModel;
using Cinedex.Core.Localization;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.Android.ViewModels;

/// <summary>Catalogue en lecture seule sur le téléphone : recherche, liste avec affiches, fiche d'un titre.</summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    private readonly PortableCatalog _catalog;
    private TitleRepository? _titles;
    private ImageStore? _images;
    private PeopleRepository? _people;

    public CatalogViewModel(PortableCatalog catalog)
    {
        _catalog = catalog;
        Reload();
    }

    public ObservableCollection<TitleSummary> Titles { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => Refresh();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(IsListVisible))]
    public partial TitlePage? Detail { get; set; }

    public bool HasDetail => Detail is not null;

    public bool IsListVisible => Detail is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasCatalog { get; set; }

    public bool IsEmpty => !HasCatalog;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    public static string SearchPlaceholder => Tr.T("Rechercher un titre, un acteur…");

    public static string ImportLabel => Tr.T("Importer une sauvegarde");

    public static string EmptyText => Tr.T("Aucun catalogue sur ce téléphone. Sur l'ordinateur, créez une sauvegarde (Réglages > Sauvegardes), copiez le fichier .zip sur le téléphone, puis importez-le ici.");

    public static string BackLabel => Tr.T("Retour");

    /// <summary>Ouvre la fiche d'un titre touché dans la liste.</summary>
    [RelayCommand]
    private void Open(TitleSummary? title)
    {
        if (title is null || _titles?.Get(title.Id) is not { } detail)
        {
            return;
        }

        var credits = _people?.Credits(detail.Id) ?? [];
        Detail = new TitlePage(
            detail.Title,
            string.Join(" · ", new[] { detail.Year?.ToString(System.Globalization.CultureInfo.CurrentCulture), detail.RuntimeText }.Where(p => !string.IsNullOrEmpty(p))),
            _images?.PathFor(detail.Id, "poster"),
            _titles.GenreLabels(detail.Id).Select(Tr.T).ToList(),
            detail.UserRating is { } mine ? Tr.F("Ma note : {0:0.#} / 10", mine) : null,
            detail.ImdbRating is { } imdb ? Tr.F("IMDb : {0:0.0}", imdb) : null,
            detail.Overview,
            Names(credits, "director", Tr.T("Réalisation")),
            Names(credits.Where(c => c.Role == "actor").Take(12), "actor", Tr.T("Avec")));
    }

    [RelayCommand]
    private void Back() => Detail = null;

    /// <summary>Remplace le catalogue du téléphone par une sauvegarde ; renvoie faux en cas d'échec (message dans <see cref="Status"/>).</summary>
    public bool Import(Stream archive)
    {
        try
        {
            Detail = null;
            var images = _catalog.Import(archive);
            Reload();
            Status = Tr.F("Catalogue importé : {0} titre(s), {1} image(s).", Titles.Count, images);
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or Data.CatalogCorruptedException)
        {
            Status = Tr.F("Import impossible : {0}", e.Message);
            return false;
        }
    }

    private void Reload()
    {
        HasCatalog = _catalog.Exists;
        if (HasCatalog)
        {
            var database = _catalog.Open();
            _titles = new TitleRepository(database);
            _images = new ImageStore(database, _catalog.ImagesDirectory);
            _people = new PeopleRepository(database);
        }

        Refresh();
    }

    private void Refresh()
    {
        Titles.Clear();
        if (_titles is null)
        {
            Status = string.Empty;
            return;
        }

        foreach (var title in _titles.Query(new TitleQuery(SearchText)))
        {
            Titles.Add(title with { PosterPath = title.PosterRelativePath is { } poster ? _images!.ResolveInside(poster) : null });
        }

        Status = Tr.F("{0} titre(s)", Titles.Count);
    }

    private static string? Names(IEnumerable<Core.People.CastMember> credits, string role, string label)
    {
        var names = credits.Where(c => c.Role == role).Select(c => c.Name).Distinct().ToList();
        return names.Count == 0 ? null : $"{label} : {string.Join(", ", names)}";
    }
}

/// <summary>Fiche d'un titre, prête à afficher.</summary>
public sealed record TitlePage(
    string Title,
    string Subtitle,
    string? PosterPath,
    IReadOnlyList<string> Genres,
    string? UserRating,
    string? ImdbRating,
    string? Overview,
    string? Directors,
    string? Cast);
