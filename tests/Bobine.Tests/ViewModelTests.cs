using Bobine.App.ViewModels;
using Bobine.Core.Titles;

namespace Bobine.Tests;

public sealed class ViewModelTests
{
    private static readonly Genre[] SomeGenres = [new("crime", "Policier"), new("drama", "Drame")];
    private static readonly RefItem[] SomeMedia = [new("medium", "bluray", "Blu-ray")];

    [Theory]
    [InlineData("Heat (1995)", "Heat", 1995)]
    [InlineData("  2001 : l'odyssée de l'espace (1968) ", "2001 : l'odyssée de l'espace", 1968)]
    [InlineData("Heat", "Heat", null)]
    [InlineData("Film (1234)", "Film (1234)", null)]
    [InlineData("(1995)", "(1995)", null)]
    public void Title_and_year_are_split(string input, string title, int? year)
    {
        Assert.Equal((title, year), MainWindowViewModel.ParseTitleAndYear(input));
    }

    [Fact]
    public void Editor_round_trips_an_unchanged_detail()
    {
        var detail = Sample();
        var editor = NewEditor(detail);

        Assert.True(editor.TryBuild(out var result));
        Assert.Equal(detail.Title, result.Title);
        Assert.Equal(detail.Year, result.Year);
        Assert.Equal(["crime"], result.GenreCodes);
        Assert.Equal("bluray", result.Copy!.MediumCode);
        Assert.Equal(detail.Copy!.Id, result.Copy.Id);
    }

    [Fact]
    public void Editor_reports_every_invalid_field()
    {
        var editor = NewEditor(Sample());
        editor.Title = " ";
        editor.Year = "19x5";
        editor.UserRating = "11";
        editor.ImdbId = "nm0000158";
        editor.WebLink = "file:///etc/passwd";

        Assert.False(editor.TryBuild(out _));
        Assert.Contains("titre est obligatoire", editor.LastError, StringComparison.Ordinal);
        Assert.Contains("Année", editor.LastError, StringComparison.Ordinal);
        Assert.Contains("Note personnelle", editor.LastError, StringComparison.Ordinal);
        Assert.Contains("IMDb", editor.LastError, StringComparison.Ordinal);
        Assert.Contains("https://", editor.LastError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("7,3", 7.5)]
    [InlineData("7.2", 7.0)]
    [InlineData("10", 10.0)]
    public void Personal_rating_is_rounded_to_half_points(string input, double expected)
    {
        var editor = NewEditor(Sample());
        editor.UserRating = input;

        Assert.True(editor.TryBuild(out var result));
        Assert.Equal(expected, result.UserRating);
    }

    [Fact]
    public void Editor_applies_genre_and_copy_changes()
    {
        var editor = NewEditor(Sample());
        editor.Genres.Single(g => g.Genre.Code == "drama").IsSelected = true;
        editor.SelectedMedium = editor.Media[0];
        editor.Price = "19,90";

        Assert.True(editor.TryBuild(out var result));
        Assert.Equal(["crime", "drama"], result.GenreCodes);
        Assert.Null(result.Copy!.MediumCode);
        Assert.Equal(19.90m, result.Copy.Price);
    }

    private static TitleEditorViewModel NewEditor(TitleDetail detail) => new(detail, SomeGenres, SomeMedia, [], []);

    private static TitleDetail Sample() => new()
    {
        Id = 42,
        Title = "Heat",
        Year = 1995,
        GenreCodes = ["crime"],
        Copy = new CopyInfo { Id = 7, Number = 3, MediumCode = "bluray" },
    };
}
