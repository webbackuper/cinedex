using System.Collections.ObjectModel;
using System.Globalization;
using Bobine.Core.Metadata;
using Bobine.Core.Titles;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bobine.App.ViewModels;

/// <summary>Recherche par champ : personne, personnage, années, notes, genres (tous / un / aucun), tag, groupe, collection, emplacement, emprunteur.</summary>
public sealed partial class AdvancedSearchViewModel : ObservableObject
{
    public AdvancedSearchViewModel(
        SearchCriteria? current,
        IReadOnlyList<Genre> genres,
        IReadOnlyList<LabelInfo> tags,
        IReadOnlyList<LabelInfo> groups,
        IReadOnlyList<CollectionInfo> collections)
    {
        var c = current ?? new SearchCriteria();
        Roles = [new(null, "Tous les rôles"), new(CreditRole.Actor, "Acteur"), new(CreditRole.Director, "Réalisateur"), new(CreditRole.Writer, "Scénariste"), new(CreditRole.Composer, "Compositeur")];
        GenreModes = [new(GenreMatch.Any, "au moins un des genres cochés"), new(GenreMatch.All, "tous les genres cochés"), new(GenreMatch.None, "aucun des genres cochés")];
        Tags = ["", .. tags.Select(t => t.Name)];
        Groups = ["", .. groups.Select(g => g.Name)];
        Collections = [new(null, "(toutes)"), .. collections.Select(x => new Choice<long?>(x.Id, $"{x.Name} ({x.Count})"))];
        Genres = new ObservableCollection<GenreChoice>(genres.Select(g => new GenreChoice(g, c.GenreCodes.Contains(g.Code))));

        PersonName = c.PersonName ?? string.Empty;
        SelectedRole = Roles.First(r => r.Value == c.PersonRole);
        Character = c.Character ?? string.Empty;
        YearFrom = c.YearFrom?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        YearTo = c.YearTo?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        UserRatingMin = c.UserRatingMin?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        SourceRatingMin = c.SourceRatingMin?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        SelectedGenreMode = GenreModes.First(m => m.Value == c.GenreMatch);
        SelectedTag = c.Tag ?? string.Empty;
        SelectedGroup = c.Group ?? string.Empty;
        SelectedCollection = Collections.FirstOrDefault(x => x.Value == c.CollectionId) ?? Collections[0];
        Location = c.Location ?? string.Empty;
        Borrower = c.Borrower ?? string.Empty;
    }

    public IReadOnlyList<Choice<CreditRole?>> Roles { get; }

    public IReadOnlyList<Choice<GenreMatch>> GenreModes { get; }

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<string> Groups { get; }

    public IReadOnlyList<Choice<long?>> Collections { get; }

    public ObservableCollection<GenreChoice> Genres { get; }

    [ObservableProperty] public partial string PersonName { get; set; }

    [ObservableProperty] public partial Choice<CreditRole?> SelectedRole { get; set; }

    [ObservableProperty] public partial string Character { get; set; }

    [ObservableProperty] public partial string YearFrom { get; set; }

    [ObservableProperty] public partial string YearTo { get; set; }

    [ObservableProperty] public partial string UserRatingMin { get; set; }

    [ObservableProperty] public partial string SourceRatingMin { get; set; }

    [ObservableProperty] public partial Choice<GenreMatch> SelectedGenreMode { get; set; }

    [ObservableProperty] public partial string SelectedTag { get; set; }

    [ObservableProperty] public partial string SelectedGroup { get; set; }

    [ObservableProperty] public partial Choice<long?> SelectedCollection { get; set; }

    [ObservableProperty] public partial string Location { get; set; }

    [ObservableProperty] public partial string Borrower { get; set; }

    /// <summary>Critères saisis ; les nombres illisibles sont ignorés plutôt que bloquants.</summary>
    public SearchCriteria Build() => new()
    {
        PersonName = Blank(PersonName),
        PersonRole = SelectedRole.Value,
        Character = Blank(Character),
        YearFrom = ParseInt(YearFrom),
        YearTo = ParseInt(YearTo),
        UserRatingMin = ParseDouble(UserRatingMin),
        SourceRatingMin = ParseDouble(SourceRatingMin),
        GenreCodes = Genres.Where(g => g.IsSelected).Select(g => g.Genre.Code).ToList(),
        GenreMatch = SelectedGenreMode.Value,
        Tag = Blank(SelectedTag),
        Group = Blank(SelectedGroup),
        CollectionId = SelectedCollection.Value,
        Location = Blank(Location),
        Borrower = Blank(Borrower),
    };

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static int? ParseInt(string text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ? value : null;

    private static double? ParseDouble(string text) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}
