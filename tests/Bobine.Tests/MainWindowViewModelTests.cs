using Bobine.App.ViewModels;
using Bobine.Core;
using Bobine.Core.Titles;
using Bobine.Data;

namespace Bobine.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _repo;
    private readonly FakeDialogs _dialogs = new();
    private readonly MainWindowViewModel _vm;

    public MainWindowViewModelTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "bobine.db"));
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
    }
}
