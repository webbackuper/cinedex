using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Cinedex.Core.Titles;

namespace Cinedex.App.ViewModels;

/// <summary>
/// Titres de la liste principale. <see cref="ReplaceAll"/> remplace tout le contenu en une seule notification :
/// avec 10 000 titres, ajouter les lignes une à une déclencherait 10 000 mises à jour de l'affichage.
/// </summary>
public sealed class TitleCollection : ObservableCollection<TitleSummary>
{
    public void ReplaceAll(IEnumerable<TitleSummary> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        CheckReentrancy();
        Items.Clear();
        foreach (var title in titles)
        {
            Items.Add(title);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
