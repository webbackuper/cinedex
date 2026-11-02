using System.Collections.ObjectModel;
using System.Reflection;
using Cinedex.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cinedex.App.ViewModels;

/// <summary>Fenêtre d'aide : sommaire des rubriques, recherche, rubrique affichée.</summary>
public sealed partial class HelpViewModel : ObservableObject
{
    private readonly HelpDocument _document;

    public HelpViewModel(HelpDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        ApplyFilter();
    }

    public string Title => _document.Title;

    /// <summary>« Cinedex 0.2.0 » : version de l'application.</summary>
    public static string VersionText =>
        "Cinedex " + (typeof(HelpViewModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : string.Empty);

    public ObservableCollection<HelpSection> Sections { get; } = [];

    [ObservableProperty]
    public partial HelpSection? SelectedSection { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var previous = SelectedSection;
        Sections.Clear();
        foreach (var section in _document.Sections.Where(s => query.Length == 0 || s.PlainText.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            Sections.Add(section);
        }

        SelectedSection = previous is not null && Sections.Contains(previous) ? previous : Sections.FirstOrDefault();
    }
}
