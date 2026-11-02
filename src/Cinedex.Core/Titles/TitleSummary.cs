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

    /// <summary>Ligne secondaire de la liste : « 1995 · Blu-ray · 2 h 50 ».</summary>
    public string Subtitle => string.Join(" · ", new[]
    {
        Year?.ToString(System.Globalization.CultureInfo.CurrentCulture),
        Kind == TitleKind.Series ? Localization.Tr.T("Série") : null,
        Medium,
        TitleText.FormatRuntime(RuntimeMinutes),
    }.Where(p => !string.IsNullOrEmpty(p)));

    public bool IsLoaned => Borrower is not null;
}

/// <summary>Données minimales pour créer une fiche à la main.</summary>
public sealed record NewTitle(TitleKind Kind, string Title, int? Year = null, string? OriginalTitle = null);
