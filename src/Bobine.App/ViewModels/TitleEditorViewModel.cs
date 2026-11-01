using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Bobine.Core.Titles;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bobine.App.ViewModels;

public sealed partial class GenreChoice(Genre genre, bool isSelected) : ObservableObject
{
    public Genre Genre { get; } = genre;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}

/// <summary>
/// Copie éditable d'une fiche. Les nombres sont saisis en texte et validés à l'enregistrement,
/// pour pouvoir signaler une saisie invalide sans perdre le reste du formulaire.
/// </summary>
public sealed partial class TitleEditorViewModel : ObservableObject
{
    private readonly TitleDetail _original;

    public TitleEditorViewModel(
        TitleDetail original,
        IReadOnlyList<Genre> genres,
        IReadOnlyList<RefItem> media,
        IReadOnlyList<RefItem> cases,
        IReadOnlyList<RefItem> versions,
        IReadOnlyList<CollectionInfo>? collections = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        _original = original;
        Collections = [new(null, "(aucune)"), .. (collections ?? []).Select(c => new Choice<long?>(c.Id, c.Name))];
        SelectedCollection = Collections.FirstOrDefault(c => c.Value == original.CollectionId) ?? Collections[0];
        TagsText = string.Join(", ", original.Tags);
        GroupsText = string.Join(", ", original.Groups);
        Kinds = [new(TitleKind.Movie, "Film"), new(TitleKind.Series, "Série")];
        Media = WithNone(media);
        Cases = WithNone(cases);
        Versions = WithNone(versions);
        Genres = new ObservableCollection<GenreChoice>(genres.Select(g => new GenreChoice(g, original.GenreCodes.Contains(g.Code))));

        SelectedKind = Kinds.First(k => k.Value == original.Kind);
        Title = original.Title;
        OriginalTitle = original.OriginalTitle ?? string.Empty;
        AlternativeTitle = original.AlternativeTitle ?? string.Empty;
        Year = Format(original.Year);
        Runtime = Format(original.RuntimeMinutes);
        Tagline = original.Tagline ?? string.Empty;
        Overview = original.Overview ?? string.Empty;
        Certification = original.Certification ?? string.Empty;
        Countries = original.Countries ?? string.Empty;
        Studio = original.Studio ?? string.Empty;
        ImdbId = original.ImdbId ?? string.Empty;
        WebLink = original.WebLink ?? string.Empty;
        UserRating = original.UserRating?.ToString("0.#", CultureInfo.CurrentCulture) ?? string.Empty;
        Comments = original.Comments ?? string.Empty;
        IsOwned = original.IsOwned;
        IsFavorite = original.IsFavorite;
        IsShortlisted = original.IsShortlisted;
        IsLocked = original.IsLocked;
        IsAdult = original.IsAdult;

        var copy = original.Copy;
        Number = Format(copy?.Number);
        CustomNumber = copy?.CustomNumber ?? string.Empty;
        SelectedMedium = Media.First(m => m.Value == copy?.MediumCode);
        SelectedCase = Cases.First(m => m.Value == copy?.CaseCode);
        SelectedVersion = Versions.First(m => m.Value == copy?.VersionCode);
        Location = copy?.Location ?? string.Empty;
        DiscCount = Format(copy?.DiscCount);
        Ean = copy?.Ean ?? string.Empty;
        Price = copy?.Price?.ToString("0.00", CultureInfo.CurrentCulture) ?? string.Empty;
        Is3D = copy?.Is3D ?? false;
    }

    public string WindowTitle => $"Modifier : {_original.Title}";

    public IReadOnlyList<Choice<TitleKind>> Kinds { get; }

    public IReadOnlyList<Choice<string?>> Media { get; }

    public IReadOnlyList<Choice<string?>> Cases { get; }

    public IReadOnlyList<Choice<string?>> Versions { get; }

    public ObservableCollection<GenreChoice> Genres { get; }

    public IReadOnlyList<Choice<long?>> Collections { get; }

    [ObservableProperty] public partial Choice<long?> SelectedCollection { get; set; }

    [ObservableProperty] public partial string TagsText { get; set; }

    [ObservableProperty] public partial string GroupsText { get; set; }

    [ObservableProperty] public partial Choice<TitleKind> SelectedKind { get; set; }

    [ObservableProperty] public partial string Title { get; set; }

    [ObservableProperty] public partial string OriginalTitle { get; set; }

    [ObservableProperty] public partial string AlternativeTitle { get; set; }

    [ObservableProperty] public partial string Year { get; set; }

    [ObservableProperty] public partial string Runtime { get; set; }

    [ObservableProperty] public partial string Tagline { get; set; }

    [ObservableProperty] public partial string Overview { get; set; }

    [ObservableProperty] public partial string Certification { get; set; }

    [ObservableProperty] public partial string Countries { get; set; }

    [ObservableProperty] public partial string Studio { get; set; }

    [ObservableProperty] public partial string ImdbId { get; set; }

    [ObservableProperty] public partial string WebLink { get; set; }

