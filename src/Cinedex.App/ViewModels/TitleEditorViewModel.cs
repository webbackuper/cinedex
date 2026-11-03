using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Cinedex.App.Services;
using Cinedex.Core.People;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

public sealed partial class GenreChoice(Genre genre, bool isSelected) : ObservableObject
{
    public Genre Genre { get; } = genre;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}

/// <summary>
/// Ce que le panneau de droite affiche en plus des champs de la fiche : affiche, distribution, équipe et fichiers.
/// <paramref name="RefreshMedia"/> relit les fichiers avec MediaInfo et renvoie le nouveau texte ;
/// <paramref name="AddFile"/> rattache un fichier de plus (CD2, part2...) et renvoie le nouveau texte, ou lève une erreur lisible.
/// </summary>
public sealed record TitleEditorExtras(
    string? PosterPath = null,
    IReadOnlyList<CastMember>? Cast = null,
    string? CrewText = null,
    IReadOnlyList<CastMember>? Directors = null,
    string? MediaText = null,
    Func<Task<string?>>? RefreshMedia = null,
    Func<string, Task<string?>>? AddFile = null,
    PeopleService? People = null,
    LoanEditorViewModel? Loans = null,
    MediaFileRepository? Files = null);

/// <summary>Un fichier du titre dans l'onglet Fichier : chemin complet et MediaID (nom du disque) modifiable.</summary>
public sealed partial class FileVolumeRow(MediaFileEntry file) : ObservableObject
{
    public MediaFileEntry File { get; } = file;

    public string Header => (File.Part > 1 ? Tr.F("Partie {0} : ", File.Part) : string.Empty) + File.FullPath;

    [ObservableProperty]
    public partial string MediaId { get; set; } = file.VolumeLabel ?? string.Empty;

    public bool IsChanged => !string.Equals(MediaId.Trim(), File.VolumeLabel ?? string.Empty, StringComparison.Ordinal);
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
        IReadOnlyList<CollectionInfo>? collections = null,
        TitleEditorExtras? extras = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        _original = original;
        extras ??= new TitleEditorExtras();
        _refreshMedia = extras.RefreshMedia;
        _addFile = extras.AddFile;
        _people = extras.People;
        Loans = extras.Loans;
        _files = extras.Files;
        ReloadFiles();
        PosterPath = extras.PosterPath;
        Cast = extras.Cast ?? [];
        Directors = extras.Directors ?? [];
        CrewText = extras.CrewText ?? string.Empty;
        MediaText = extras.MediaText ?? "Aucun fichier vidéo rattaché à ce titre.";
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
        TmdbRating = original.TmdbRating?.ToString("0.0", CultureInfo.CurrentCulture) ?? string.Empty;
        ImdbRating = original.ImdbRating?.ToString("0.0", CultureInfo.CurrentCulture) ?? string.Empty;
        ImdbVotes = Format(original.ImdbVotes);
        Tomatometer = Format(original.Tomatometer);
        Metascore = Format(original.Metascore);
        SpokenLanguages = original.SpokenLanguages ?? string.Empty;
        TrailerUrl = original.TrailerUrl ?? string.Empty;
        IsSeen = original.IsSeen;
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

    private readonly Func<Task<string?>>? _refreshMedia;

    private readonly Func<string, Task<string?>>? _addFile;

    private readonly PeopleService? _people;

    private readonly MediaFileRepository? _files;

    /// <summary>Fichiers du titre, avec leur MediaID modifiable (enregistré avec la fiche).</summary>
    public ObservableCollection<FileVolumeRow> Files { get; } = [];

    public bool HasFiles => Files.Count > 0;

    /// <summary>Enregistre les MediaID modifiés ; appelé après un enregistrement réussi de la fiche.</summary>
    public void ApplyFileChanges()
    {
        foreach (var row in Files.Where(r => r.IsChanged))
        {
            _files?.SetVolumeLabel(row.File.Id, row.MediaId);
        }
    }

    private void ReloadFiles()
    {
        // Les MediaID déjà saisis sont conservés quand la liste est relue (partie ajoutée, MediaInfo relu).
        var typed = Files.ToDictionary(r => r.File.Id, r => r.MediaId);
        Files.Clear();
        foreach (var file in _files?.FilesOf(_original.Id) ?? [])
        {
            var row = new FileVolumeRow(file);
            if (typed.TryGetValue(file.Id, out var mediaId))
            {
                row.MediaId = mediaId;
            }

            Files.Add(row);
        }

        OnPropertyChanged(nameof(HasFiles));
    }

    /// <summary>Titre choisi depuis la fiche d'un acteur : la fiche se ferme et le catalogue s'y place.</summary>
    public long? NavigateToTitleId { get; set; }

    public bool CanOpenPerson => _people is not null;

    /// <summary>Fiche d'un acteur de la distribution (null si les acteurs ne sont pas gérés).</summary>
    public PersonViewModel? PersonPage(CastMember member) =>
        _people is null || member is null ? null : new PersonViewModel(_people, member.PersonId, member.Role == "director" ? "director" : null);

    public bool CanAddFile => _addFile is not null;

    /// <summary>Rattache une partie supplémentaire (CD2, part2...) au titre ; le texte de l'onglet est mis à jour.</summary>
    public async Task AddFileAsync(string path)
    {
        if (_addFile is null || string.IsNullOrWhiteSpace(path) || IsRefreshingMedia)
        {
            return;
        }

        IsRefreshingMedia = true;
        try
        {
            MediaText = await _addFile(path) ?? MediaText;
            ReloadFiles();
            LastError = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsRefreshingMedia = false;
        }
    }

