using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Bobine.Media;

public sealed record AudioTrackInfo(string? Codec, int? Channels, string? Language);

public sealed record MediaProperties
{
    public string? Container { get; init; }

    public string? VideoCodec { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public int? BitDepth { get; init; }

    public string? AspectRatio { get; init; }

    public IReadOnlyList<string> HdrFormats { get; init; } = [];

    public int? DurationSeconds { get; init; }

    public IReadOnlyList<AudioTrackInfo> Audio { get; init; } = [];

    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];
}

/// <summary>
/// Lit les propriétés techniques d'un fichier avec ffprobe (FFmpeg). Exécuté sans shell, chemin passé en
/// argument unique, délai et taille de sortie bornés. ffprobe est cherché dans le réglage puis dans le PATH.
/// </summary>
public sealed class MediaProbe(string? ffprobePath = null)
{
    private const int MaxOutputChars = 4 * 1024 * 1024;

    public string? ExecutablePath { get; } = Resolve(ffprobePath);

    public bool IsAvailable => ExecutablePath is not null;

    public async Task<MediaProperties?> ProbeAsync(string file, CancellationToken cancellationToken = default)
    {
        if (ExecutablePath is null || !File.Exists(file))
        {
            return null;
        }

        var info = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", "--", file })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info);
        if (process is null)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var outputTask = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            return process.ExitCode == 0 ? Parse(output) : null;
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    internal static MediaProperties? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var streams = root.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
            var video = streams.FirstOrDefault(x => Str(x, "codec_type") == "video" && !(x.TryGetProperty("disposition", out var d) && Int(d, "attached_pic") == 1));
            var hdr = new List<string>();
            if (video.ValueKind == JsonValueKind.Object)
            {
                var transfer = Str(video, "color_transfer");
                if (transfer == "smpte2084")
                {
                    hdr.Add("HDR10");
                }
                else if (transfer == "arib-std-b67")
                {
                    hdr.Add("HLG");
                }

                if (video.TryGetProperty("side_data_list", out var side))
                {
                    foreach (var item in side.EnumerateArray())
                    {
                        var type = Str(item, "side_data_type") ?? string.Empty;
                        if (type.Contains("DOVI", StringComparison.OrdinalIgnoreCase))
                        {
                            hdr.Add("Dolby Vision");
                        }
                        else if (type.Contains("HDR Dynamic Metadata", StringComparison.OrdinalIgnoreCase))
                        {
                            hdr.Add("HDR10+");
                        }
                    }
                }
            }

            var format = root.TryGetProperty("format", out var f) ? f : default;
            var duration = format.ValueKind == JsonValueKind.Object && double.TryParse(Str(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)
                ? (int?)Math.Round(dur)
                : null;

            return new MediaProperties
            {
                Container = format.ValueKind == JsonValueKind.Object ? Str(format, "format_name")?.Split(',')[0] : null,
                VideoCodec = video.ValueKind == JsonValueKind.Object ? Str(video, "codec_name") : null,
                Width = video.ValueKind == JsonValueKind.Object ? Int(video, "width") : null,
                Height = video.ValueKind == JsonValueKind.Object ? Int(video, "height") : null,
                FrameRate = video.ValueKind == JsonValueKind.Object ? Rate(Str(video, "avg_frame_rate") ?? Str(video, "r_frame_rate")) : null,
                BitDepth = video.ValueKind == JsonValueKind.Object ? BitDepthOf(video) : null,
                AspectRatio = video.ValueKind == JsonValueKind.Object ? Str(video, "display_aspect_ratio") : null,
                HdrFormats = hdr.Distinct().ToList(),
                DurationSeconds = duration,
                Audio = streams.Where(x => Str(x, "codec_type") == "audio")
                    .Select(x => new AudioTrackInfo(Str(x, "codec_name"), Int(x, "channels"), Language(x)))
                    .ToList(),
                SubtitleLanguages = streams.Where(x => Str(x, "codec_type") == "subtitle")
                    .Select(Language).OfType<string>().Distinct().ToList(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        var builder = new System.Text.StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (builder.Length + read > MaxOutputChars)
            {
                throw new InvalidDataException("Sortie de ffprobe trop volumineuse.");
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    private static string? Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim('"'), name))
            .FirstOrDefault(File.Exists);
    }

    private static string? Language(JsonElement stream) =>
        stream.TryGetProperty("tags", out var tags) ? Str(tags, "language") is { } l && l != "und" ? l : null : null;

    private static int? BitDepthOf(JsonElement video)
    {
        if (int.TryParse(Str(video, "bits_per_raw_sample"), NumberStyles.None, CultureInfo.InvariantCulture, out var bits))
        {
            return bits;
        }

        var pix = Str(video, "pix_fmt") ?? string.Empty;
        return pix.Contains("10", StringComparison.Ordinal) ? 10 : pix.Contains("12", StringComparison.Ordinal) ? 12 : pix.Length > 0 ? 8 : null;
    }

    private static double? Rate(string? fraction)
    {
        var parts = fraction?.Split('/');
        if (parts is { Length: 2 } &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 && n > 0)
        {
            return Math.Round(n / d, 3);
        }

        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}