    [ObservableProperty] public partial string UserRating { get; set; }

    [ObservableProperty] public partial string Comments { get; set; }

    [ObservableProperty] public partial bool IsOwned { get; set; }

    [ObservableProperty] public partial bool IsFavorite { get; set; }

    [ObservableProperty] public partial bool IsShortlisted { get; set; }

    [ObservableProperty] public partial bool IsLocked { get; set; }

    [ObservableProperty] public partial bool IsAdult { get; set; }

    [ObservableProperty] public partial string Number { get; set; }

    [ObservableProperty] public partial string CustomNumber { get; set; }

    [ObservableProperty] public partial Choice<string?> SelectedMedium { get; set; }

    [ObservableProperty] public partial Choice<string?> SelectedCase { get; set; }

    [ObservableProperty] public partial Choice<string?> SelectedVersion { get; set; }

    [ObservableProperty] public partial string Location { get; set; }

    [ObservableProperty] public partial string DiscCount { get; set; }

    [ObservableProperty] public partial string Ean { get; set; }

    [ObservableProperty] public partial string Price { get; set; }

    [ObservableProperty] public partial bool Is3D { get; set; }

    [ObservableProperty] public partial string? LastError { get; set; }

    /// <summary>Valide la saisie et produit la fiche à enregistrer ; sinon renseigne <see cref="LastError"/>.</summary>
    public bool TryBuild([NotNullWhen(true)] out TitleDetail? result)
    {
        result = null;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Title))
        {
            errors.Add("Le titre est obligatoire.");
        }

        var year = ParseInt(Year, "Année", 1870, 2100, errors);
        var runtime = ParseInt(Runtime, "Durée", 1, 10_000, errors);
        var number = ParseInt(Number, "Numéro", 1, int.MaxValue, errors);
        var discs = ParseInt(DiscCount, "Nombre de disques", 1, 500, errors);
        var rating = ParseDecimal(UserRating, "Note personnelle", 0, 10, errors);
        var price = ParseDecimal(Price, "Prix", 0, 1_000_000, errors);
        var imdb = ImdbId.Trim();
        if (imdb.Length > 0 && !IsImdbId(imdb))
        {
            errors.Add("L'identifiant IMDb doit avoir la forme tt1234567.");
        }

        var link = WebLink.Trim();
        if (link.Length > 0 && !(Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"))
        {
            errors.Add("Le lien web doit commencer par https:// ou http://.");
        }

        if (errors.Count > 0)
        {
            LastError = string.Join(' ', errors);
            return false;
        }

        LastError = null;
        result = _original with
        {
            Kind = SelectedKind.Value,
            Title = Title.Trim(),
            OriginalTitle = OriginalTitle,
            AlternativeTitle = AlternativeTitle,
            Year = year,
            RuntimeMinutes = runtime,
            Tagline = Tagline,
            Overview = Overview,
            Certification = Certification,
            Countries = Countries,
            Studio = Studio,
            ImdbId = imdb,
            WebLink = link,
            UserRating = rating is null ? null : (double)Math.Round(rating.Value * 2, MidpointRounding.AwayFromZero) / 2,
            Comments = Comments,
            IsOwned = IsOwned,
            IsFavorite = IsFavorite,
            IsShortlisted = IsShortlisted,
            IsLocked = IsLocked,
            IsAdult = IsAdult,
            GenreCodes = Genres.Where(g => g.IsSelected).Select(g => g.Genre.Code).ToList(),
            Tags = SplitList(TagsText),
            Groups = SplitList(GroupsText),
            CollectionId = SelectedCollection.Value,
            Copy = (_original.Copy ?? new CopyInfo()) with
            {
                Number = number,
                CustomNumber = CustomNumber,
                MediumCode = SelectedMedium.Value,
                CaseCode = SelectedCase.Value,
                VersionCode = SelectedVersion.Value,
                Location = Location,
                DiscCount = discs,
                Ean = Ean,
                Price = price,
                Is3D = Is3D,
            },
        };
        return true;
    }

    /// <summary>« SF, huis clos ; culte » donne trois libellés, sans doublon ni vide.</summary>
    internal static IReadOnlyList<string> SplitList(string text) =>
        (text ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static bool IsImdbId(string value) =>
        value.Length is >= 9 and <= 12 && value.StartsWith("tt", StringComparison.Ordinal) && value[2..].All(char.IsAsciiDigit);

    private static IReadOnlyList<Choice<string?>> WithNone(IReadOnlyList<RefItem> items) =>
        [new(null, "(non précisé)"), .. items.Select(i => new Choice<string?>(i.Code, i.Label))];

    private static string Format(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    private static int? ParseInt(string text, string field, int min, int max, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors.Add($"{field} : nombre entier attendu entre {min} et {max}.");
        return null;
    }

    private static decimal? ParseDecimal(string text, string field, decimal min, decimal max, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Accepte la virgule comme le point, quelle que soit la langue du système.
        var normalized = text.Trim().Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors.Add($"{field} : nombre attendu entre {min} et {max}.");
        return null;
    }
}
