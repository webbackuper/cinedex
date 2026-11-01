using System.Collections.ObjectModel;
using Cinedex.App.Services;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Metadata.Tmdb;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

public sealed record SearchRow(MetadataSearchResult Result)
{
    public string Title => Result.Title;

    public string Details
    {
        get
        {
            var kind = Result.Kind == TitleKind.Series ? "Série" : "Film";
            var year = Result.Year?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "année inconnue";
            var original = Result.OriginalTitle is { } o && o != Result.Title ? $" · {o}" : string.Empty;
            return $"{kind} · {year}{original}";
        }
    }

    public string Overview => Result.Overview ?? string.Empty;
}

/// <summary>Boîte de recherche TMDb : saisie, résultats, choix d'un résultat.</summary>
public sealed partial class MetadataSearchViewModel : ObservableObject
{
    private readonly MetadataService _metadata;

    public MetadataSearchViewModel(MetadataService metadata, string? initialQuery)
    {
        _metadata = metadata;
        Query = initialQuery?.Trim() ?? string.Empty;
    }

    public ObservableCollection<SearchRow> Results { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string Query { get; set; }

    [ObservableProperty]
    public partial SearchRow? Selected { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "Titre, « Titre (année) », tt1234567, movie/123 ou tv/123.";

    public bool AutoSearch => !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanSearch), AllowConcurrentExecutions = false)]
    private async Task SearchAsync()
    {
        Results.Clear();
        Message = "Recherche sur TMDb...";
        try
        {
            var results = await _metadata.SearchAsync(Query);
            foreach (var result in results)
            {
                Results.Add(new SearchRow(result));
            }

            Selected = Results.FirstOrDefault();
            Message = Results.Count == 0 ? "Aucun résultat. Essayez le titre original ou l'identifiant IMDb." : $"{Results.Count} résultat(s)";
        }
        catch (Exception ex) when (ex is TmdbAuthenticationException or TmdbUnavailableException or HttpRequestException or TaskCanceledException or KeyNotFoundException)
        {
            Message = ex.Message;
        }
    }

    private bool CanSearch() => !string.IsNullOrWhiteSpace(Query);
}
