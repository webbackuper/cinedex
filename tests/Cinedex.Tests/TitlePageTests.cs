using Cinedex.App.ViewModels;
using Cinedex.Core.People;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>La fiche (double-clic) reprend tout ce que montre le panneau de droite.</summary>
public sealed class TitlePageTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;

    public TitlePageTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _titles = new TitleRepository(db);
        _files = new MediaFileRepository(db);
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(170, "2 h 50")]
    [InlineData(105, "1 h 45")]
    [InlineData(120, "2 h")]
    [InlineData(45, "45 min")]
    [InlineData(null, "")]
    public void Runtimes_are_shown_in_hours_and_minutes(int? minutes, string expected)
    {
        Assert.Equal(expected, TitleText.FormatRuntime(minutes));
        Assert.Equal(expected, new TitleDetail { RuntimeMinutes = minutes }.RuntimeText);
    }

    [Fact]
    public void Technical_details_give_full_path_codecs_and_tracks()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var path = Path.Combine(_temp.Path, "Films", "Heat (1995).mkv");
        var fileId = _files.Attach(id, path, 3L << 30);
        _files.SaveProperties(fileId, new MediaFileProperties("Matroska", "HEVC", 3840, 2160, 23.976, 10, "16:9", ["HDR10"], 6127, [("TrueHD", 8, "eng"), ("AC-3", 6, "fre")], ["fre", "eng"]));

        var text = Assert.Single(_files.TechnicalDetails(id)).Describe();

        Assert.Contains("Emplacement : " + path, text, StringComparison.Ordinal);
        Assert.Contains("Durée : 1 h 42 min 07 s", text, StringComparison.Ordinal);
        Assert.Contains("Conteneur : Matroska", text, StringComparison.Ordinal);
        Assert.Contains("HEVC · 3840×2160 · 16:9", text, StringComparison.Ordinal);
        Assert.Contains("TrueHD 8ch (eng), AC-3 6ch (fre)", text, StringComparison.Ordinal);
        Assert.Contains("Sous-titres : fre, eng", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_not_yet_read_says_so()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        _files.Attach(id, Path.Combine(_temp.Path, "ran.avi"), 10);

        var detail = Assert.Single(_files.TechnicalDetails(id));

        Assert.False(detail.HasTechnical);
        Assert.Contains("Relire les infos techniques", detail.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_shows_and_saves_external_ratings_and_seen()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _titles.Update(_titles.Get(id)! with { ImdbRating = 8.3, ImdbVotes = 700_000, Tomatometer = 88, Metascore = 76, TmdbRating = 7.9, RuntimeMinutes = 170 });
        var cast = new List<CastMember> { new(1, "Al Pacino", "actor", "Vincent Hanna", 0, null, null) };
        var editor = new TitleEditorViewModel(_titles.Get(id)!, [], [], [], [], null,
            new TitleEditorExtras(Cast: cast, CrewText: "Réalisation : Michael Mann", MediaText: "Emplacement : X:\\heat.mkv"));

        Assert.Equal("soit 2 h 50", editor.RuntimeText);
        Assert.Equal("88", editor.Tomatometer);
        Assert.Equal("76", editor.Metascore);
        Assert.True(editor.HasCast);
        Assert.Contains("X:\\heat.mkv", editor.MediaText, StringComparison.Ordinal);
        Assert.False(editor.IsSeen);

        editor.Tomatometer = "91";
        editor.ImdbRating = "8,4";
        editor.IsSeen = true;
        Assert.True(editor.TryBuild(out var updated), editor.LastError);
        _titles.Update(updated);
        Assert.True(editor.SeenChanged);

        var saved = _titles.Get(id)!;
        Assert.Equal(91, saved.Tomatometer);
        Assert.Equal(8.4, saved.ImdbRating);
        Assert.Equal(76, saved.Metascore);
    }

    [Fact]
    public void Editor_rejects_out_of_range_scores()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var editor = new TitleEditorViewModel(_titles.Get(id)!, [], [], [], []) { Metascore = "140" };

        Assert.False(editor.TryBuild(out _));
        Assert.Contains("Metascore", editor.LastError, StringComparison.Ordinal);
    }
}
