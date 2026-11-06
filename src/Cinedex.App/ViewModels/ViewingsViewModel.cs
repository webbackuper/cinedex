using System.Collections.ObjectModel;
using Cinedex.Core.Localization;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>Une ligne de l'historique de visionnage, prête à afficher.</summary>
public sealed record ViewingLine(Viewing Viewing)
{
    public string Text => Describe(Viewing);

    /// <summary>« 12/10/2026 (lecture) », ou « date inconnue » pour un visionnage sans date.</summary>
    public static string Describe(Viewing viewing)
    {
        ArgumentNullException.ThrowIfNull(viewing);
        var date = viewing.ViewedAt is { } when ? Tr.F("{0:d}", when.ToLocalTime()) : Tr.T("date inconnue");
        return viewing.Source switch
        {
            ViewingSource.Playback => Tr.F("{0} (lecture)", date),
            ViewingSource.Import => Tr.F("{0} (import)", date),
            _ => date,
        };
    }
}

/// <summary>
/// Historique de visionnage d'un titre : chaque fois qu'on le revoit est un événement de plus, daté. C'est ce
/// qui permet « mes films de cette année » et « revu trois fois », là où un simple drapeau « vu » ne dit rien.
/// Comme pour les prêts, chaque action est enregistrée aussitôt.
/// </summary>
public sealed partial class ViewingsViewModel : ObservableObject
{
    private readonly TitleRepository _titles;
    private readonly long _titleId;

    public ViewingsViewModel(TitleRepository titles, long titleId)
    {
        ArgumentNullException.ThrowIfNull(titles);
        _titles = titles;
        _titleId = titleId;
        Reload();
    }

    public ObservableCollection<ViewingLine> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    /// <summary>Vrai dès qu'un visionnage a été ajouté ou effacé : la liste du catalogue est à rafraîchir.</summary>
    public bool Changed { get; private set; }

    /// <summary>Date du visionnage à ajouter ; aujourd'hui par défaut, modifiable pour saisir une séance passée.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? Date { get; set; } = DateTimeOffset.Now;

    public string CountText => History.Count switch
    {
        0 => Tr.T("Jamais vu"),
        1 => Tr.T("Vu une fois"),
        var n => Tr.F("Vu {0} fois", n),
    };

    [RelayCommand]
    private void Add()
    {
        _titles.AddViewing(_titleId, Date);
        Changed = true;
        Reload();
    }

    [RelayCommand]
    private void Delete(ViewingLine? line)
    {
        if (line is null)
        {
            return;
        }

        _titles.DeleteViewing(line.Viewing.Id);
        Changed = true;
        Reload();
    }

    private void Reload()
    {
        History.Clear();
        foreach (var viewing in _titles.Viewings(_titleId))
        {
            History.Add(new ViewingLine(viewing));
        }

        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(CountText));
    }
}
