using Cinedex.Media;

namespace Cinedex.Tests;

public sealed class MediaTests
{
    [Theory]
    [InlineData("Heat.1995.1080p.BluRay.x264-GRP.mkv", "Heat", 1995, "1080p")]
    [InlineData("Heat (1995).mkv", "Heat", 1995, null)]
    [InlineData("Blade Runner 2049 (2017) [2160p].mkv", "Blade Runner 2049", 2017, "2160p")]
    [InlineData("2001 l'odyssée de l'espace 1968.avi", "2001 l'odyssée de l'espace", 1968, null)]
    [InlineData("Le.Fabuleux.Destin.d'Amelie.Poulain.FRENCH.DVDRip.avi", "Le Fabuleux Destin d'Amelie Poulain", null, null)]
    [InlineData("The_Thing_1982_720p.mp4", "The Thing", 1982, "720p")]
    public void Movie_names_are_parsed(string file, string title, int? year, string? resolution)
    {
        var parsed = FileNameParser.Parse(file);

        Assert.Equal(title, parsed.Title);
        Assert.Equal(year, parsed.Year);
        Assert.Equal(resolution, parsed.Resolution);
        Assert.False(parsed.IsEpisode);
    }

    [Theory]
    [InlineData("Breaking.Bad.S01E02.720p.mkv", "Breaking Bad", 1, 2)]
    [InlineData("Dark - 2x05 - Truths.mkv", "Dark", 2, 5)]
    [InlineData("The Office S09E23.mp4", "The Office", 9, 23)]
    public void Episode_names_follow_kodi_conventions(string file, string title, int season, int episode)
    {
        var parsed = FileNameParser.Parse(file);

        Assert.Equal(title, parsed.Title);
        Assert.Equal(season, parsed.Season);
        Assert.Equal(episode, parsed.Episode);
    }

    [Theory]
    [InlineData("Heat (1995) [Director's Cut] 1080p.mkv", "Heat", 1995)]
    [InlineData("Heat (Version longue) (1995).mkv", "Heat", 1995)]
    [InlineData("[Team] Akira (1988) [BD 1080p][x265].mkv", "Akira", 1988)]
    [InlineData("Le Samouraï (Restauré [4K]) 1967.mkv", "Le Samouraï", 1967)]
    [InlineData("La Haine (VF.mkv", "La Haine", null)]
    [InlineData("17 fois Cécile Cassard (Christophe Honoré - 2002).mp4", "17 fois Cécile Cassard", 2002)]
    [InlineData("37°2 Le Matin (DC-1080p) de Jean-Jacques Beineix - 1985.mkv", "37°2 Le Matin de Jean-Jacques Beineix", 1985)]
    public void Bracketed_parts_are_ignored_by_default_but_the_year_is_kept(string file, string title, int? year)
    {
        var parsed = FileNameParser.Parse(file);

        Assert.Equal(title, parsed.Title);
        Assert.Equal(year, parsed.Year);
    }

    [Theory]
    [InlineData("Heat.1080p.TeleSync.x265.EAC3.mkv", "Heat")]
    [InlineData("Alien Romulus WEB-DL DDP5.1 H.264-GRP.mkv", "Alien Romulus")]
    [InlineData("Le Fabuleux Destin TRUEFRENCH DTS-HD MA 7.1 HEVC.mkv", "Le Fabuleux Destin")]
    [InlineData("Ma vie de Courgette MULTi 10bit.mkv", "Ma vie de Courgette")]
    [InlineData("It.2017.HDR10.Dolby.Vision.mkv", "It")]
    [InlineData("En attendant Bojangles.mkv", "En attendant Bojangles")]
    public void Release_terms_are_removed_without_eating_title_words(string file, string title)
    {
        Assert.Equal(title, FileNameParser.Parse(file).Title);
    }

    [Fact]
    public void Noise_list_can_be_replaced_by_the_user()
    {
        Assert.Equal("Heat", FileNameParser.Parse("Heat Edition Ultime.mkv", ["Edition Ultime"]).Title);
        Assert.Equal("Heat TeleSync", FileNameParser.Parse("Heat TeleSync.mkv", []).Title);
    }

    [Fact]
    public void Bracketed_parts_are_kept_when_the_option_is_off()
    {
        var parsed = FileNameParser.Parse("Heat (Version longue) [Remaster] (1995).mkv", ignoreBracketed: false);

        Assert.Equal("Heat Version longue Remaster", parsed.Title);
        Assert.Equal(1995, parsed.Year);
    }

    [Fact]
    public void Parts_and_imdb_ids_are_detected()
    {
        var parsed = FileNameParser.Parse("Lawrence of Arabia (1962) CD2 tt0056172.avi");

        Assert.Equal("Lawrence of Arabia", parsed.Title);
        Assert.Equal(2, parsed.Part);
        Assert.Equal("tt0056172", parsed.ImdbId);
    }

