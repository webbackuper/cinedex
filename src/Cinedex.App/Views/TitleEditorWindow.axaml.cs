using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cinedex.App.ViewModels;
using Cinedex.Core.People;
using Cinedex.Media;

namespace Cinedex.App.Views;

public sealed partial class TitleEditorWindow : Window
{
    public TitleEditorWindow()
    {
        InitializeComponent();

        // Lire un fichier absent demande de brancher son disque : la question s'ouvre devant la fiche.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TitleEditorViewModel editor)
            {
                editor.Ask = (title, message, answers) => MessageWindow.ChooseAsync(this, title, message, answers);
            }
        };
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Ne ferme la fenêtre que si la saisie est valide ; sinon l'erreur s'affiche en bas.
        if (DataContext is TitleEditorViewModel editor && editor.TryBuild(out _))
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnPrevious(object? sender, RoutedEventArgs e) => Step(-1);

    private void OnNext(object? sender, RoutedEventArgs e) => Step(1);

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        // Ctrl+Page précédente / suivante, et Alt+← / Alt+→ comme dans un navigateur.
        var direction = (e.KeyModifiers, e.Key) switch
        {
            (Avalonia.Input.KeyModifiers.Control, Avalonia.Input.Key.PageDown) => 1,
            (Avalonia.Input.KeyModifiers.Control, Avalonia.Input.Key.PageUp) => -1,
            (Avalonia.Input.KeyModifiers.Alt, Avalonia.Input.Key.Right) => 1,
            (Avalonia.Input.KeyModifiers.Alt, Avalonia.Input.Key.Left) => -1,
            _ => 0,
        };
        if (direction != 0 && DataContext is TitleEditorViewModel)
        {
            e.Handled = true;
            Step(direction);
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// Passe au titre voisin de la vue d'où vient la fiche, dans la même fenêtre : la fiche est enregistrée
    /// si elle a changé, puis la suivante prend sa place (taille, position et onglet restent). Une saisie
    /// invalide retient ici : l'erreur s'affiche en bas, rien n'est perdu.
    /// </summary>
    private void Step(int direction)
    {
        if (DataContext is TitleEditorViewModel editor && editor.Step(direction) is { } next)
        {
            DataContext = next;
        }
    }

    /// <summary>Choisit une image du disque comme affiche du titre.</summary>
    private async void OnChoosePoster(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TitleEditorViewModel editor)
        {
            return;
        }

        var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Affiche du titre",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All],
        });
        if (chosen.Count > 0 && chosen[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            await editor.SetPosterAsync(path);
        }
    }

    /// <summary>
    /// Fiche de l'acteur cliqué ; si l'on y choisit un film, il s'ouvre dans cette fenêtre, et Précédent /
    /// Suivant parcourent alors la filmographie de l'acteur.
    /// </summary>
    private async void OnOpenPerson(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TitleEditorViewModel editor || (sender as Control)?.DataContext is not CastMember member ||
            editor.PersonPage(member) is not { } page)
        {
            return;
        }

        if (await new PersonWindow { DataContext = page }.ShowDialog<bool>(this) && page.ChosenTitleId is { } titleId)
        {
            await GoToAsync(TitleEditorViewModel.ContextOf(page.Name, page.Titles.Select(t => t.TitleId), titleId));
        }
    }

    /// <summary>
    /// Un film de la même collection, ou un film proche : il s'ouvre dans cette fenêtre, et Précédent /
    /// Suivant parcourent la liste d'où il a été choisi.
    /// </summary>
    private async void OnOpenSibling(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TitleEditorViewModel editor && (sender as Control)?.DataContext is CollectionSibling { TitleId: { } id, IsCurrent: false })
        {
            await GoToAsync(editor.CollectionContext(id));
        }
    }

    private async void OnOpenSimilar(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TitleEditorViewModel editor && (sender as Control)?.DataContext is SimilarTitle similar)
        {
            await GoToAsync(editor.SimilarContext(similar.Title.Id));
        }
    }

    /// <summary>
    /// Une suggestion TMDb : celle qu'on possède s'ouvre, celle qu'on n'a pas part dans la liste de souhaits.
    /// La fenêtre principale pose la question et appelle TMDb, puisque c'est elle qui en tient les moyens.
    /// </summary>
    private async void OnOpenSuggestion(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TitleEditorViewModel editor || (sender as Control)?.DataContext is not Cinedex.Data.TmdbSuggestion suggestion)
        {
            return;
        }

        if (suggestion.OwnedTitleId is { } owned)
        {
            await GoToAsync(editor.SuggestionsContext(owned));
            return;
        }

        await editor.AddSuggestionAsync(suggestion);
    }

    /// <summary>
    /// Ouvre un autre titre dans cette fenêtre. Si la fiche a des modifications en cours, on demande avant :
    /// les perdre sans un mot en cliquant un film de la collection était le piège de cette navigation.
    /// </summary>
    private async Task GoToAsync(TitleNavigation navigation)
    {
        if (DataContext is not TitleEditorViewModel editor)
        {
            return;
        }

        var save = false;
        if (editor.HasUnsavedChanges)
        {
            var answer = await MessageWindow.ChooseAsync(
                this,
                "Modifications non enregistrées",
                "Cette fiche a été modifiée. Voulez-vous enregistrer avant d'ouvrir l'autre titre ?",
                "Enregistrer",
                "Abandonner les modifications",
                "Rester sur cette fiche");
            if (answer is not (0 or 1))
            {
                return;
            }

            save = answer == 0;
            if (save && !editor.TryBuild(out _))
            {
                // Saisie invalide : l'erreur s'affiche en bas, et l'on reste pour la corriger.
                return;
            }
        }

        if (editor.MoveTo(navigation, save) is { } next)
        {
            DataContext = next;
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
