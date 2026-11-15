using Cinedex.App.ViewModels;
using Cinedex.App.Views;
using Cinedex.Tests;

namespace Cinedex.UiTests;

/// <summary>
/// Le cache d'images tient un budget d'octets. Il comptait des entrées : 600 images quelle que soit leur
/// taille, ce qui laissait l'économiseur d'écran retenir des gigaoctets d'affiches plein écran.
/// </summary>
public sealed class ImageCacheUiTests(UiSession ui)
{
    [Fact]
    public Task The_cache_holds_a_byte_budget_and_keeps_what_was_read_last() => ui.Run(() =>
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "poster.png");
        var files = Enumerable.Range(0, 6).Select(i =>
        {
            var copy = Path.Combine(temp.Path, $"affiche-{i}.png");
            File.Copy(source, copy);
            return copy;
        }).ToList();

        var before = PathToBitmapConverter.Budget;
        try
        {
            PathToBitmapConverter.Clear();
            var first = PathToBitmapConverter.Load(files[0], 400)!;
            var each = (long)first.PixelSize.Width * first.PixelSize.Height * 4;
            Assert.Equal(each, PathToBitmapConverter.CachedBytes);

            // De la place pour trois images, pas plus.
            PathToBitmapConverter.Budget = (each * 3) + 1;
            PathToBitmapConverter.Load(files[1], 400);
            PathToBitmapConverter.Load(files[2], 400);
            Assert.Equal(each * 3, PathToBitmapConverter.CachedBytes);

            // Relire la première la protège : c'est la deuxième, la moins récemment lue, qui sortira.
            Assert.True(PathToBitmapConverter.TryGetCached(files[0], 400, out _));
            PathToBitmapConverter.Load(files[3], 400);

            Assert.Equal(each * 3, PathToBitmapConverter.CachedBytes);
            Assert.True(PathToBitmapConverter.TryGetCached(files[0], 400, out _));
            Assert.False(PathToBitmapConverter.TryGetCached(files[1], 400, out _));
            Assert.True(PathToBitmapConverter.TryGetCached(files[2], 400, out _));
            Assert.True(PathToBitmapConverter.TryGetCached(files[3], 400, out _));

            // La même image à deux largeurs, ce sont deux entrées : la vignette et l'affiche plein écran.
            PathToBitmapConverter.Load(files[4], 100);
            Assert.True(PathToBitmapConverter.TryGetCached(files[4], 100, out var small));
            Assert.False(PathToBitmapConverter.TryGetCached(files[4], 400, out _));
            Assert.True(small!.PixelSize.Width <= 100);

            // Un fichier absent ou illisible ne fait rien tomber.
            Assert.Null(PathToBitmapConverter.Load(Path.Combine(temp.Path, "absent.png"), 400));
            File.WriteAllText(Path.Combine(temp.Path, "faux.png"), "ceci n'est pas une image");
            Assert.Null(PathToBitmapConverter.Load(Path.Combine(temp.Path, "faux.png"), 400));
        }
        finally
        {
            PathToBitmapConverter.Budget = before;
            PathToBitmapConverter.Clear();
        }
    });

    [Fact]
    public void The_screen_saver_names_its_next_slide_before_showing_it()
    {
        var slides = Enumerable.Range(1, 5)
            .Select(i => new ScreenSaverSlide(i, $"Film {i}", 1990 + i, $"affiche-{i}.jpg", null))
            .ToList();
        var vm = new ScreenSaverViewModel(slides, TimeSpan.FromSeconds(5), showTitles: true, new Random(3));

        // Ce que la fenêtre décode d'avance est bien ce qui sera montré, y compris au passage d'un tour au suivant.
        for (var i = 0; i < 12; i++)
        {
            var upcoming = vm.Upcoming;
            var current = vm.Current;
            vm.Next();
            Assert.Same(upcoming, vm.Current);
            Assert.NotSame(current, vm.Current);
        }
    }
}
