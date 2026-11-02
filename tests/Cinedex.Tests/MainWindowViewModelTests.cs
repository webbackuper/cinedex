using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _repo;
    private readonly FakeDialogs _dialogs = new();
    private readonly MainWindowViewModel _vm;

    public MainWindowViewModelTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _repo = new TitleRepository(db);
        _vm = new MainWindowViewModel(_repo, new AppPaths(_temp.Path, isPortable: false), _dialogs);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Construction_and_load_do_not_throw_and_list_titles()
    {
        _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));

        _vm.Load();

        Assert.Single(_vm.Titles);
        Assert.False(_vm.HasDetail);
    }

    [Fact]
    public void Adding_selects_the_new_title_and_shows_its_detail()
    {
        _vm.Load();
        _vm.NewTitleText = "Alien (1979)";

        _vm.AddCommand.Execute(null);

        var selected = Assert.Single(_vm.SelectedTitles);
        Assert.Equal("Alien", selected.Title);
        Assert.Equal(1979, _vm.Detail!.Year);
        Assert.Equal(string.Empty, _vm.NewTitleText);
    }

    [Fact]
    public void Filter_sort_and_seen_toggle_work_together()
    {
        _repo.Add(new NewTitle(TitleKind.Movie, "B", 2000));
        _repo.Add(new NewTitle(TitleKind.Movie, "A", 2010));
        _vm.Load();

        _vm.SelectedSort = _vm.Sorts.Single(s => s.Value == SortField.Year);
        _vm.SortDescending = true;
        Assert.Equal(["A", "B"], _vm.Titles.Select(t => t.Title));

        _vm.SelectedTitles.Add(_vm.Titles[0]);
        _vm.ToggleSeenCommand.Execute(null);
        _vm.SelectedFilter = _vm.Filters.Single(f => f.Value == QuickFilter.Unseen);

        Assert.Equal(["B"], _vm.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task Delete_asks_for_confirmation()
    {
        _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _vm.Load();
        _vm.SelectedTitles.Add(_vm.Titles[0]);

        _dialogs.Answer = false;
        await _vm.DeleteCommand.ExecuteAsync(null);
        Assert.Single(_vm.Titles);

        _dialogs.Answer = true;
        await _vm.DeleteCommand.ExecuteAsync(null);
        Assert.Empty(_vm.Titles);
        Assert.Equal(2, _dialogs.Confirmations);
    }

    [Fact]
    public void Lend_and_return_from_the_detail_panel()
    {
        _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _vm.Load();
        _vm.SelectedTitles.Add(_vm.Titles[0]);
        _vm.BorrowerText = "Paul";

        _vm.LendCommand.Execute(null);
        Assert.Equal("Paul", _vm.Detail!.CurrentLoan!.Borrower);
        Assert.False(_vm.LendCommand.CanExecute(null));

        _vm.ReturnLoanCommand.Execute(null);
        Assert.Null(_vm.Detail!.CurrentLoan);
    }

    [Fact]
    public async Task Advanced_search_filters_and_clear_resets()
    {
        _repo.Add(new NewTitle(TitleKind.Movie, "Old", 1950));
        _repo.Add(new NewTitle(TitleKind.Movie, "New", 2020));
        _vm.Load();
        _dialogs.OnRuleSearch = search =>
        {
            var row = search.Rules[0];
            row.SelectedField = Cinedex.Core.Titles.SearchFields.Find("year")!;
            row.SelectedOperator = row.Operators.Single(o => o.Value == Cinedex.Core.Titles.RuleOperator.GreaterOrEqual);
            row.Value = "2000";
            Assert.Equal("1 film correspond.", search.CountText);
            return true;
        };

        await _vm.AdvancedSearchCommand.ExecuteAsync(null);
        Assert.True(_vm.HasCriteria);
        Assert.Equal(["New"], _vm.Titles.Select(t => t.Title));

        _vm.ClearSearchCommand.Execute(null);
        Assert.False(_vm.HasCriteria);
        Assert.Equal(2, _vm.Titles.Count);
    }

    [Fact]
    public void Definition_submenu_shares_the_selection_with_the_main_list()
    {
        _vm.Load();
        Assert.Same(_vm.Filters[0], _vm.SelectedMainFilter);
        Assert.False(_vm.IsDefinitionExpanded);
        Assert.DoesNotContain(_vm.Filters, f => f.Value == QuickFilter.PhysicalMedia);

        _vm.SelectedDefinitionFilter = _vm.DefinitionFilters.Single(f => f.Value == QuickFilter.UltraHighDefinition);
        Assert.Null(_vm.SelectedMainFilter);
        Assert.Equal(QuickFilter.UltraHighDefinition, _vm.SelectedFilter.Value);
        Assert.True(_vm.IsDefinitionExpanded);

        _vm.SelectedMainFilter = null;
        Assert.Equal(QuickFilter.UltraHighDefinition, _vm.SelectedFilter.Value);
        _vm.SelectedMainFilter = _vm.Filters[1];
        Assert.Null(_vm.SelectedDefinitionFilter);
    }

    [Fact]
    public async Task Editing_several_titles_opens_the_bulk_editor()
    {
        _repo.Add(new NewTitle(TitleKind.Movie, "A", 2000));
        _repo.Add(new NewTitle(TitleKind.Movie, "B", 2001));
        _vm.Load();
        _vm.SelectedTitles.Add(_vm.Titles[0]);
        _vm.SelectedTitles.Add(_vm.Titles[1]);
        _dialogs.OnBulkEdit = edit =>
        {
            edit.IsFavorite = true;
            edit.AddTags = "lot";
            return true;
        };

        await _vm.EditCommand.ExecuteAsync(null);

        Assert.All(_vm.Titles, t => Assert.True(t.IsFavorite));
        Assert.Equal(2, _vm.SelectedTitles.Count);
        Assert.Equal(["lot"], _repo.Get(_vm.Titles[0].Id)!.Tags);
    }

    private sealed class FakeDialogs : IDialogs
    {
        public bool Answer { get; set; }

        public int Confirmations { get; private set; }

        public Task<bool> EditTitleAsync(TitleEditorViewModel editor) => Task.FromResult(Answer);

        public Task<bool> ConfirmAsync(string title, string message)
        {
            Confirmations++;
            return Task.FromResult(Answer);
        }

        public Task InformAsync(string title, string message) => Task.CompletedTask;

        public Task<Cinedex.Core.Metadata.MetadataSearchResult?> SearchMetadataAsync(MetadataSearchViewModel search) =>
            Task.FromResult<Cinedex.Core.Metadata.MetadataSearchResult?>(null);

        public Task<bool> EditSettingsAsync(SettingsViewModel settings) => Task.FromResult(false);

        public Func<RuleSearchViewModel, bool>? OnRuleSearch { get; set; }

        public Func<BulkEditViewModel, bool>? OnBulkEdit { get; set; }

        public Task<bool> RuleSearchAsync(RuleSearchViewModel search) => Task.FromResult(OnRuleSearch?.Invoke(search) ?? false);

        public Task<bool> BulkEditAsync(BulkEditViewModel edit) => Task.FromResult(OnBulkEdit?.Invoke(edit) ?? false);

        public Task<bool> ImportFolderAsync(ImportFolderViewModel import) => Task.FromResult(false);

        public Task ManageCatalogsAsync(CatalogsViewModel catalogs) => Task.CompletedTask;

        public Task<bool> ShowSeriesAsync(SeriesViewModel series) => Task.FromResult(false);

        public Task<bool> ShowPersonAsync(PersonViewModel person) => Task.FromResult(false);

        public Task<bool> ShowPeopleAsync(PeopleBrowserViewModel people) => Task.FromResult(false);

        public Task<bool> ShowMatchReviewAsync(MatchReviewViewModel review) => Task.FromResult(false);

        public void ShowHelp(HelpViewModel help)
        {
        }
    }
}
