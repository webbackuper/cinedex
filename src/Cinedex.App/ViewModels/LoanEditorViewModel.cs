using Cinedex.App.Services;
using System.Collections.ObjectModel;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

/// <summary>Une ligne de l'historique des prêts, prête à afficher.</summary>
public sealed record LoanLine(Loan Loan)
{
    public string Text => Describe(Loan);

    /// <summary>« Marie : du 01/10/2026 au 12/10/2026 (11 j) », ou « … à aujourd'hui, pas encore rendu ».</summary>
    public static string Describe(Loan loan)
    {
        ArgumentNullException.ThrowIfNull(loan);
        return loan.ReturnedAt is { } r
            ? Tr.F("{0} : du {1:d} au {2:d} ({3} j)", loan.Borrower, loan.LoanedAt.ToLocalTime(), r.ToLocalTime(), Math.Max(0, (r - loan.LoanedAt).Days))
            : Tr.F("{0} : du {1:d} à aujourd'hui, pas encore rendu", loan.Borrower, loan.LoanedAt.ToLocalTime());
    }
}

/// <summary>
/// Prêt de l'exemplaire depuis la fiche : prêter (emprunteur, date), rendre (date), historique et correction.
/// Chaque action est enregistrée aussitôt, comme dans le panneau de droite.
/// </summary>
public sealed partial class LoanEditorViewModel : ObservableObject
{
    private readonly TitleRepository _titles;
    private readonly long _titleId;

    public LoanEditorViewModel(TitleRepository titles, long titleId)
    {
        ArgumentNullException.ThrowIfNull(titles);
        _titles = titles;
        _titleId = titleId;
        Borrowers = titles.Borrowers();
        Reload();
    }

    public IReadOnlyList<string> Borrowers { get; }

    public ObservableCollection<LoanLine> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    /// <summary>Vrai dès qu'un prêt a été enregistré, rendu ou effacé : la liste du catalogue est à rafraîchir.</summary>
    public bool Changed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaned), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(LendCommand), nameof(ReturnCommand))]
    public partial Loan? Current { get; private set; }

    public bool IsLoaned => Current is not null;

    public string StatusText => Current is { } loan
        ? Tr.F("Prêté à {0} depuis le {1:d}", loan.Borrower, loan.LoanedAt.ToLocalTime())
        : "Disponible";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LendCommand))]
    public partial string BorrowerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? LoanDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial DateTime? ReturnDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string? Error { get; set; }

    [RelayCommand(CanExecute = nameof(CanLend))]
    private void Lend()
    {
        var when = LoanDate ?? DateTime.Today;
        if (when.Date > DateTime.Today)
        {
            Error = "La date de prêt ne peut pas être dans le futur.";
            return;
        }

        try
        {
            _titles.Lend(_titleId, BorrowerText, At(when));
            ActivityJournal.Current.Write($"Prêté à {BorrowerText.Trim()} le {when:d} (titre n° {_titleId})");
            Error = null;
            Changed = true;
            ReturnDate = DateTime.Today;
            Reload();
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }
    }

    private bool CanLend() => Current is null && !string.IsNullOrWhiteSpace(BorrowerText);

    [RelayCommand(CanExecute = nameof(IsLoaned))]
    private void Return()
    {
        var when = ReturnDate ?? DateTime.Today;
        if (Current is { } loan && when.Date < loan.LoanedAt.ToLocalTime().Date)
        {
            Error = "La date de retour est antérieure à la date de prêt.";
            return;
        }

        _titles.Return(_titleId, At(when));
        ActivityJournal.Current.Write($"Rendu le {when:d} (titre n° {_titleId})");
        Error = null;
        Changed = true;
        BorrowerText = string.Empty;
        Reload();
    }

    [RelayCommand]
    private void Delete(LoanLine? line)
    {
        if (line is null)
        {
            return;
        }

        _titles.DeleteLoan(line.Loan.Id);
        ActivityJournal.Current.Write($"Ligne de prêt effacée : {line.Loan.Borrower} (titre n° {_titleId})");
        Changed = true;
        Reload();
    }

    private void Reload()
    {
        History.Clear();
        foreach (var loan in _titles.LoanHistory(_titleId))
        {
            History.Add(new LoanLine(loan));
        }

        Current = History.Select(l => l.Loan).FirstOrDefault(l => l.ReturnedAt is null);
        if (Current is null && string.IsNullOrEmpty(BorrowerText))
        {
            BorrowerText = _titles.LastBorrower() ?? string.Empty;
        }

        OnPropertyChanged(nameof(HasHistory));
    }

    /// <summary>Date choisie à l'heure actuelle si c'est aujourd'hui, à midi sinon (évite les décalages de fuseau).</summary>
    private static DateTimeOffset At(DateTime day) =>
        day.Date == DateTime.Today ? DateTimeOffset.Now : new DateTimeOffset(day.Date.AddHours(12));
}
