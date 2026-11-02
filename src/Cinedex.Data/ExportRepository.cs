using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Cinedex.Core.Localization;
using Dapper;

namespace Cinedex.Data;

/// <summary>Format d'export de la liste des films.</summary>
public enum ExportFormat
{
    Csv,
    Tsv,
    Xlsx,
}

/// <summary>Une colonne exportable : clé stable (réglages), libellé français (traduit à l'affichage), expression SQL.</summary>
public sealed record ExportColumn(string Key, string Label, string Sql, bool IsNumber = false, bool IsDefault = true);

/// <summary>
/// Export de la liste des films (CSV, TSV, Excel) avec choix des colonnes. Les lignes suivent l'ordre des titres
/// donnés (liste affichée ou catalogue entier, dans l'ordre de tri choisi).
/// </summary>
public sealed class ExportRepository(CatalogDatabase database)
{
    // Valeurs produites par les requêtes (en français) et traduites à l'export.
    private static readonly HashSet<string> TranslatedValues = new(StringComparer.Ordinal) { "Oui", "Non", "Film", "Série" };

    private const string MainCopy = "(SELECT min(c2.id) FROM copy c2 WHERE c2.title_id = t.id)";
    private const string Files = "media_file mf JOIN copy cf ON cf.id = mf.copy_id LEFT JOIN scan_root sr ON sr.id = mf.scan_root_id WHERE cf.title_id = t.id";

