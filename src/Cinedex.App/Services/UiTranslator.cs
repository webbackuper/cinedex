using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Cinedex.Core.Localization;

namespace Cinedex.App.Services;

/// <summary>
/// Traduit l'interface au vol : chaque texte affiché (TextBlock, bouton, case, onglet, menu, titre de fenêtre,
/// texte indicatif, info-bulle) qui a une traduction dans la langue choisie est remplacé au moment où il est posé.
/// Les fenêtres restent écrites en français ; les textes calculés en C# passent par <see cref="Tr"/>.
/// <c>SetCurrentValue</c> garde les liaisons de données en place.
/// </summary>
public static class UiTranslator
{
    private static bool _installed;

    public static void Install(Application? app)
    {

        if (Tr.Current.Code != Tr.SourceLanguage && !_installed)
        {
            _installed = true;
            Watch<TextBlock>(TextBlock.TextProperty);
            Watch<ContentControl>(ContentControl.ContentProperty);
            Watch<HeaderedContentControl>(HeaderedContentControl.HeaderProperty);
            Watch<HeaderedItemsControl>(HeaderedItemsControl.HeaderProperty);
            Watch<HeaderedSelectingItemsControl>(HeaderedSelectingItemsControl.HeaderProperty);
            Watch<Window>(Window.TitleProperty);
            Watch<TextBox>(TextBox.PlaceholderTextProperty);
            Watch<AutoCompleteBox>(AutoCompleteBox.PlaceholderTextProperty);
            Watch<Control>(ToolTip.TipProperty);
        }

        if (Tr.IsRightToLeft && app is not null)
        {
            // Arabe : toutes les fenêtres en écriture de droite à gauche.
            app.Styles.Add(new Style(x => x.OfType<Window>())
            {
                Setters = { new Setter(Visual.FlowDirectionProperty, FlowDirection.RightToLeft) },
            });
        }
    }

    private static void Watch<T>(AvaloniaProperty property)
        where T : AvaloniaObject =>
        property.Changed.AddClassHandler<T>((control, e) =>
        {
            if (e.NewValue is string text && Tr.TryTranslate(text, out var translated))
            {
                control.SetCurrentValue(property, translated);
            }
        });
}
