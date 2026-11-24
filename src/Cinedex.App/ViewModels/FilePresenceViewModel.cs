using Cinedex.Core.Localization;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cinedex.App.ViewModels;

/// <summary>État de la pastille de présence, du plus neutre au plus grave.</summary>
public enum PresenceState
{
    /// <summary>Pas de pastille : aucun titre, ou un titre sans fichier (liste de souhaits).</summary>
    None,

    /// <summary>Vérification en cours.</summary>
    Checking,

    /// <summary>Au moins un fichier du titre est là, à son chemin enregistré.</summary>
    Present,

    /// <summary>Aucun fichier trouvé.</summary>
    Absent,

    /// <summary>Aucun fichier trouvé, et au moins un disque n'a pas répondu à temps (lecteur réseau déconnecté).</summary>
    TimedOut,
}

/// <summary>Ce qu'on sait d'un fichier après la vérification : là, absent, ou disque muet au-delà du délai.</summary>
public enum FileCheck
{
    Present,
    Absent,
    TimedOut,
}

/// <summary>Un fichier vérifié : son entrée au catalogue et ce qu'on a constaté.</summary>
public sealed record FilePresence(MediaFileEntry File, FileCheck Check);

/// <summary>
/// Pastille « Présent / Absent » à côté du bouton Lire, dans le bandeau droit et dans la fiche. Elle regarde
/// le disque en direct, au moment où le titre s'affiche, plutôt que de croire le dernier scan.
/// <para>
/// Le test se fait hors du fil d'interface et chaque fichier a un délai maximal : un lecteur réseau déconnecté
/// peut faire attendre <see cref="File.Exists(string)"/> vingt secondes et plus, et l'interface ne doit
/// jamais geler pour une pastille. Un résultat arrivé après un changement de titre est jeté : la pastille ne
/// montre jamais l'état d'un autre film. Ce qui a été constaté est écrit en base (voir
/// <see cref="MediaFileRepository.RecordPresence"/>), pour que le filtre « Fichiers manquants » et le
/// « (introuvable) » du résumé technique suivent.
/// </para>
/// </summary>
public sealed partial class FilePresenceViewModel : ObservableObject
{
    /// <summary>Délai par fichier au-delà duquel on renonce : le disque est considéré comme absent.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<IReadOnlyList<(long Id, bool Exists)>, int>? _record;
    private readonly Func<string, bool> _exists;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _delay;
    private int _generation;

    /// <param name="record">Écrit le résultat en base ; renvoie le nombre de fichiers dont l'état a changé.</param>
    /// <param name="exists">Test de présence d'un chemin ; <see cref="File.Exists(string)"/> par défaut (les tests y mettent le leur).</param>
    /// <param name="timeout">Délai maximal par fichier.</param>
    /// <param name="delay">Attente avant de vérifier : tenir la flèche bas dans la liste ne lance pas un test par titre.</param>
    public FilePresenceViewModel(
        Func<IReadOnlyList<(long Id, bool Exists)>, int>? record = null,
        Func<string, bool>? exists = null,
        TimeSpan? timeout = null,
        TimeSpan? delay = null)
    {
        _record = record;
        _exists = exists ?? File.Exists;
        _timeout = timeout ?? DefaultTimeout;
        _delay = delay ?? TimeSpan.Zero;
    }