    public static IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("number", "Numéro", $"(SELECT c.number FROM copy c WHERE c.id = {MainCopy})", IsNumber: true),
        new("title", "Titre", "t.title"),
        new("original_title", "Titre original", "t.original_title"),
        new("year", "Année", "t.year", IsNumber: true),
        new("kind", "Type", "CASE t.kind WHEN 'series' THEN 'Série' ELSE 'Film' END", IsDefault: false),
        new("genres", "Genres", "(SELECT group_concat(g.label, ', ') FROM title_genre tg JOIN genre g ON g.id = tg.genre_id WHERE tg.title_id = t.id)"),
        new("runtime", "Durée (min)", "t.runtime_minutes", IsNumber: true),
        new("directors", "Réalisateurs", "(SELECT group_concat(p.name, ', ') FROM credit cr JOIN person p ON p.id = cr.person_id WHERE cr.title_id = t.id AND cr.role = 'director')"),
        new("actors", "Acteurs principaux", "(SELECT group_concat(name, ', ') FROM (SELECT p.name FROM credit cr JOIN person p ON p.id = cr.person_id WHERE cr.title_id = t.id AND cr.role = 'actor' ORDER BY cr.position LIMIT 5))"),
        new("imdb_rating", "Note IMDb", "t.imdb_rating", IsNumber: true),
        new("user_rating", "Ma note", "t.user_rating", IsNumber: true),
        new("seen", "Vu", "CASE WHEN EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id) THEN 'Oui' ELSE 'Non' END"),
        new("favorite", "Favori", "CASE t.is_favorite WHEN 1 THEN 'Oui' ELSE 'Non' END", IsDefault: false),
        new("owned", "Possédé", "CASE t.is_owned WHEN 1 THEN 'Oui' ELSE 'Non' END", IsDefault: false),
        new("medium", "Support", $"(SELECT r.label FROM copy c JOIN ref_list r ON r.id = c.medium_id WHERE c.id = {MainCopy})"),
        new("location", "Emplacement", $"(SELECT c.location FROM copy c WHERE c.id = {MainCopy})", IsDefault: false),
        new("collection", "Collection", "(SELECT co.name FROM collection co WHERE co.id = t.collection_id)", IsDefault: false),
        new("borrower", "Prêté à", $"(SELECT l.borrower FROM loan l WHERE l.copy_id = {MainCopy} AND l.returned_at IS NULL)", IsDefault: false),
        new("resolution", "Résolution", $"(SELECT max(mf.width) || '×' || max(mf.height) FROM {Files} AND mf.height IS NOT NULL)", IsDefault: false),
        new("media_id", "MediaID", $"(SELECT group_concat(DISTINCT mf.volume_label) FROM {Files})", IsDefault: false),
        new("files", "Fichiers", $"(SELECT group_concat(CASE WHEN sr.path IS NULL THEN mf.path ELSE sr.path || '/' || mf.path END, ' | ') FROM {Files})", IsDefault: false),
        new("size", "Taille (Go)", $"(SELECT round(sum(mf.size_bytes) / 1073741824.0, 2) FROM {Files})", IsNumber: true, IsDefault: false),
        new("tmdb_id", "TMDb", "t.tmdb_id", IsNumber: true, IsDefault: false),
        new("imdb_id", "IMDb", "t.imdb_id", IsDefault: false),
        new("added_at", "Ajouté le", "substr(t.added_at, 1, 10)", IsDefault: false),
        new("overview", "Synopsis", "t.overview", IsDefault: false),
    ];

    /// <summary>Lignes à exporter (une par titre, dans l'ordre donné) : valeurs des colonnes choisies.</summary>
    public IReadOnlyList<object?[]> Rows(IReadOnlyList<ExportColumn> columns, IReadOnlyList<long> titleIds)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(titleIds);
        if (columns.Count == 0 || titleIds.Count == 0)
        {
            return [];
        }

        using var connection = database.Open();
        var sql = $"SELECT t.id, {string.Join(", ", columns.Select(c => c.Sql))} FROM title t;";
        var byId = new Dictionary<long, object?[]>();
        using (var reader = connection.ExecuteReader(sql))
        {
            while (reader.Read())
            {
                var values = new object?[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                {
                    values[i] = reader.IsDBNull(i + 1) ? null : reader.GetValue(i + 1);
                }

                byId[reader.GetInt64(0)] = values;
            }
        }

        return [.. titleIds.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    /// <summary>Écrit le fichier d'export ; les libellés des colonnes et les « Oui »/« Non » sont traduits.</summary>
    public int Export(string path, ExportFormat format, IReadOnlyList<ExportColumn> columns, IReadOnlyList<long> titleIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var rows = Rows(columns, titleIds)
            .Select(r => r.Select(v => v is string s && TranslatedValues.Contains(s) ? Tr.T(s) : v).ToArray())
            .ToList();
        var headers = columns.Select(c => Tr.T(c.Label)).ToList();
        switch (format)
        {
            case ExportFormat.Xlsx:
                WriteXlsx(path, headers, rows);
                break;
            default:
                WriteDelimited(path, format == ExportFormat.Tsv ? "\t" : CultureInfo.CurrentCulture.TextInfo.ListSeparator, headers, rows);
                break;
        }

        return rows.Count;
    }

    /// <summary>CSV (séparateur de la langue du système, « ; » en français) ou TSV, en UTF-8 avec BOM pour Excel.</summary>
    internal static void WriteDelimited(string path, string separator, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(separator, headers.Select(h => Quote(h, separator))));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(separator, row.Select(v => Quote(Format(v), separator))));
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        double d => d.ToString("0.##", CultureInfo.CurrentCulture),
        float f => f.ToString("0.##", CultureInfo.CurrentCulture),
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Quote(string text, string separator)
    {
        if (separator == "\t")
        {
            // TSV : pas de guillemets ; tabulations et retours à la ligne remplacés par des espaces.
            return text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        return text.Contains(separator, StringComparison.Ordinal) || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    /// <summary>Classeur Excel (.xlsx) minimal : une feuille, en-tête en gras et figé, nombres en cellules numériques.</summary>
    internal static void WriteXlsx(string path, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Entry(string name, string content)
        {
            using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }

        Entry("[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);
        Entry("_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        Entry("xl/workbook.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
            <sheets><sheet name="{SecurityElement.Escape(Tr.T("Films"))}" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        Entry("xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        Entry("xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
            <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
            <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
            <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
            <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
            <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>
            </styleSheet>
            """);

        var sheet = new StringBuilder();
        sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""").Append('\n');
        sheet.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sheet.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        sheet.Append("<sheetData>");
        AppendRow(sheet, 1, headers.Cast<object?>().ToArray(), bold: true);
        for (var i = 0; i < rows.Count; i++)
        {
            AppendRow(sheet, i + 2, rows[i], bold: false);
        }

        sheet.Append("</sheetData>");
        if (headers.Count > 0)
        {
            sheet.Append(CultureInfo.InvariantCulture, $"""<autoFilter ref="A1:{ColumnName(headers.Count - 1)}{rows.Count + 1}"/>""");
        }

        sheet.Append("</worksheet>");
        Entry("xl/worksheets/sheet1.xml", sheet.ToString());
    }

    private static void AppendRow(StringBuilder sheet, int number, object?[] values, bool bold)
    {
        sheet.Append(CultureInfo.InvariantCulture, $"<row r=\"{number}\">");
        for (var i = 0; i < values.Length; i++)
        {
            var reference = ColumnName(i) + number.ToString(CultureInfo.InvariantCulture);
            var style = bold ? " s=\"1\"" : string.Empty;
            switch (values[i])
            {
                case null:
                    break;
                case long or int or double or float or decimal:
                    var number2 = Convert.ToDouble(values[i], CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"{style}><v>{number2}</v></c>");
                    break;
                default:
                    var text = SecurityElement.Escape(Convert.ToString(values[i], CultureInfo.CurrentCulture) ?? string.Empty);
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{text}</t></is></c>");
                    break;
            }
        }

        sheet.Append("</row>");
    }

    /// <summary>Nom de colonne Excel : 0 → A, 25 → Z, 26 → AA.</summary>
    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }
}
