using System.Collections.ObjectModel;
using Cinedex.App.Services;
using Cinedex.Core.People;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

/// <summary>Fiche d'une personne : photo, dates, biographie et ses titres présents dans le catalogue.</summary>
public sealed partial class PersonViewModel : ObservableObject
{
    private readonly PeopleService _people;
    private readonly long _personId;
    private readonly string? _role;

    /// <param name="role">« director » : fiche d'un réalisateur, avec ses films réalisés seulement.</param>
    public PersonViewModel(PeopleService people, long personId, string? role = null)
    {
        _people = people;
        _personId = personId;
        _role = role;
        Person = people.Repository.Get(personId) is { } p ? people.Resolve(p) : null;
        LoadFilmography();
    }

    public ObservableCollection<PersonCredit> Titles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Dates), nameof(Biography), nameof(KnownFor), nameof(PhotoPath), nameof(TmdbUrl), nameof(ImdbUrl))]
    public partial PersonDetail? Person { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Titre choisi dans la filmographie : la fenêtre principale le sélectionne.</summary>
    public long? ChosenTitleId { get; private set; }

    public string Name => Person?.Name ?? string.Empty;

    public string Dates => Person?.Dates ?? string.Empty;

    public string? Biography => Person?.Biography;

    public string? KnownFor => Person?.KnownFor;

    public string? PhotoPath => Person?.PhotoPath;

    public string? TmdbUrl => Person?.TmdbId is { } id ? $"https://www.themoviedb.org/person/{id}" : null;

    public string? ImdbUrl => Person?.ImdbId is { } id ? $"https://www.imdb.com/name/{id}/" : null;

    public bool IsDirector => _role == "director";

    public string Heading => IsDirector ? Tr.T("Films réalisés") : Tr.T("Dans le catalogue");

    public string Count => (IsDirector, Titles.Count) switch
    {
        (true, 0) => Tr.T("Aucun film réalisé dans le catalogue"),
        (true, var n) => Tr.F("{0} film(s) réalisé(s) dans le catalogue", n),
        (false, 0) => "Aucun titre du catalogue",
        (false, 1) => "1 titre dans le catalogue",
        (false, var n) => Tr.F("{0} titres dans le catalogue", n),
    };

    /// <summary>Complète la fiche depuis TMDb (biographie, photo) si nécessaire.</summary>
    public async Task LoadDetailsAsync()
    {
        if (Person is { HasDetails: true, PhotoPath: not null })
        {
            return;
        }

        IsLoading = true;
        try
        {
            Person = await _people.EnsureDetailsAsync(_personId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Choose(PersonCredit credit)
    {
        ArgumentNullException.ThrowIfNull(credit);
        ChosenTitleId = credit.TitleId;
    }

    private void LoadFilmography()
    {
        Titles.Clear();
        // Un même titre peut apparaître deux fois (acteur et réalisateur) : une ligne par titre, rôle d'acteur en premier.
        foreach (var credit in _people.Repository.Filmography(_personId, _role).GroupBy(c => c.TitleId).Select(g => g.First()))
        {
            Titles.Add(credit with { PosterPath = _people.ResolvePhoto(credit.PosterRelativePath) });
        }

        OnPropertyChanged(nameof(Count));
    }
}

/// <summary>Recherche des personnes du catalogue : toutes (acteurs et équipe) ou les seuls réalisateurs.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "_cancel n'existe que pendant le téléchargement et est libéré à sa fin.")]
public sealed partial class PeopleBrowserViewModel : ObservableObject
{
    private readonly PeopleService _people;
    private CancellationTokenSource? _cancel;

    /// <param name="role">« director » : annuaire des réalisateurs.</param>
    public PeopleBrowserViewModel(PeopleService people, string? role = null)
    {
        _people = people;
        Role = role;
        Search();
    }

    /// <summary>Personnes affichées au plus : la galerie est virtualisée, la recherche affine au-delà.</summary>
    public const int MaxShown = 2000;

    public PeopleService Service => _people;

    public string? Role { get; }

    public bool IsDirectors => Role == "director";

    public string WindowTitle => IsDirectors ? Tr.T("Réalisateurs du catalogue") : Tr.T("Acteurs du catalogue");

    public string Placeholder => IsDirectors ? Tr.T("Nom d'un réalisateur") : Tr.T("Acteur, réalisateur, scénariste, compositeur…");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadDetailsCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>Télécharge les fiches TMDb (biographie, photo) des personnes listées qui ne les ont pas encore.</summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadDetailsAsync()
    {
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            var ids = People.Select(p => p.Id).ToList();
            var done = await _people.EnsureAllDetailsAsync(ids, new Progress<string>(m => Message = m), _cancel.Token);
            Message = Tr.F("{0} fiche(s) téléchargée(s) depuis TMDb.", done);
            ActivityJournal.Current.Write($"Fiches TMDb des {(IsDirectors ? "réalisateurs" : "personnes")} : {done} téléchargée(s)");
            Search();
        }
        catch (OperationCanceledException)
        {
            Message = Tr.T("Interrompu.");
        }
        finally
        {
            IsBusy = false;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    private bool CanDownload() => !IsBusy;

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    /// <summary>Personnes trouvées, les plus présentes dans le catalogue d'abord (liste remplacée à chaque recherche).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel))]
    public partial IReadOnlyList<PersonSummary> People { get; private set; } = [];

    public string CountLabel => (IsDirectors, People.Count) switch
    {
        (_, 0) => Tr.T("Aucune personne trouvée"),
        (true, var n) => Tr.F("{0} réalisateur(s)", n),
        (false, var n) => Tr.F("{0} personne(s)", n),
    };

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    partial void OnQueryChanged(string value) => Search();

    [RelayCommand]
    private void Search()
    {
        People = [.. _people.Repository.Search(Query, MaxShown, Role).Select(p => p with { PhotoPath = _people.ResolvePhoto(p.PhotoRelativePath) })];
    }
}
