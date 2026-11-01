using System.Text;
using Bobine.App.Services;
using Bobine.Core.Metadata;
using Bobine.Core.Titles;
using Bobine.Data;
using Bobine.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bobine.Tests;

public sealed class NfoTests
{
    // Exemple fourni par l'utilisateur : déclaré UTF-8, mais « ° » encodé en Latin-1 (octet 0xB0).
    private static readonly byte[] UserSample = Encoding.Latin1.GetBytes("""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <movie>
          <title>37°2 Le Matin</title>
          <year>1986</year>
          <uniqueid type="imdb">tt0090563</uniqueid>
          <uniqueid type="tmdb" default="true">11986</uniqueid>
        </movie>
        """);

    private const string FullKodi = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <movie>
          <title>Heat</title>
          <originaltitle>Heat</originaltitle>
          <sorttitle>Heat</sorttitle>
          <ratings><rating name="themoviedb" max="10" default="true"><value>7.9</value><votes>7000</votes></rating></ratings>
          <userrating>9</userrating>
          <plot>Un braqueur et un flic.</plot>
          <tagline>A Los Angeles crime saga</tagline>
          <runtime>170</runtime>
          <mpaa>12</mpaa>
          <uniqueid type="imdb">tt0113277</uniqueid>
          <uniqueid type="tmdb" default="true">949</uniqueid>
          <genre>Policier</genre>
          <genre>Drame / Thriller</genre>
          <country>États-Unis</country>
          <set><name>Michael Mann</name></set>
          <tag>culte</tag>
          <director>Michael Mann</director>
          <credits>Michael Mann</credits>
          <premiered>1995-12-15</premiered>
          <studio>Regency</studio>
          <trailer>javascript:alert(1)</trailer>
          <actor><name>Al Pacino</name><role>Vincent Hanna</role><order>0</order></actor>
          <actor><name>Robert De Niro</name><role>Neil McCauley</role><order>1</order></actor>
          <watched>true</watched>
          <custom>conservé</custom>
        </movie>
        """;

    [Fact]
    public void User_sample_with_latin1_bytes_is_read()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "37°2 Le Matin DC-1080p de Jean-Jacques Beineix - 1985.nfo");
        File.WriteAllBytes(path, UserSample);

        var nfo = NfoReader.Read(path)!;

        Assert.Equal("37°2 Le Matin", nfo.Title);
        Assert.Equal(1986, nfo.Year);
        Assert.Equal("tt0090563", nfo.ImdbId);
        Assert.Equal(11986, nfo.TmdbId);
    }

    [Fact]
    public void Full_kodi_nfo_is_mapped_and_sanitised()
    {
        var nfo = NfoReader.ReadXml(FullKodi)!;

        Assert.Equal(170, nfo.RuntimeMinutes);
        Assert.Equal(7.9, nfo.Rating);
        Assert.Equal(7000, nfo.Votes);
        Assert.Equal(9, nfo.UserRating);
        Assert.Equal(1995, nfo.Year);
        Assert.Equal(["Policier", "Drame", "Thriller"], nfo.Genres);
        Assert.Equal("Michael Mann", nfo.Set);
        Assert.Equal(["Al Pacino", "Robert De Niro"], nfo.Actors.Select(a => a.Name));
        Assert.Equal("Vincent Hanna", nfo.Actors[0].Role);
        Assert.Null(nfo.Trailer);
        Assert.True(nfo.Watched);
    }

    [Fact]
    public void Writing_creates_a_file_compatible_with_the_user_sample()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "film.nfo");

        var result = NfoWriter.Write(path, new NfoInfo { Title = "37°2 Le Matin", Year = 1986, ImdbId = "tt0090563", TmdbId = 11986 });

        Assert.Equal(NfoWriteResult.Created, result);
        var text = File.ReadAllText(path, new UTF8Encoding(false, true));
        Assert.StartsWith("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""", text, StringComparison.Ordinal);
        Assert.Contains("<title>37°2 Le Matin</title>", text, StringComparison.Ordinal);
        Assert.Contains("""<uniqueid type="imdb">tt0090563</uniqueid>""", text, StringComparison.Ordinal);
        Assert.Contains("""<uniqueid type="tmdb" default="true">11986</uniqueid>""", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("<year>", StringComparison.Ordinal) < text.IndexOf("<uniqueid", StringComparison.Ordinal));
        Assert.Equal(11986, NfoReader.Read(path)!.TmdbId);
    }

    [Fact]
    public void Merging_adds_missing_fields_keeps_existing_ones_and_backs_up_once()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "film.nfo");
        File.WriteAllBytes(path, UserSample);

        var values = new NfoInfo
        {
            Title = "37°2 le matin (version longue)",
            Year = 1986,
            TmdbId = 11986,
            Plot = "Zorg et Betty.",
            RuntimeMinutes = 185,
            Genres = ["Drame", "Romance"],
            Directors = ["Jean-Jacques Beineix"],
            Actors = [new NfoActor("Béatrice Dalle", "Betty", 0)],
        };

        Assert.Equal(NfoWriteResult.Updated, NfoWriter.Write(path, values));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(UserSample, File.ReadAllBytes(path + ".bak"));

        var merged = NfoReader.Read(path)!;
        Assert.Equal("37°2 Le Matin", merged.Title);
        Assert.Equal("Zorg et Betty.", merged.Plot);
        Assert.Equal(185, merged.RuntimeMinutes);
        Assert.Equal(["Drame", "Romance"], merged.Genres);
        Assert.Equal("Béatrice Dalle", Assert.Single(merged.Actors).Name);
        Assert.Equal("tt0090563", merged.ImdbId);

        // Un second passage identique ne change rien ; la copie .bak garde l'original.
        Assert.Equal(NfoWriteResult.Unchanged, NfoWriter.Write(path, values));
        Assert.Equal(NfoWriteResult.Updated, NfoWriter.Write(path, values with { Plot = "Autre." }, new NfoWriteOptions { OverwriteExisting = true }));
        Assert.Equal("Autre.", NfoReader.Read(path)!.Plot);
        Assert.Equal(UserSample, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public void Unknown_elements_are_preserved()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "film.nfo");
        File.WriteAllText(path, FullKodi);

        NfoWriter.Write(path, new NfoInfo { Outline = "Résumé court." });

        var text = File.ReadAllText(path);
        Assert.Contains("<custom>conservé</custom>", text, StringComparison.Ordinal);
        Assert.Contains("<outline>Résumé court.</outline>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_nfo_is_only_replaced_when_a_backup_is_allowed()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "film.nfo");
        File.WriteAllText(path, "https://www.imdb.com/title/tt0113277/");

        Assert.Equal(NfoWriteResult.Skipped, NfoWriter.Write(path, new NfoInfo { Title = "Heat" }, new NfoWriteOptions { Backup = false }));
        Assert.Equal(NfoWriteResult.Updated, NfoWriter.Write(path, new NfoInfo { Title = "Heat" }));
        Assert.Equal("https://www.imdb.com/title/tt0113277/", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public async Task Scan_fills_titles_from_nfo_and_tmdb_ids_prevent_duplicates_and_nfo_is_written_back()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var settings = new SettingsStore(db);
        var nfo = new NfoService(titles, new MetadataRepository(db), files, settings, NullLogger<NfoService>.Instance);
        var service = new LibraryImportService(titles, files, new ImageStore(db, Path.Combine(temp.Path, "img")), null, () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance, null, nfo);

        var library = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;
        var dir = Directory.CreateDirectory(Path.Combine(library, "Heat")).FullName;
        File.WriteAllBytes(Path.Combine(dir, "Heat.mkv"), new byte[2048]);
        File.WriteAllText(Path.Combine(dir, "Heat.nfo"), FullKodi);
        var dir2 = Directory.CreateDirectory(Path.Combine(library, "Heat bonus")).FullName;
        File.WriteAllBytes(Path.Combine(dir2, "Heat CD2.mkv"), new byte[2048]);
        File.WriteAllText(Path.Combine(dir2, "Heat CD2.nfo"), FullKodi);

        var candidates = await service.ScanAsync(library, new ScanOptions { MinimumBytes = 1024 }, TestContext.Current.CancellationToken);
        var result = await service.ImportAsync(candidates, new LibraryImportOptions(), null, TestContext.Current.CancellationToken);

        Assert.Equal((1, 1, 1), (result.WithoutMetadata, result.FromNfo, result.Linked));
        var heat = titles.Get(Assert.Single(titles.ListAll()).Id)!;
        Assert.Equal(949, heat.TmdbId);
        Assert.Equal("tt0113277", heat.ImdbId);
        Assert.Equal(170, heat.RuntimeMinutes);
        Assert.Equal("Un braqueur et un flic.", heat.Overview);
        Assert.Equal(9, heat.UserRating);
        Assert.Equal("Michael Mann", heat.CollectionName);
        Assert.Contains("crime", heat.GenreCodes);
        Assert.Contains("thriller", heat.GenreCodes);
        Assert.Equal(["culte"], heat.Tags);
        Assert.Equal(1, heat.ViewCount);
        Assert.Equal(2, files.FilesOf(heat.Id).Count);
        Assert.Contains(titles.Credits(heat.Id), c => c is ("actor", "Al Pacino", "Vincent Hanna"));

        // Écriture : la fiche est reportée dans le .nfo de chaque dossier, sans perdre l'existant.
        titles.Update(heat with { Tagline = null, Tags = ["culte", "braquage"] });
        Assert.Equal(2, nfo.WriteForTitle(heat.Id));
        var written = NfoReader.Read(Path.Combine(dir, "Heat.nfo"))!;
        Assert.Equal(["culte", "braquage"], written.Tags);
        Assert.Equal("A Los Angeles crime saga", written.Tagline);
        Assert.True(File.Exists(Path.Combine(dir, "Heat.nfo.bak")));
    }
}
