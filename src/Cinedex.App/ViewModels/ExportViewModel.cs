using System.Globalization;
using Cinedex.App.Services;
using Cinedex.Core.Localization;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cinedex.App.ViewModels;

/// <summary>Une colonne exportable et son état (cochée ou non).</summary>
public sealed partial class ExportColumnToggle(ExportColumn column, bool isChecked) : ObservableObject
{
    public ExportColumn Column { get; } = column;

    public string Label => Tr.T(Column.Label);

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;
}

/// <summary>
/// Onglet Export des réglages : liste des films en CSV, TSV ou Excel, colonnes au choix, liste affichée
/// (filtre et tri actuels) ou catalogue entier. Les choix sont mémorisés pour le catalogue.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    public const string ColumnsKey = "export.columns";
    public const string FormatKey = "export.format";

    private readonly ExportRepository _export;
    private readonly SettingsStore _settings;

    public ExportViewModel(ExportRepository export, SettingsStore settings)
    {
        _export = export;
        _settings = settings;
        var saved = settings.Get(ColumnsKey)?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        Columns = [.. ExportRepository.Columns.Select(c => new ExportColumnToggle(c, saved?.Contains(c.Key) ?? c.IsDefault))];
        Formats = [new(ExportFormat.Csv, Tr.T("CSV (Excel, LibreOffice…)")), new(ExportFormat.Tsv, Tr.T("TSV (texte séparé par des tabulations)")), new(ExportFormat.Xlsx, Tr.T("Excel (.xlsx)"))];
        SelectedFormat = Formats.FirstOrDefault(f => f.Value.ToString() == settings.Get(FormatKey)) ?? Formats[2];
        Scopes = [new(false, Tr.T("La liste affichée (recherche, filtre et tri actuels)")), new(true, Tr.T("Tout le catalogue (tri actuel)"))];
        SelectedScope = Scopes[0];
    }

    public IReadOnlyList<ExportColumnToggle> Columns { get; }

    public IReadOnlyList<Choice<ExportFormat>> Formats { get; }

    public IReadOnlyList<Choice<bool>> Scopes { get; }

    [ObservableProperty] public partial Choice<ExportFormat> SelectedFormat { get; set; }

    [ObservableProperty] public partial Choice<bool> SelectedScope { get; set; }

    [ObservableProperty] public partial string? Message { get; set; }

    /// <summary>Titres de la liste affichée, dans son ordre ; fourni par la fenêtre principale.</summary>
    public Func<IReadOnlyList<long>>? DisplayedIds { get; set; }

    /// <summary>Tous les titres du catalogue dans l'ordre de tri actuel ; fourni par la fenêtre principale.</summary>
    public Func<IReadOnlyList<long>>? AllIds { get; set; }

    public string Extension => SelectedFormat.Value switch
    {
        ExportFormat.Tsv => ".tsv",
        ExportFormat.Xlsx => ".xlsx",
        _ => ".csv",
    };

    public string SuggestedFileName => Tr.F("Cinedex - films {0}", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + Extension;

    public void SelectAll(bool value)
    {
        foreach (var column in Columns)
        {
            column.IsChecked = value;
        }
    }

    /// <summary>Écrit le fichier choisi et mémorise les colonnes et le format ; renvoie le nombre de films exportés.</summary>
    public async Task<int> ExportAsync(string path)
    {
        var columns = Columns.Where(c => c.IsChecked).Select(c => c.Column).ToList();
        if (columns.Count == 0)
        {
            Message = Tr.T("Cochez au moins une colonne à exporter.");
            return 0;
        }

        _settings.Set(ColumnsKey, string.Join(',', columns.Select(c => c.Key)));
        _settings.Set(FormatKey, SelectedFormat.Value.ToString());
        var ids = (SelectedScope.Value ? AllIds : DisplayedIds)?.Invoke() ?? [];
        var format = SelectedFormat.Value;
        try
        {
            var count = await Task.Run(() => _export.Export(path, format, columns, ids));
            Message = Tr.F("{0} film(s) exporté(s) dans {1}", count, path);
            ActivityJournal.Current.Write($"Export de {count} film(s) ({format}, {columns.Count} colonnes) : {path}");
            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
            return 0;
        }
    }
}
