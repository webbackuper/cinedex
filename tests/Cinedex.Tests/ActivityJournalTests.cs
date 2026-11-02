using Cinedex.App.Services;

namespace Cinedex.Tests;

/// <summary>Journal des actions : actif par défaut, désactivable, exportable. Hors parallélisme (journal global).</summary>
[Collection(nameof(LocalizationTests))]
public sealed class ActivityJournalTests
{
    [Fact]
    public void Actions_are_logged_by_default_can_be_disabled_and_exported()
    {
        using var temp = new TempDirectory();
        var logs = Path.Combine(temp.Path, "logs");
        try
        {
            var journal = ActivityJournal.Configure(temp.Path, logs);
            journal.Catalog = "films";
            Assert.True(journal.Enabled);

            journal.Write("Titre ajouté à la main : Heat (1995)");
            var line = Assert.Single(File.ReadAllLines(journal.TodayPath));
            Assert.Contains("[films]  Titre ajouté à la main : Heat (1995)", line, StringComparison.Ordinal);

            journal.SetEnabled(false);
            journal.Write("non noté");
            Assert.False(ActivityJournal.Configure(temp.Path, logs).Enabled);

            var export = Path.Combine(temp.Path, "export.log");
            Assert.Equal(2, ActivityJournal.Current.Export(export));
            Assert.Contains("Journal des actions désactivé", File.ReadAllText(export), StringComparison.Ordinal);
            Assert.DoesNotContain("non noté", File.ReadAllText(export), StringComparison.Ordinal);

            ActivityJournal.Current.SetEnabled(true);
            Assert.True(ActivityJournal.Configure(temp.Path, logs).Enabled);
        }
        finally
        {
            ActivityJournal.Configure(temp.Path, logs).SetEnabled(false);
        }
    }
}
