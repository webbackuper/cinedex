using System.Collections.ObjectModel;
using Bobine.Core.Titles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bobine.App.ViewModels;

/// <summary>Une condition en cours de saisie : champ, opérateur (selon le type du champ) et valeur.</summary>
public sealed partial class RuleRowViewModel : ObservableObject
{
    public RuleRowViewModel(IReadOnlyList<Choice<string>> genres, SearchRule? rule = null)
    {
        Genres = genres;
        SelectedField = (rule is null ? null : SearchFields.Find(rule.Field)) ?? SearchFields.All[0];
        if (rule is not null)
        {
            SelectedOperator = Operators.FirstOrDefault(o => o.Value == rule.Operator) ?? Operators[0];
            Value = SelectedField.Kind == FieldKind.Genre ? string.Empty : rule.Value ?? string.Empty;
            SelectedGenre = Genres.FirstOrDefault(g => g.Value == rule.Value) ?? Genres.FirstOrDefault();
        }
    }

    public static IReadOnlyList<SearchField> Fields => SearchFields.All;

    public IReadOnlyList<Choice<string>> Genres { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Operators), nameof(IsTextValue), nameof(IsGenreValue), nameof(Watermark))]
    public partial SearchField SelectedField { get; set; }

    public IReadOnlyList<Choice<RuleOperator>> Operators =>
        SearchFields.OperatorsFor(SelectedField.Kind).Select(o => new Choice<RuleOperator>(o, SearchFields.Label(o, SelectedField.Kind))).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextValue), nameof(IsGenreValue))]
    public partial Choice<RuleOperator>? SelectedOperator { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Choice<string>? SelectedGenre { get; set; }

    public bool IsTextValue => SelectedOperator is { } op && SearchFields.NeedsValue(op.Value) && SelectedField.Kind != FieldKind.Genre;

    public bool IsGenreValue => SelectedOperator is { } op && SearchFields.NeedsValue(op.Value) && SelectedField.Kind == FieldKind.Genre;

    public string Watermark => SelectedField.Kind == FieldKind.Number ? "nombre (7 ou 7,5)" : "texte";

    partial void OnSelectedFieldChanged(SearchField value)
    {
        // Nouveau champ : premier opérateur adapté à son type (contient, =, est, oui...).
        SelectedOperator = Operators[0];
        SelectedGenre ??= Genres.FirstOrDefault();
    }

    public SearchRule ToRule() => new(
        SelectedField.Key,
        SelectedOperator?.Value ?? SearchFields.OperatorsFor(SelectedField.Kind)[0],
        SelectedField.Kind == FieldKind.Genre ? SelectedGenre?.Value : Value);
}

/// <summary>
/// Recherche multicritère sur tous les champs : une liste de conditions combinées par ET (toutes) ou OU (au moins une).
/// Le nombre de films trouvés est recalculé à chaque modification.
/// </summary>
public sealed partial class RuleSearchViewModel : ObservableObject
{
    private readonly Func<RuleSearch, int> _count;

    public RuleSearchViewModel(RuleSearch? current, IReadOnlyList<Genre> genres, Func<RuleSearch, int> count)
    {
        ArgumentNullException.ThrowIfNull(genres);
        _count = count;
        GenreChoices = genres.Select(g => new Choice<string>(g.Code, g.Label)).ToList();
        SelectedMatch = current is { MatchAll: false } ? MatchChoices[1] : MatchChoices[0];
        foreach (var rule in current?.Rules ?? [])
        {
            Add(new RuleRowViewModel(GenreChoices, rule));
        }

        if (Rules.Count == 0)
        {
            Add(new RuleRowViewModel(GenreChoices));
        }

        UpdateCount();
    }

    public IReadOnlyList<Choice<string>> GenreChoices { get; }

    public IReadOnlyList<Choice<bool>> MatchChoices { get; } =
    [
        new(true, "toutes les conditions (ET)"),
        new(false, "au moins une condition (OU)"),
    ];

    [ObservableProperty]
    public partial Choice<bool> SelectedMatch { get; set; }

    public ObservableCollection<RuleRowViewModel> Rules { get; } = [];

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    partial void OnSelectedMatchChanged(Choice<bool> value) => UpdateCount();

    [RelayCommand]
    private void AddRule()
    {
        Add(new RuleRowViewModel(GenreChoices));
        UpdateCount();
    }

    [RelayCommand]
    private void RemoveRule(RuleRowViewModel? row)
    {
        if (row is not null && Rules.Remove(row))
        {
            row.PropertyChanged -= OnRowChanged;
            UpdateCount();
        }
    }

    [RelayCommand]
    private void ClearRules()
    {
        foreach (var row in Rules)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rules.Clear();
        AddRule();
    }

    /// <summary>Conditions retenues ; null si aucune n'est exploitable (la recherche est alors levée).</summary>
    public RuleSearch? Build()
    {
        var rules = Rules.Select(r => r.ToRule())
            .Where(r => !r.NeedsValue || (SearchFields.Find(r.Field)?.Kind == FieldKind.Number ? r.Number is not null : !string.IsNullOrWhiteSpace(r.Value)))
            .ToList();
        return rules.Count == 0 ? null : new RuleSearch(rules, SelectedMatch.Value);
    }

    private void Add(RuleRowViewModel row)
    {
        row.PropertyChanged += OnRowChanged;
        Rules.Add(row);
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var search = Build();
        CountText = search is null
            ? "Complétez au moins une condition."
            : _count(search) switch
            {
                0 => "Aucun film ne correspond.",
                1 => "1 film correspond.",
                var n => $"{n} films correspondent.",
            };
    }
}
