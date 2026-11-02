using Cinedex.App.Services;
using Cinedex.App.ViewModels;

namespace Cinedex.Tests;

public sealed class HelpTests
{
    [Theory]
    [InlineData("fr", "Nouveautés", "Raccourcis clavier")]
    [InlineData("en", "What's new", "Keyboard shortcuts")]
    public void Help_is_complete_and_documents_the_current_version(string language, string whatsNew, string shortcuts)
    {
        var help = HelpDocument.Load(language);

        Assert.True(help.Sections.Count >= 15);
        Assert.All(help.Sections, s => Assert.NotEmpty(s.Blocks));
        Assert.Contains(help.Sections, s => s.Title == shortcuts && s.PlainText.Contains("F1", StringComparison.Ordinal));

        // Chaque nouvelle version doit avoir sa rubrique « Nouveautés » : la plus récente est la version de l'application.
        var version = typeof(HelpViewModel).Assembly.GetName().Version!;
        var news = Assert.Single(help.Sections, s => s.Title == whatsNew);
        Assert.Equal($"{version.Major}.{version.Minor}.{version.Build}", news.Blocks.First(b => b.IsSubheading).Text);
    }

    [Fact]
    public void Other_languages_get_the_english_help_and_search_filters_topics()
    {
        Assert.Equal("Cinedex help", HelpDocument.Load("ja").Title);

        var vm = new HelpViewModel(HelpDocument.Load("fr"));
        Assert.Equal("Bienvenue", vm.SelectedSection!.Title);
        vm.SearchText = "MediaID";
        Assert.Contains(vm.Sections, s => s.Title == "Disques USB et MediaID");
        Assert.DoesNotContain(vm.Sections, s => s.Title == "Prêts");
        Assert.StartsWith("Cinedex 0.", HelpViewModel.VersionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_blocks_are_parsed()
    {
        var doc = HelpDocument.Parse("# T\n## A\nTexte **gras**\n- puce\n### 1.0\n## B\nx");

        Assert.Equal("T", doc.Title);
        Assert.Equal(["A", "B"], doc.Sections.Select(s => s.Title));
        Assert.Equal([HelpBlockKind.Paragraph, HelpBlockKind.Bullet, HelpBlockKind.Subheading], doc.Sections[0].Blocks.Select(b => b.Kind));
        Assert.Contains("Texte gras", doc.Sections[0].PlainText, StringComparison.Ordinal);
    }
}
