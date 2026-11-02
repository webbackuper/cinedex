namespace Cinedex.App.Services;

/// <summary>
/// Suit la dernière action au clavier ou à la souris et décide quand lancer l'économiseur d'écran :
/// réglage activé, fenêtre active, inactivité atteinte, économiseur pas déjà affiché.
/// </summary>
public sealed class IdleMonitor(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTimeOffset _lastInput = (clock ?? TimeProvider.System).GetUtcNow();

    /// <summary>Économiseur à l'écran : pas de second lancement tant qu'il n'est pas fermé.</summary>
    public bool IsShowing { get; set; }

    public TimeSpan Idle => _clock.GetUtcNow() - _lastInput;

    /// <summary>Une action de l'utilisateur (ou la fenêtre qui redevient active) remet le compteur à zéro.</summary>
    public void Touch() => _lastInput = _clock.GetUtcNow();

    public bool ShouldStart(ScreenSaverSettings settings, bool windowActive)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Enabled && windowActive && !IsShowing && Idle >= TimeSpan.FromMinutes(settings.IdleMinutes);
    }
}
