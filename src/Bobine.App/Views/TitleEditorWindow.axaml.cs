using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bobine.App.ViewModels;
using Bobine.Core.People;
using Bobine.Media;

namespace Bobine.App.Views;

public sealed partial class TitleEditorWindow : Window
{
    public TitleEditorWindow() => InitializeComponent();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Ne ferme la fenêtre que si la saisie est valide ; sinon l'erreur s'affiche en bas.
        if (DataContext is TitleEditorViewModel editor && editor.TryBuild(out _))
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>Fiche de l'acteur cliqué ; si l'on y choisit un film, la fiche se ferme et le catalogue s'y place.</summary>
    private async void OnOpenPerson(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TitleEditorViewModel editor || (sender as Control)?.DataContext is not CastMember member ||
            editor.PersonPage(member) is not { } page)
        {
            return;
        }

        if (await new PersonWindow { DataContext = page }.ShowDialog<bool>(this) && page.ChosenTitleId is { } titleId)
        {
            editor.NavigateToTitleId = titleId;
            Close(false);
        }
    }

    /// <summary>Fichier supplémentaire du même film (CD2, part2...).</summary>
    private async void OnAddPart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TitleEditorViewModel editor)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Partie suivante du film",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Fichiers vidéo") { Patterns = FileNameParser.VideoExtensions.Select(x => "*" + x).ToList() },
                FilePickerFileTypes.All,
            ],
        });
        foreach (var path in files.Select(f => f.TryGetLocalPath()).OfType<string>().Order(StringComparer.OrdinalIgnoreCase))
        {
            await editor.AddFileAsync(path);
        }
    }
}
