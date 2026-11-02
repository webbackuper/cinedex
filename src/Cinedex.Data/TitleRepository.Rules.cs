using Cinedex.Core.Titles;
using Dapper;
using Cinedex.Core.Localization;

namespace Cinedex.Data;

/// <summary>
/// Traduction de la recherche multicritère en SQL. Seuls les champs de <see cref="SearchFields"/> sont acceptés :
/// chacun correspond à une expression SQL écrite ici, et toutes les valeurs saisies passent en paramètres.
/// </summary>
public sealed partial class TitleRepository
{
    // Champ simple : une colonne (ou un calcul) par titre.
    // Champ multiple : plusieurs valeurs possibles par titre (acteurs, fichiers...), testées avec EXISTS.
    private sealed record FieldSql(string Expression, string? Source = null);

    private const string FileSource = "FROM media_file m JOIN copy cm ON cm.id = m.copy_id WHERE cm.title_id = t.id";

    private static readonly Dictionary<string, FieldSql> RuleFields = new(StringComparer.Ordinal)
    {
        ["title"] = new("t.title"),
        ["original_title"] = new("t.original_title"),
        ["alternative_title"] = new("t.alternative_title"),
        ["year"] = new("t.year"),
        ["runtime"] = new("t.runtime_minutes"),
        ["genre"] = new("g.code", "FROM title_genre tg JOIN genre g ON g.id = tg.genre_id WHERE tg.title_id = t.id"),
        ["overview"] = new("t.overview"),
        ["tagline"] = new("t.tagline"),
        ["countries"] = new("t.countries"),
        ["languages"] = new("t.spoken_languages"),
        ["studio"] = new("t.studio"),
        ["certification"] = new("t.certification"),
        ["imdb_id"] = new("t.imdb_id"),
        ["comments"] = new("t.comments"),

        ["user_rating"] = new("t.user_rating"),
        ["tmdb_rating"] = new("t.tmdb_rating"),
        ["imdb_rating"] = new("t.imdb_rating"),
        ["imdb_votes"] = new("t.imdb_votes"),
        ["tomatometer"] = new("t.tomatometer"),
        ["metascore"] = new("t.metascore"),

        ["actor"] = new("p.name", "FROM credit cr JOIN person p ON p.id = cr.person_id WHERE cr.title_id = t.id AND cr.role = 'actor'"),
        ["character"] = new("cr.character", "FROM credit cr WHERE cr.title_id = t.id AND cr.role = 'actor'"),
        ["director"] = new("p.name", "FROM credit cr JOIN person p ON p.id = cr.person_id WHERE cr.title_id = t.id AND cr.role = 'director'"),
        ["writer"] = new("p.name", "FROM credit cr JOIN person p ON p.id = cr.person_id WHERE cr.title_id = t.id AND cr.role = 'writer'"),
        ["composer"] = new("p.name", "FROM credit cr JOIN person p ON p.id = cr.person_id WHERE cr.title_id = t.id AND cr.role = 'composer'"),
        ["person"] = new("p.name", "FROM credit cr JOIN person p ON p.id = cr.person_id WHERE cr.title_id = t.id"),

        ["media_id"] = new("m.volume_label", FileSource),
        ["file_path"] = new("coalesce(rt.path || '/', '') || m.path", "FROM media_file m JOIN copy cm ON cm.id = m.copy_id LEFT JOIN scan_root rt ON rt.id = m.scan_root_id WHERE cm.title_id = t.id"),
        ["container"] = new("m.container", FileSource),
        ["video_codec"] = new("m.video_codec", FileSource),
        ["video_height"] = new("m.height", FileSource),
        ["audio"] = new("coalesce(a.channels, '') || ' ' || coalesce(a.language, '')", "FROM audio_track a JOIN media_file m ON m.id = a.media_file_id JOIN copy cm ON cm.id = m.copy_id WHERE cm.title_id = t.id"),
        ["subtitles"] = new("st.language", "FROM subtitle_track st JOIN media_file m ON m.id = st.media_file_id JOIN copy cm ON cm.id = m.copy_id WHERE cm.title_id = t.id"),
        ["parts"] = new("(SELECT count(*) FROM media_file m JOIN copy cm ON cm.id = m.copy_id WHERE cm.title_id = t.id)"),

        ["medium"] = new("rm.label", "FROM copy cx JOIN ref_list rm ON rm.id = cx.medium_id WHERE cx.title_id = t.id"),
        ["location"] = new("cx.location", "FROM copy cx WHERE cx.title_id = t.id"),
        ["number"] = new("cx.number", "FROM copy cx WHERE cx.title_id = t.id"),
        ["borrower"] = new("l.borrower", "FROM loan l JOIN copy cx ON cx.id = l.copy_id WHERE cx.title_id = t.id AND l.returned_at IS NULL"),

        ["collection"] = new("co.name", "FROM collection co WHERE co.id = t.collection_id"),
        ["tag"] = new("tg2.name", "FROM title_tag tt JOIN tag tg2 ON tg2.id = tt.tag_id WHERE tt.title_id = t.id"),
        ["group"] = new("ug.name", "FROM title_group tgr JOIN user_group ug ON ug.id = tgr.group_id WHERE tgr.title_id = t.id"),

        ["seen"] = new("EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id)"),
        ["views"] = new("(SELECT count(*) FROM viewing v WHERE v.title_id = t.id)"),
        ["favorite"] = new("t.is_favorite = 1"),
        ["owned"] = new("t.is_owned = 1"),
        ["loaned"] = new("EXISTS (SELECT 1 FROM loan l JOIN copy cx ON cx.id = l.copy_id WHERE cx.title_id = t.id AND l.returned_at IS NULL)"),
        ["is_3d"] = new("EXISTS (SELECT 1 FROM copy cx WHERE cx.title_id = t.id AND cx.is_3d = 1)"),
        ["series"] = new("t.kind = 'series'"),
    };

