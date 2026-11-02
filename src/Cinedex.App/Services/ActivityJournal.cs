using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cinedex.App.Services;

/// <summary>
/// Journal des actions faites dans Cinedex (ajouts, modifications, suppressions, prêts, imports, mises à jour…),
/// distinct du journal technique. Un fichier texte par jour dans logs/journal-AAAA-MM-JJ.log, conservé 90 jours.
/// Activé par défaut ; le choix est mémorisé dans journal.json (commun à tous les catalogues).
/// Les lignes sont écrites en français, avec la date, l'heure et le catalogue concerné.
/// </summary>
public sealed class ActivityJournal
{
    private const string SettingsFile = "journal.json";
    private const int RetentionDays = 90;
    private readonly Lock _gate = new();
    private readonly string? _dataRoot;

    private ActivityJournal(string? dataRoot, string logsDirectory, bool enabled)
    {
        _dataRoot = dataRoot;
        LogsDirectory = logsDirectory;
        Enabled = enabled;
    }

    /// <summary>Journal de l'application (désactivé tant que <see cref="Configure"/> n'a pas été appelé, par exemple en test).</summary>
    public static ActivityJournal Current { get; private set; } = new(null, Path.GetTempPath(), enabled: false);

    public string LogsDirectory { get; }

    public bool Enabled { get; private set; }

    /// <summary>Catalogue ouvert, indiqué sur chaque ligne.</summary>
    public string? Catalog { get; set; }

    public string TodayPath => PathFor(DateTime.Now);

    public static ActivityJournal Configure(string dataRoot, string logsDirectory)
    {
        Current = new ActivityJournal(dataRoot, logsDirectory, LoadEnabled(dataRoot));
        Current.Purge();
        return Current;
    }

    /// <summary>Active ou désactive le journal ; le changement lui-même est noté.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled == Enabled)
        {
            return;
        }

        if (!enabled)
        {
            Write("Journal des actions désactivé");
        }

        Enabled = enabled;
        if (_dataRoot is not null)
        {
            Directory.CreateDirectory(_dataRoot);
            File.WriteAllText(Path.Combine(_dataRoot, SettingsFile), JsonSerializer.Serialize(new State(enabled)));
        }

        if (enabled)
        {
            Write("Journal des actions activé");
        }
    }

    public void Write(string message)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{Catalog ?? "-"}]  {message.ReplaceLineEndings(" ")}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(LogsDirectory);
                File.AppendAllText(TodayPath, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Le journal ne doit jamais empêcher une action : une ligne perdue est acceptable.
            }
        }
    }

    /// <summary>Copie le journal du jour vers <paramref name="destination"/> ; renvoie le nombre de lignes exportées.</summary>
    public int Export(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        lock (_gate)
        {
            var lines = File.Exists(TodayPath) ? File.ReadAllLines(TodayPath, Encoding.UTF8) : [];
            File.WriteAllLines(destination, lines, Encoding.UTF8);
            return lines.Length;
        }
    }

    private string PathFor(DateTime day) => Path.Combine(LogsDirectory, $"journal-{day:yyyy-MM-dd}.log");

    private void Purge()
    {
        try
        {
            if (!Directory.Exists(LogsDirectory))
            {
                return;
            }

            var limit = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(LogsDirectory, "journal-*.log").Where(f => File.GetLastWriteTime(f) < limit))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nettoyage au prochain lancement.
        }
    }

    private static bool LoadEnabled(string dataRoot)
    {
        try
        {
            var path = Path.Combine(dataRoot, SettingsFile);
            return !File.Exists(path) || JsonSerializer.Deserialize<State>(File.ReadAllText(path))?.Enabled != false;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private sealed record State(bool Enabled);
}