    public string WindowTitle => Tr.F("Modifier : {0}", _original.Title);

    public string? PosterPath { get; }

    public IReadOnlyList<CastMember> Cast { get; }

    public bool HasCast => Cast.Count > 0;

    /// <summary>Réalisateurs du titre (onglet Distribution), cliquables comme les acteurs.</summary>
    public IReadOnlyList<CastMember> Directors { get; }

    public bool HasDirectors => Directors.Count > 0;

    public string CrewText { get; }

    /// <summary>Emplacement complet et propriétés techniques de chaque fichier (lecture seule : relu par MediaInfo).</summary>
    [ObservableProperty] public partial string MediaText { get; set; }

    public bool CanRefreshMedia => _refreshMedia is not null;

    /// <summary>Identifiants en lecture seule : TMDb (rempli par la mise à jour) et date d'ajout.</summary>
    public string SourceText => string.Join(" · ", new[]
    {
        _original.TmdbId is { } tmdb ? Tr.F("TMDb {0}", tmdb) : "Pas lié à TMDb",
        Tr.F("ajouté le {0:d}", _original.AddedAt.ToLocalTime()),
    });

    public string SeenText => _original.ViewCount == 0
        ? "Jamais vu"
        : Tr.F("Vu {0} fois", _original.ViewCount) + (_original.LastSeenAt is { } last ? Tr.F(", dernière fois le {0:d}", last.ToLocalTime()) : string.Empty);

    /// <summary>Gestion du prêt dans l'onglet Exemplaire (absente si la fiche est ouverte sans catalogue).</summary>
    public LoanEditorViewModel? Loans { get; }

    public bool HasLoans => Loans is not null;

    /// <summary>Durée saisie en minutes, affichée aussi en heures : « 112 » donne « soit 1 h 52 ».</summary>
    public string RuntimeText =>
        int.TryParse(Runtime?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var minutes) && minutes > 0
            ? Tr.F("soit {0}", TitleText.FormatRuntime(minutes))
            : string.Empty;

    partial void OnRuntimeChanged(string value) => OnPropertyChanged(nameof(RuntimeText));

    [ObservableProperty] public partial bool IsRefreshingMedia { get; set; }

    [RelayCommand]
    private async Task RefreshMediaAsync()
    {
        if (_refreshMedia is null || IsRefreshingMedia)
        {
            return;
        }

        IsRefreshingMedia = true;
        try
        {
            MediaText = await _refreshMedia() ?? MediaText;
            ReloadFiles();
        }
        finally
        {
            IsRefreshingMedia = false;
        }
    }

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

    [ObservableProperty] public partial string TmdbRating { get; set; }

    [ObservableProperty] public partial string ImdbRating { get; set; }

    [ObservableProperty] public partial string ImdbVotes { get; set; }

    [ObservableProperty] public partial string Tomatometer { get; set; }

    [ObservableProperty] public partial string Metascore { get; set; }

    [ObservableProperty] public partial string SpokenLanguages { get; set; }

    [ObservableProperty] public partial string TrailerUrl { get; set; }

    /// <summary>Vu / pas vu ; enregistré à part (historique de visionnage).</summary>
    [ObservableProperty] public partial bool IsSeen { get; set; }

    public bool SeenChanged => IsSeen != _original.IsSeen;

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
            errors.Add(Tr.T("Le titre est obligatoire."));
        }

        var year = ParseInt(Year, "Année", 1870, 2100, errors);
        var runtime = ParseInt(Runtime, "Durée", 1, 10_000, errors);
        var number = ParseInt(Number, "Numéro", 1, int.MaxValue, errors);
        var discs = ParseInt(DiscCount, "Nombre de disques", 1, 500, errors);
        var rating = ParseDecimal(UserRating, "Note personnelle", 0, 10, errors);
        var price = ParseDecimal(Price, "Prix", 0, 1_000_000, errors);
        var tmdbRating = ParseDecimal(TmdbRating, "Note TMDb", 0, 10, errors);
        var imdbRating = ParseDecimal(ImdbRating, "Note IMDb", 0, 10, errors);
        var imdbVotes = ParseInt(ImdbVotes, "Votes IMDb", 0, int.MaxValue, errors);
        var tomatometer = ParseInt(Tomatometer, "Tomatometer", 0, 100, errors);
        var metascore = ParseInt(Metascore, "Metascore", 0, 100, errors);
        var imdb = ImdbId.Trim();
        if (imdb.Length > 0 && !IsImdbId(imdb))
        {
            errors.Add(Tr.T("L'identifiant IMDb doit avoir la forme tt1234567."));
        }

        var link = WebLink.Trim();
        if (link.Length > 0 && !IsWebLink(link))
        {
            errors.Add(Tr.T("Le lien web doit commencer par https:// ou http://."));
        }

        var trailer = TrailerUrl.Trim();
        if (trailer.Length > 0 && !IsWebLink(trailer))
        {
            errors.Add(Tr.T("La bande-annonce doit être un lien https:// ou http://."));
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
            TmdbRating = (double?)tmdbRating,
            ImdbRating = (double?)imdbRating,
            ImdbVotes = imdbVotes,
            Tomatometer = tomatometer,
            Metascore = metascore,
            SpokenLanguages = SpokenLanguages,
            TrailerUrl = trailer,
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

    private static bool IsWebLink(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

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

        errors.Add(Tr.F("{0} : nombre entier attendu entre {1} et {2}.", Tr.T(field), min, max));
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

        errors.Add(Tr.F("{0} : nombre attendu entre {1} et {2}.", Tr.T(field), min, max));
        return null;
    }
}