    /// <summary>Clause WHERE de la recherche multicritère ; null si aucune condition n'est exploitable.</summary>
    private static string? RuleClause(RuleSearch search, DynamicParameters parameters)
    {
        var clauses = new List<string>();
        foreach (var (rule, index) in search.Rules.Select((r, i) => (r, i)))
        {
            if (SearchFields.Find(rule.Field) is { } field && RuleFields.TryGetValue(rule.Field, out var sql) &&
                Condition(field, sql, rule, "@R" + index, parameters) is { } clause)
            {
                clauses.Add("(" + clause + ")");
            }
        }

        return clauses.Count == 0 ? null : "(" + string.Join(search.MatchAll ? " AND " : " OR ", clauses) + ")";
    }

    private static string? Condition(SearchField field, FieldSql sql, SearchRule rule, string name, DynamicParameters parameters)
    {
        var e = sql.Expression;
        if (field.Kind == FieldKind.Flag)
        {
            return rule.Operator == RuleOperator.IsFalse ? Tr.F("NOT ({0})", e) : e;
        }

        object? value = null;
        if (rule.NeedsValue)
        {
            if (field.Kind == FieldKind.Number)
            {
                if (rule.Number is not { } number)
                {
                    return null;
                }

                value = number;
            }
            else if (string.IsNullOrWhiteSpace(rule.Value))
            {
                return null;
            }
            else
            {
                value = rule.Operator is RuleOperator.Contains or RuleOperator.NotContains ? Like(rule.Value) : rule.Value.Trim();
            }

            parameters.Add(name[1..], value);
        }

        var text = field.Kind != FieldKind.Number;
        var notEmpty = text ? Tr.F("({0} IS NOT NULL AND {1} <> '')", e, e) : Tr.F("{0} IS NOT NULL", e);

        // Test « positif » sur une valeur ; les négations s'expriment autour (NOT EXISTS pour un champ multiple).
        var positive = rule.Operator switch
        {
            RuleOperator.Contains or RuleOperator.NotContains => Tr.F("{0} LIKE {1} ESCAPE '!'", e, name),
            RuleOperator.Equals or RuleOperator.NotEquals => text ? Tr.F("{0} = {1} COLLATE NOCASE", e, name) : $"{e} = {name}",
            RuleOperator.Greater => $"{e} > {name}",
            RuleOperator.GreaterOrEqual => $"{e} >= {name}",
            RuleOperator.Less => $"{e} < {name}",
            RuleOperator.LessOrEqual => $"{e} <= {name}",
            _ => notEmpty,
        };
        var negated = rule.Operator is RuleOperator.NotContains or RuleOperator.NotEquals or RuleOperator.IsEmpty;

        if (sql.Source is { } source)
        {
            var exists = $"EXISTS (SELECT 1 {source} AND {positive})";
            return negated ? "NOT " + exists : exists;
        }

        return negated ? Tr.F("NOT coalesce({0}, 0)", positive) : positive;
    }
}
