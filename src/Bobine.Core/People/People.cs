using Bobine.Core.Titles;

namespace Bobine.Core.People;

/// <summary>Membre de la distribution ou de l'équipe d'un titre.</summary>
public sealed record CastMember(long PersonId, string Name, string Role, string? Character, int Order, string? PhotoRelativePath, string? ProfilePath)
{
    public string? PhotoPath { get; init; }

    public string RoleLabel => Role switch
    {
        "actor" => Character ?? "Acteur",
        "director" => "Réalisation",
        "writer" => "Scénario",
        "composer" => "Musique",
        _ => Role,
    };
}

/// <summary>Fiche d'une personne.</summary>
public sealed record PersonDetail
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public long? TmdbId { get; init; }

    public string? ImdbId { get; init; }

    public string? Biography { get; init; }

    public DateOnly? Birthday { get; init; }

    public DateOnly? Deathday { get; init; }

    public string? PlaceOfBirth { get; init; }

    public string? KnownFor { get; init; }

    public string? ProfilePath { get; init; }

    public string? PhotoRelativePath { get; init; }

    public string? PhotoPath { get; init; }

    public bool HasDetails { get; init; }

    /// <summary>« 21 août 1940 – 13 mai 2014 (73 ans), Paris, France ».</summary>
    public string Dates
    {
        get
        {
            if (Birthday is null)
            {
                return PlaceOfBirth ?? string.Empty;
            }

            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var end = Deathday ?? DateOnly.FromDateTime(DateTime.Today);
            var age = end.Year - Birthday.Value.Year - (end < Birthday.Value.AddYears(end.Year - Birthday.Value.Year) ? 1 : 0);
            var text = Deathday is { } d
                ? $"{Birthday.Value.ToString("D", culture)} – {d.ToString("D", culture)} ({age} ans)"
                : $"Né(e) le {Birthday.Value.ToString("D", culture)} ({age} ans)";
            return PlaceOfBirth is null ? text : $"{text}, {PlaceOfBirth}";
        }
    }
}

/// <summary>Participation d'une personne à un titre présent dans le catalogue.</summary>
public sealed record PersonCredit(long TitleId, string Title, int? Year, TitleKind Kind, string Role, string? Character, string? PosterRelativePath)
{
    public string? PosterPath { get; init; }

    public string Subtitle => string.Join(" · ", new[]
    {
        Year?.ToString(System.Globalization.CultureInfo.CurrentCulture),
        Role switch
        {
            "actor" => Character is null ? "Acteur" : $"rôle : {Character}",
            "director" => "Réalisation",
            "writer" => "Scénario",
            "composer" => "Musique",
            _ => Role,
        },
    }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Résultat de recherche de personnes : nombre de titres du catalogue où elle apparaît.</summary>
public sealed record PersonSummary(long Id, string Name, int TitleCount, string? PhotoRelativePath)
{
    public string? PhotoPath { get; init; }

    public string CountLabel => TitleCount <= 1 ? "1 titre" : $"{TitleCount} titres";
}
