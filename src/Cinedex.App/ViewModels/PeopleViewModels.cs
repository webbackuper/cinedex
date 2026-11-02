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

    public PersonViewModel(PeopleService people, long personId)
    {
        _people = people;
        _personId = personId;
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

    public string Count => Titles.Count switch
    {
        0 => "Aucun titre du catalogue",
        1 => "1 titre dans le catalogue",
        var n => Tr.F("{0} titres dans le catalogue", n),
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
        foreach (var credit in _people.Repository.Filmography(_personId).GroupBy(c => c.TitleId).Select(g => g.First()))
        {
            Titles.Add(credit with { PosterPath = _people.ResolvePhoto(credit.PosterRelativePath) });
        }

        OnPropertyChanged(nameof(Count));
    }
}

/// <summary>Recherche des personnes du catalogue.</summary>
public sealed partial class PeopleBrowserViewModel : ObservableObject
{
    private readonly PeopleService _people;

    public PeopleBrowserViewModel(PeopleService people)
    {
        _people = people;
        Search();
    }

    public PeopleService Service => _people;

    public ObservableCollection<PersonSummary> People { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PersonSummary? Selected { get; set; }

    public long? ChosenTitleId { get; set; }

    partial void OnQueryChanged(string value) => Search();

    [RelayCommand]
    private void Search()
    {
        People.Clear();
        foreach (var person in _people.Repository.Search(Query))
        {
            People.Add(person with { PhotoPath = _people.ResolvePhoto(person.PhotoRelativePath) });
        }
    }
}
