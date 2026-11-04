namespace Cinedex.Core.Titles;

public enum TitleKind
{
    Movie,
    Series,
}

/// <summary>Ligne affichée dans la liste principale.</summary>
public sealed record TitleSummary(
    long Id,
    TitleKind Kind,
    string Title,
    int? Year,
    int? RuntimeMinutes,
    double? UserRating,
    bool IsOwned,
    bool IsFavorite,
    bool IsSeen,
    int? Number = null,
    string? Medium = null,
    string? Borrower = null,
    string? PosterRelativePath = null)
{
    /// <summary>Chemin absolu de l'affiche, renseigné par l'interface à partir du dossier d'images du catalogue.</summary>
    public string? PosterPath { get; init; }

    /// <summary>Colonnes facultatives de la vue liste, lues avec le reste de la ligne (aucune requête de plus).</summary>
    public string? OriginalTitle { get; init; }

    public string? Studio { get; init; }

    public string? Certification { get; init; }

    /// <summary>Nom du ou des disques qui portent les fichiers du titre (MediaID).</summary>
    public string? MediaId { get; init; }

    public string? CollectionName { get; init; }

    /// <summary>Date d'ajout au catalogue, au format AAAA-MM-JJ.</summary>
    public string? AddedOn { get; init; }

    /// <summary>Libellés des genres, séparés par des virgules (infobulle de la liste).</summary>
    public string? Genres { get; init; }

    public double? ImdbRating { get; init; }

    /// <summary>Début du synopsis (infobulle de la liste).</summary>
    public string? Overview { get; init; }

    /// <summary>Infobulle : « Policier, Thriller » dans la langue de l'interface.</summary>
    public string GenresText => string.Join(", ", (Genres ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Localization.Tr.T));

    public bool HasGenres => !string.IsNullOrWhiteSpace(Genres);

    public string ImdbText => ImdbRating is { } r ? "IMDb " + r.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) : string.Empty;

    public bool HasImdb => ImdbRating is not null;

    public bool HasOverview => !string.IsNullOrWhiteSpace(Overview);

    public string TitleWithYear => Year is { } y ? $"{Title} ({y})" : Title;

    /// <summary>Ligne secondaire de la liste : « 1995 · Blu-ray · 2 h 50 ».</summary>
    public string Subtitle => string.Join(" · ", new[]
    {
        Year?.ToString(System.Globalization.CultureInfo.CurrentCulture),
        Kind == TitleKind.Series ? Localization.Tr.T("Série") : null,
        Medium,
        TitleText.FormatRuntime(RuntimeMinutes),
    }.Where(p => !string.IsNullOrEmpty(p)));

    public bool IsLoaned => Borrower is not null;

    /// <summary>Ligne secondaire composée selon les colonnes choisies (réglages) ; sinon <see cref="Subtitle"/>.</summary>
    public string? CustomSubtitle { get; init; }

    public string DisplaySubtitle => CustomSubtitle ?? Subtitle;
}

/// <summary>Données minimales pour créer une fiche à la main.</summary>
public sealed record NewTitle(TitleKind Kind, string Title, int? Year = null, string? OriginalTitle = null);
