namespace Bobine.Core.Titles;

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
    bool IsSeen);

/// <summary>Données minimales pour créer une fiche à la main.</summary>
public sealed record NewTitle(TitleKind Kind, string Title, int? Year = null, string? OriginalTitle = null);
