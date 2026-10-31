using System.Collections.ObjectModel;
using Bobine.Core;
using Bobine.Core.Titles;
using Bobine.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bobine.App.ViewModels;

public sealed partial class MainWindowViewModel(TitleRepository titles, AppPaths paths) : ObservableObject
{
    public ObservableCollection<TitleSummary> Titles { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewTitleText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    public string DataLocation => paths.IsPortable ? $"Mode portable : {paths.DataRoot}" : paths.DataRoot;

    public void Load() => Refresh();

    partial void OnSearchTextChanged(string value) => Refresh();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var (title, year) = ParseTitleAndYear(NewTitleText);
        titles.Add(new NewTitle(TitleKind.Movie, title, year));
        NewTitleText = string.Empty;
        Refresh();
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewTitleText);

    private void Refresh()
    {
        Titles.Clear();
        foreach (var title in titles.Search(SearchText))
        {
            Titles.Add(title);
        }

        Status = string.IsNullOrWhiteSpace(SearchText)
            ? $"{Titles.Count} titre(s) dans le catalogue"
            : $"{Titles.Count} titre(s) trouvé(s)";
    }

    /// <summary>« Heat (1995) » donne le titre « Heat » et l'année 1995.</summary>
    internal static (string Title, int? Year) ParseTitleAndYear(string input)
    {
        var text = input.Trim();
        if (text.Length > 7 && text[^1] == ')' && text[^6] == '(' &&
            int.TryParse(text.AsSpan(text.Length - 5, 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year) &&
            year is >= 1870 and <= 2100)
        {
            return (text[..^6].TrimEnd(), year);
        }

        return (text, null);
    }
}
