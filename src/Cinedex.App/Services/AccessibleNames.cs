using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace Cinedex.App.Services;

/// <summary>
/// Noms lus par les lecteurs d'écran (Narrateur, NVDA, VoiceOver) pour les boutons dont le contenu n'est pas un
/// simple texte (icône seule, icône et libellé) : sans cela, ils annoncent le type du contrôle (« StackPanel »).
/// Le nom est le texte visible du bouton, sinon son infobulle ; l'infobulle devient l'aide du bouton.
/// </summary>
public static class AccessibleNames
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Control.LoadedEvent.AddClassHandler<Button>((button, _) => Name(button), RoutingStrategies.Direct);
    }

    /// <summary>Donne un nom au bouton s'il n'en a pas (appelé à son affichage ; texte déjà traduit).</summary>
    public static void Name(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) || button.Content is string)
        {
            return;
        }

        // Flèches des champs numériques (modèle du contrôle) : « Augmenter » et « Diminuer ».
        var spin = button.Name switch
        {
            "PART_IncreaseButton" => Cinedex.Core.Localization.Tr.T("Augmenter"),
            "PART_DecreaseButton" => Cinedex.Core.Localization.Tr.T("Diminuer"),
            _ => null,
        };
        if (spin is not null)
        {
            button.SetCurrentValue(AutomationProperties.NameProperty, spin);
            return;
        }

        var tip = ToolTip.GetTip(button) as string;
        var text = string.Join(" ", button.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text!.Trim()));
        var name = text.Length > 0 ? text : tip;
        if (!string.IsNullOrWhiteSpace(name))
        {
            button.SetCurrentValue(AutomationProperties.NameProperty, name);
        }

        if (text.Length > 0 && !string.IsNullOrWhiteSpace(tip) && string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(button)))
        {
            button.SetCurrentValue(AutomationProperties.HelpTextProperty, tip);
        }
    }
}
