using Cinedex.App.ViewModels;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

public sealed class LoanEditorTests
{
    [Fact]
    public void Lend_return_and_correct_from_the_title_page()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var id = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var loans = new LoanEditorViewModel(titles, id);

        Assert.False(loans.IsLoaned);
        Assert.False(loans.LendCommand.CanExecute(null));

        loans.BorrowerText = "Marie";
        loans.LoanDate = DateTime.Today.AddDays(-10);
        loans.LendCommand.Execute(null);
        Assert.True(loans.IsLoaned);
        Assert.Equal("Marie", titles.Get(id)!.CurrentLoan!.Borrower);

        loans.ReturnDate = DateTime.Today.AddDays(-20);
        loans.ReturnCommand.Execute(null);
        Assert.NotNull(loans.Error);
        Assert.True(loans.IsLoaned);

        loans.ReturnDate = DateTime.Today;
        loans.ReturnCommand.Execute(null);
        Assert.False(loans.IsLoaned);
        Assert.Null(titles.Get(id)!.CurrentLoan);
        Assert.Contains("Marie : du", Assert.Single(loans.History).Text, StringComparison.Ordinal);
        Assert.Equal(["Marie"], titles.Borrowers());

        loans.DeleteCommand.Execute(loans.History[0]);
        Assert.Empty(loans.History);
        Assert.True(loans.Changed);
    }
}
