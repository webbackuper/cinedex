using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Cinedex.App.Services;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

/// <summary>Une fiche TMDb proposée, avec son affiche (chargée à la demande) et son lien vers le site.</summary>
public sealed partial class TmdbCandidate(MetadataSearchResult result) : ObservableObject
{
    public MetadataSearchResult Result { get; } = result;

    public string Title => Result.Title;

    public string Details => string.Join(" · ", new[]
    {
        Result.Year?.ToString(CultureInfo.CurrentCulture) ?? "année inconnue",
        Result.OriginalTitle is { } o && o != Result.Title ? o : null,
        Result.Rating is > 0 ? Tr.F("TMDb {0}", Result.Rating.Value.ToString("0.0", CultureInfo.CurrentCulture)) : null,
    }.Where(p => p is not null));

    public string Overview => string.IsNullOrWhiteSpace(Result.Overview) ? "Pas de résumé sur TMDb." : Result.Overview;

    public string TmdbUrl => TmdbLinks.Title(Result.Kind, Result.SourceId);

    [ObservableProperty]
    public partial Bitmap? Poster { get; set; }
}

/// <summary>Un titre à vérifier : ce qui a été cherché, le fichier concerné et les candidats.</summary>
public sealed class ReviewItem(MatchReview review)
{
    public MatchReview Review { get; } = review;

    public long TitleId => Review.TitleId;

    public string Header => Review.Year is { } y ? $"{Review.Title} ({y})" : Review.Title;

    public string FileName => Review.FilePath is { } path ? Path.GetFileName(path) : "(pas de fichier)";

    public string? FilePath => Review.FilePath;

    public string SearchText => Tr.F("Cherché sur TMDb : « {0} » · {1} possibilité(s)", Review.Query, Review.Candidates.Count);

    public string SearchUrl => TmdbLinks.Search(Review.Query);

    public ObservableCollection<TmdbCandidate> Candidates { get; } = [.. review.Candidates.Select(c => new TmdbCandidate(c))];
}

/// <summary>
/// Liste « À vérifier » : pour chaque titre au rapprochement ambigu, l'utilisateur choisit la bonne fiche TMDb
/// (ou aucune). Chaque choix est appliqué aussitôt, sans créer de nouvelle entrée.
/// </summary>
public sealed partial class MatchReviewViewModel : ObservableObject
{
    private readonly MatchReviewRepository _reviews;
    private readonly Dictionary<string, Bitmap?> _posters = new(StringComparer.Ordinal);

    public MatchReviewViewModel(MatchReviewRepository reviews, MetadataService metadata)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        _reviews = reviews;
        Metadata = metadata;
        foreach (var review in reviews.List())
        {
            Items.Add(new ReviewItem(review));
        }

        SelectedItem = Items.FirstOrDefault();
        UpdateMessage();
    }

    public MetadataService Metadata { get; }

    public ObservableCollection<ReviewItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ReviewItem? SelectedItem { get; set; }

    public bool HasSelection => SelectedItem is not null;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand), nameof(DismissCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Vrai dès qu'un titre a été lié ou écarté : la liste du catalogue est à rafraîchir.</summary>
    public bool Changed { get; private set; }

    partial void OnSelectedItemChanged(ReviewItem? value)
    {
        if (value is not null)
        {
            _ = LoadPostersAsync(value);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ChooseAsync(TmdbCandidate? candidate) => candidate is null ? Task.CompletedTask : ApplyAsync(candidate.Result);

    /// <summary>Fiche trouvée par une autre recherche (fenêtre de recherche TMDb) : appliquée comme un candidat.</summary>
    public Task ApplyAsync(MetadataSearchResult result) => RunAsync(async item =>
    {
        try
        {
            await Metadata.UpdateAsync(item.TitleId, result.Kind, result.SourceId, ImportOptions.Default);
            _reviews.Remove(item.TitleId);
            Done(item, Tr.F("« {0} » lié à « {1} »{2}.", item.Header, result.Title, (result.Year is { } y ? $" ({y})" : string.Empty)));
        }
        catch (DuplicateTitleException ex)
        {
            Message = ex.Message;
        }
        catch (Exception ex) when (ex is TmdbAuthenticationException or TmdbUnavailableException or HttpRequestException or TaskCanceledException or KeyNotFoundException)
        {
            Message = "TMDb : " + ex.Message;
        }
    });

    /// <summary>Aucune proposition ne convient : le titre sort de la liste et reste sans fiche TMDb.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task DismissAsync() => RunAsync(item =>
    {
        _reviews.Remove(item.TitleId);
        Done(item, Tr.F("« {0} » laissé sans fiche TMDb (Ctrl+U sur le titre pour le lier plus tard).", item.Header));
        return Task.CompletedTask;
    });

    private bool CanAct() => !IsBusy;

    private async Task RunAsync(Func<ReviewItem, Task> action)
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action(item);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Done(ReviewItem item, string message)
    {
        Changed = true;
        var index = Items.IndexOf(item);
        Items.Remove(item);
        SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        UpdateMessage(message);
    }

    private void UpdateMessage(string? prefix = null)
    {
        var rest = Items.Count switch
        {
            0 => "Plus rien à vérifier.",
            1 => "1 titre à vérifier.",
            var n => Tr.F("{0} titres à vérifier.", n),
        };
        Message = prefix is null ? rest : $"{prefix} {rest}";
    }

    private async Task LoadPostersAsync(ReviewItem item)
    {
        foreach (var row in item.Candidates.Where(r => r.Poster is null && r.Result.PosterPath is not null).ToList())
        {
            var path = row.Result.PosterPath!;
            if (!_posters.TryGetValue(path, out var bitmap))
            {
                try
                {
                    var bytes = await Metadata.DownloadThumbnailAsync(path);
                    using var stream = new MemoryStream(bytes);
                    bitmap = new Bitmap(stream);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TmdbUnavailableException or ArgumentException or InvalidOperationException or IOException)
                {
                    // Affiche indisponible : le candidat reste choisissable sur son titre et son résumé.
                    bitmap = null;
                }

                _posters[path] = bitmap;
            }

            row.Poster = bitmap;
        }
    }
}