    [Fact]
    public void Nfo_files_are_used_only_when_asked()
    {
        using var temp = new TempDirectory();
        var video = Path.Combine(temp.Path, "Heat.mkv");
        File.WriteAllBytes(video, new byte[10]);
        File.WriteAllText(Path.Combine(temp.Path, "Heat.nfo"), "<movie><title>Heat (version longue)</title><year>1995</year><tmdbid>949</tmdbid></movie>");

        var withNfo = Assert.Single(LibraryScanner.Scan(temp.Path, new ScanOptions { MinimumBytes = 0 }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((949L, "Heat (version longue)"), (withNfo.Nfo!.TmdbId!.Value, withNfo.BestTitle));

        var withoutNfo = Assert.Single(LibraryScanner.Scan(temp.Path, new ScanOptions { MinimumBytes = 0, ReadNfo = false }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(withoutNfo.Nfo);
        Assert.Equal("Heat", withoutNfo.BestTitle);
    }

    [Fact]
    public void Kodi_nfo_is_read()
    {
        var info = NfoReader.ReadXml("""
            <movie><title>Heat</title><year>1995</year>
            <uniqueid type="imdb">tt0113277</uniqueid><uniqueid type="tmdb">949</uniqueid></movie>
            """);

        Assert.Equal(("Heat", 1995, "tt0113277", 949L), (info!.Title, info.Year, info.ImdbId, info.TmdbId));
    }

    [Fact]
    public void Nfo_with_external_entities_is_refused()
    {
        var info = NfoReader.ReadXml("""
            <?xml version="1.0"?>
            <!DOCTYPE movie [<!ENTITY xxe SYSTEM "file:///c:/windows/win.ini">]>
            <movie><title>&xxe;</title></movie>
            """);

        Assert.Null(info);
    }

    [Fact]
    public void Plain_text_nfo_with_links_is_read()
    {
        var info = NfoReader.ReadText("Release info\nhttps://www.imdb.com/title/tt0113277/\nhttps://www.themoviedb.org/movie/949-heat");

        Assert.Equal("tt0113277", info!.ImdbId);
        Assert.Equal(949, info.TmdbId);
    }

    [Fact]
    public void Scanner_finds_videos_skips_samples_small_and_known_files()
    {
        using var temp = new TempDirectory();
        var films = Directory.CreateDirectory(Path.Combine(temp.Path, "Films", "Heat (1995)")).FullName;
        Write(Path.Combine(films, "movie.mkv"), 2048);
        Write(Path.Combine(films, "sample.mkv"), 2048);
        File.WriteAllText(Path.Combine(films, "movie.nfo"), "https://www.imdb.com/title/tt0113277/");
        File.WriteAllBytes(Path.Combine(films, "folder.jpg"), [0xFF, 0xD8, 0xFF]);
        var other = Directory.CreateDirectory(Path.Combine(temp.Path, "Films", "Divers")).FullName;
        Write(Path.Combine(other, "Alien.1979.mkv"), 2048);
        Write(Path.Combine(other, "Tiny.2000.mkv"), 10);
        Write(Path.Combine(other, "notes.txt"), 4096);
        var dvd = Directory.CreateDirectory(Path.Combine(temp.Path, "Films", "Le Samourai 1967", "VIDEO_TS")).FullName;
        Write(Path.Combine(dvd, "VIDEO_TS.IFO"), 1);
        Write(Path.Combine(dvd, "VTS_01_1.VOB"), 4096);

        var options = new ScanOptions { MinimumBytes = 1024 };
        var all = LibraryScanner.Scan(temp.Path, options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, all.Count);
        var heat = all.Single(c => c.FullPath.EndsWith("movie.mkv", StringComparison.Ordinal));
        Assert.Equal("Heat", heat.BestTitle);
        Assert.Equal(1995, heat.BestYear);
        Assert.Equal("tt0113277", heat.ImdbId);
        Assert.EndsWith("folder.jpg", heat.LocalCoverPath, StringComparison.Ordinal);
        Assert.Contains(all, c => c.BestTitle == "Le Samourai" && c.BestYear == 1967);

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { heat.FullPath };
        Assert.Equal(2, LibraryScanner.Scan(temp.Path, options, known, TestContext.Current.CancellationToken).Count);
    }

    [Fact]
    public void Mediainfo_output_is_parsed()
    {
        var props = MediaProbe.ParseMediaInfo("""
            {"creatingLibrary":{"name":"MediaInfoLib","version":"26.10"},
             "media":{"@ref":"C:\\Films\\Heat.mkv","track":[
               {"@type":"General","Format":"Matroska","Duration":"10234.560"},
               {"@type":"Video","Format":"HEVC","Width":"3840","Height":"2160","FrameRate":"23.976","BitDepth":"10",
                "DisplayAspectRatio":"2.400","HDR_Format":"Dolby Vision / SMPTE ST 2086","HDR_Format_Compatibility":"HDR10"},
               {"@type":"Audio","Format":"MLP FBA","Format_Commercial_IfAny":"Dolby TrueHD with Dolby Atmos","Channels":"8","Language":"en"},
               {"@type":"Audio","Format":"AC-3","Channels":"6","Language":"fr"},
               {"@type":"Text","Language":"fr"},
               {"@type":"Text","Language":"fr"}]}}
            """)!;

        Assert.Equal("Matroska", props.Container);
        Assert.Equal("HEVC", props.VideoCodec);
        Assert.Equal((3840, 2160), (props.Width, props.Height));
        Assert.Equal(23.976, props.FrameRate);
        Assert.Equal(10, props.BitDepth);
        Assert.Equal("2.40:1", props.AspectRatio);
        Assert.Equal(["HDR10", "Dolby Vision"], props.HdrFormats);
        Assert.Equal(10235, props.DurationSeconds);
        Assert.Equal("Dolby TrueHD with Dolby Atmos", props.Audio[0].Codec);
        Assert.Equal(["fr"], props.SubtitleLanguages);
        Assert.Null(MediaProbe.ParseMediaInfo("""{"media":null}"""));
        Assert.Null(MediaProbe.ParseMediaInfo("pas du json"));
    }

    [Fact]
    public async Task Bundled_mediainfo_is_found_and_run_when_present()
    {
        var bundled = Path.Combine(Path.GetTempPath(), "mi-test", "tools", "MediaInfo", "MediaInfo.exe");
        Assert.SkipUnless(OperatingSystem.IsWindows() && File.Exists(bundled), "MediaInfo non installé par build/fetch-mediainfo.ps1 sur ce poste.");
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "vide.mkv");
        File.WriteAllBytes(file, [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01]);

        var probe = new MediaProbe(mediaInfoPath: bundled);
        var props = await probe.ProbeAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal("MediaInfo", probe.ToolName);
        Assert.NotNull(props); // Fichier factice : MediaInfo répond en JSON sans reconnaître de format.
    }

    [Fact]
    public void Ffprobe_output_is_parsed()
    {
        var props = MediaProbe.Parse("""
            {"streams":[
              {"codec_type":"video","codec_name":"hevc","width":3840,"height":2160,"avg_frame_rate":"24000/1001",
               "pix_fmt":"yuv420p10le","color_transfer":"smpte2084","display_aspect_ratio":"16:9",
               "side_data_list":[{"side_data_type":"DOVI configuration record"}]},
              {"codec_type":"video","codec_name":"mjpeg","disposition":{"attached_pic":1}},
              {"codec_type":"audio","codec_name":"truehd","channels":8,"tags":{"language":"eng"}},
              {"codec_type":"audio","codec_name":"ac3","channels":6,"tags":{"language":"fre"}},
              {"codec_type":"subtitle","tags":{"language":"fre"}},
              {"codec_type":"subtitle","tags":{"language":"und"}}],
             "format":{"format_name":"matroska,webm","duration":"10234.56"}}
            """)!;

        Assert.Equal("matroska", props.Container);
        Assert.Equal("hevc", props.VideoCodec);
        Assert.Equal(2160, props.Height);
        Assert.Equal(23.976, props.FrameRate);
        Assert.Equal(10, props.BitDepth);
        Assert.Equal(["HDR10", "Dolby Vision"], props.HdrFormats);
        Assert.Equal(10235, props.DurationSeconds);
        Assert.Equal(2, props.Audio.Count);
        Assert.Equal(["fre"], props.SubtitleLanguages);
    }

    [Theory]
    [InlineData(null, new[] { "C:\\Films\\Mon film.mkv" })]
    [InlineData("--fullscreen %FILEPATH%", new[] { "--fullscreen", "C:\\Films\\Mon film.mkv" })]
    [InlineData("--title \"Mon titre\"", new[] { "--title", "Mon titre", "C:\\Films\\Mon film.mkv" })]
    [InlineData("--x=%FILEPATH% & calc", new[] { "--x=C:\\Films\\Mon film.mkv", "&", "calc" })]
    public void Player_arguments_keep_the_path_whole(string? parameters, string[] expected)
    {
        Assert.Equal(expected, Player.BuildArguments(parameters, "C:\\Films\\Mon film.mkv"));
    }

    [Fact]
    public void Player_refuses_non_video_files()
    {
        using var temp = new TempDirectory();
        var script = Path.Combine(temp.Path, "evil.bat");
        File.WriteAllText(script, "echo");

        Assert.Throws<InvalidOperationException>(() => Player.Play(script));
        Assert.Throws<FileNotFoundException>(() => Player.Play(Path.Combine(temp.Path, "absent.mkv")));
    }

    private static void Write(string path, int bytes) => File.WriteAllBytes(path, new byte[bytes]);
}
