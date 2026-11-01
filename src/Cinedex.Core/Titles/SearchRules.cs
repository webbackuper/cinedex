using System.Globalization;

namespace Cinedex.Core.Titles;

/// <summary>Nature d'un champ de recherche : elle fixe les opérateurs proposés et la saisie de la valeur.</summary>
public enum FieldKind
{
    Text,
    Number,
    Genre,
    Flag,
}

public enum RuleOperator
{
    Contains,
    NotContains,
    Equals,
    NotEquals,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    IsEmpty,
    IsNotEmpty,
    IsTrue,
    IsFalse,
}

public sealed record SearchField(string Key, string Label, FieldKind Kind, string Group);

/// <summary>Une condition : champ, opérateur, valeur (texte saisi, nombre, ou code de genre).</summary>
public sealed record SearchRule(string Field, RuleOperator Operator, string? Value = null)
{
    public bool NeedsValue => SearchFields.NeedsValue(Operator);

    /// <summary>Valeur numérique, virgule ou point acceptés ; null si la saisie n'est pas un nombre.</summary>
    public double? Number =>
        double.TryParse((Value ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>Recherche multicritère : toutes les conditions (ET) ou au moins une (OU).</summary>
public sealed record RuleSearch(IReadOnlyList<SearchRule> Rules, bool MatchAll = true)
{
    public bool IsEmpty => Rules.Count == 0;
}

/// <summary>Champs interrogeables, regroupés comme dans la fiche.</summary>
public static class SearchFields
{
    public static IReadOnlyList<SearchField> All { get; } =
    [
        new("title", "Titre", FieldKind.Text, "Fiche"),
        new("original_title", "Titre original", FieldKind.Text, "Fiche"),
        new("alternative_title", "Autre titre", FieldKind.Text, "Fiche"),
        new("year", "Année", FieldKind.Number, "Fiche"),
        new("runtime", "Durée (minutes)", FieldKind.Number, "Fiche"),
        new("genre", "Genre", FieldKind.Genre, "Fiche"),
        new("overview", "Synopsis", FieldKind.Text, "Fiche"),
        new("tagline", "Accroche", FieldKind.Text, "Fiche"),
        new("countries", "Pays", FieldKind.Text, "Fiche"),
        new("languages", "Langues parlées", FieldKind.Text, "Fiche"),
        new("studio", "Studio", FieldKind.Text, "Fiche"),
        new("certification", "Certification", FieldKind.Text, "Fiche"),
        new("imdb_id", "Identifiant IMDb", FieldKind.Text, "Fiche"),
        new("comments", "Commentaires", FieldKind.Text, "Fiche"),

        new("user_rating", "Ma note", FieldKind.Number, "Notes"),
        new("tmdb_rating", "Note TMDb", FieldKind.Number, "Notes"),
        new("imdb_rating", "Note IMDb", FieldKind.Number, "Notes"),
        new("imdb_votes", "Votes IMDb", FieldKind.Number, "Notes"),
        new("tomatometer", "Rotten Tomatoes (%)", FieldKind.Number, "Notes"),
        new("metascore", "Metascore", FieldKind.Number, "Notes"),

        new("actor", "Acteur", FieldKind.Text, "Distribution"),
        new("character", "Personnage", FieldKind.Text, "Distribution"),
        new("director", "Réalisateur", FieldKind.Text, "Distribution"),
        new("writer", "Scénariste", FieldKind.Text, "Distribution"),
        new("composer", "Compositeur", FieldKind.Text, "Distribution"),
        new("person", "Personne (tout rôle)", FieldKind.Text, "Distribution"),

        new("media_id", "MediaID (nom du disque)", FieldKind.Text, "Fichier"),
        new("file_path", "Chemin du fichier", FieldKind.Text, "Fichier"),
        new("container", "Conteneur", FieldKind.Text, "Fichier"),
        new("video_codec", "Codec vidéo", FieldKind.Text, "Fichier"),
        new("video_height", "Hauteur de l'image (pixels)", FieldKind.Number, "Fichier"),
        new("audio", "Piste audio (codec, langue)", FieldKind.Text, "Fichier"),
        new("subtitles", "Sous-titres (langue)", FieldKind.Text, "Fichier"),
        new("parts", "Nombre de fichiers", FieldKind.Number, "Fichier"),

        new("medium", "Support", FieldKind.Text, "Exemplaire"),
        new("location", "Emplacement", FieldKind.Text, "Exemplaire"),
        new("number", "Numéro", FieldKind.Number, "Exemplaire"),
        new("borrower", "Emprunteur (prêt en cours)", FieldKind.Text, "Exemplaire"),

        new("collection", "Collection", FieldKind.Text, "Classement"),
        new("tag", "Tag", FieldKind.Text, "Classement"),
        new("group", "Groupe", FieldKind.Text, "Classement"),

        new("seen", "Vu", FieldKind.Flag, "Statut"),
        new("views", "Nombre de visionnages", FieldKind.Number, "Statut"),
        new("favorite", "Favori", FieldKind.Flag, "Statut"),
        new("owned", "Possédé", FieldKind.Flag, "Statut"),
        new("loaned", "Prêté", FieldKind.Flag, "Statut"),
        new("is_3d", "3D", FieldKind.Flag, "Statut"),
        new("series", "Série", FieldKind.Flag, "Statut"),
    ];

    public static SearchField? Find(string key) => All.FirstOrDefault(f => f.Key == key);

    public static IReadOnlyList<RuleOperator> OperatorsFor(FieldKind kind) => kind switch
    {
        FieldKind.Number =>
        [
            RuleOperator.Equals, RuleOperator.NotEquals, RuleOperator.Greater, RuleOperator.GreaterOrEqual,
            RuleOperator.Less, RuleOperator.LessOrEqual, RuleOperator.IsEmpty, RuleOperator.IsNotEmpty,
        ],
        FieldKind.Genre => [RuleOperator.Equals, RuleOperator.NotEquals, RuleOperator.IsEmpty],
        FieldKind.Flag => [RuleOperator.IsTrue, RuleOperator.IsFalse],
        _ =>
        [
            RuleOperator.Contains, RuleOperator.NotContains, RuleOperator.Equals, RuleOperator.NotEquals,
            RuleOperator.IsEmpty, RuleOperator.IsNotEmpty,
        ],
    };

    public static bool NeedsValue(RuleOperator op) => op is not (RuleOperator.IsEmpty or RuleOperator.IsNotEmpty or RuleOperator.IsTrue or RuleOperator.IsFalse);

    public static string Label(RuleOperator op, FieldKind kind) => op switch
    {
        RuleOperator.Contains => "contient",
        RuleOperator.NotContains => "ne contient pas",
        RuleOperator.Equals => kind == FieldKind.Number ? "=" : "est",
        RuleOperator.NotEquals => kind == FieldKind.Number ? "≠" : "n'est pas",
        RuleOperator.Greater => ">",
        RuleOperator.GreaterOrEqual => "≥",
        RuleOperator.Less => "<",
        RuleOperator.LessOrEqual => "≤",
        RuleOperator.IsEmpty => kind == FieldKind.Genre ? "aucun genre" : "est vide",
        RuleOperator.IsNotEmpty => "est renseigné",
        RuleOperator.IsTrue => "oui",
        _ => "non",
    };
}