    /// <summary>Titre dont la pastille parle ; nul quand il n'y en a pas.</summary>
    public long? TitleId { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsChecking), nameof(IsPresent), nameof(IsAbsent), nameof(Text))]
    public partial PresenceState State { get; private set; }

    /// <summary>Fichiers vérifiés, chacun avec son état (infobulle).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(ToolTip))]
    public partial IReadOnlyList<FilePresence> Results { get; private set; } = [];

    public bool IsVisible => State != PresenceState.None;

    public bool IsChecking => State == PresenceState.Checking;

    public bool IsPresent => State == PresenceState.Present;

    public bool IsAbsent => State is PresenceState.Absent or PresenceState.TimedOut;

    /// <summary>MediaID (nom du disque) à brancher : celui du premier fichier absent qui en a un.</summary>
    private string? _mediaId;

    public string Text => State switch
    {
        PresenceState.Checking => Tr.T("Vérification…"),
        PresenceState.Present => Tr.T("Présent"),
        PresenceState.TimedOut => Tr.T("Absent (délai dépassé)"),
        PresenceState.Absent when _mediaId is { } label => Tr.F("Absent – branchez « {0} »", label),
        PresenceState.Absent => Tr.T("Absent"),
        _ => string.Empty,
    };

    public string? ToolTip => State switch
    {
        PresenceState.None => null,
        PresenceState.Checking => Tr.T("Vérification de la présence des fichiers…"),
        _ => string.Join('\n', Results.Select(r => r.Check switch
        {
            FileCheck.Present => Tr.F("Présent : {0}", r.File.FullPath),
            FileCheck.TimedOut => Tr.F("Délai dépassé : {0}", r.File.FullPath),
            _ => Tr.F("Absent : {0}", r.File.FullPath),
        })),
    };

    /// <summary>Levé après une vérification menée à son terme, avec le nombre de fichiers dont l'état a changé en base.</summary>
    public event EventHandler<int>? Checked;

    /// <summary>Plus de pastille (sélection vide ou multiple), et toute vérification en cours est oubliée.</summary>
    public void Clear() => _ = CheckAsync(null, []);

    /// <summary>
    /// Vérifie les fichiers de ce titre. Une nouvelle demande annule la précédente : son résultat, s'il arrive
    /// quand même (un <see cref="File.Exists(string)"/> ne s'interrompt pas), est ignoré à l'affichage.
    /// </summary>
    public async Task CheckAsync(long? titleId, IReadOnlyList<MediaFileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var generation = ++_generation;
        TitleId = titleId;
        _mediaId = null;
        Results = [];
        if (titleId is null || files.Count == 0)
        {
            State = PresenceState.None;
            return;
        }

        State = PresenceState.Checking;
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay).ConfigureAwait(true);
            if (generation != _generation)
            {
                // Une autre demande est arrivée pendant l'attente : celle-ci n'a plus lieu d'être.
                return;
            }
        }

        IReadOnlyList<FilePresence> results = await Task.WhenAll(files.Select(CheckOneAsync)).ConfigureAwait(true);

        // Le disque a parlé : on l'écrit, même si l'on regarde déjà un autre titre. Un fichier muet au-delà du
        // délai n'est pas écrit : on ne sait pas s'il est là, seulement que son disque ne répond pas.
        var changed = 0;
        var known = results.Where(r => r.Check != FileCheck.TimedOut).Select(r => (r.File.Id, r.Check == FileCheck.Present)).ToList();
        if (_record is { } record && known.Count > 0)
        {
            try
            {
                changed = await Task.Run(() => record(known)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
            {
                // Base occupée ou en lecture seule : la pastille reste juste, seule la mémoire du constat manque.
            }
        }

        if (generation != _generation)
        {
            return;
        }

        _mediaId = results.Where(r => r.Check != FileCheck.Present).Select(r => r.File.VolumeLabel).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        Results = results;
        State = results.Any(r => r.Check == FileCheck.Present) ? PresenceState.Present
            : results.Any(r => r.Check == FileCheck.TimedOut) ? PresenceState.TimedOut
            : PresenceState.Absent;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(ToolTip));
        Checked?.Invoke(this, changed);
    }

    /// <summary>Un fichier : test sur un fil à part, abandonné au-delà du délai (le fil finira seul).</summary>
    private async Task<FilePresence> CheckOneAsync(MediaFileEntry file)
    {
        var test = Task.Run(() =>
        {
            try
            {
                return _exists(file.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        });
        try
        {
            var exists = await test.WaitAsync(_timeout).ConfigureAwait(false);
            return new FilePresence(file, exists ? FileCheck.Present : FileCheck.Absent);
        }
        catch (TimeoutException)
        {
            return new FilePresence(file, FileCheck.TimedOut);
        }
    }
}
