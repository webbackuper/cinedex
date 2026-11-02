using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Cinedex.App.Views;

namespace Cinedex.App.Controls;

/// <summary>
/// Image locale décodée en arrière-plan, à la largeur d'affichage : faire défiler des milliers d'affiches ne
/// bloque jamais l'interface. Une image déjà en cache s'affiche immédiatement.
/// </summary>
public sealed class AsyncImage : Image
{
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<AsyncImage, string?>(nameof(Path));

    public static readonly StyledProperty<int> DecodeWidthProperty =
        AvaloniaProperty.Register<AsyncImage, int>(nameof(DecodeWidth), 200);

    private int _version;

    static AsyncImage()
    {
        PathProperty.Changed.AddClassHandler<AsyncImage>((image, _) => image.Reload());
        DecodeWidthProperty.Changed.AddClassHandler<AsyncImage>((image, _) => image.Reload());
    }

    protected override Type StyleKeyOverride => typeof(Image);

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>Largeur de décodage en pixels (environ deux fois la largeur affichée, pour les écrans haute densité).</summary>
    public int DecodeWidth
    {
        get => GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    private void Reload()
    {
        var version = ++_version;
        var path = Path;
        var width = DecodeWidth;
        if (path is null)
        {
            Source = null;
            return;
        }

        if (PathToBitmapConverter.TryGetCached(path, width, out var cached))
        {
            Source = cached;
            return;
        }

        // Ligne recyclée par la liste virtualisée : l'ancienne affiche disparaît tout de suite.
        Source = null;
        _ = Task.Run(() => PathToBitmapConverter.Load(path, width)).ContinueWith(
            task => Dispatcher.UIThread.Post(() =>
            {
                if (version == _version && task.IsCompletedSuccessfully)
                {
                    Source = task.Result;
                }
            }),
            TaskScheduler.Default);
    }
}
