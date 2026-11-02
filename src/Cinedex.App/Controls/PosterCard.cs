using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Cinedex.App.Controls;

/// <summary>
/// Carte d'un titre dans la grille d'affiches. La grille est virtualisée et n'utilise pas la sélection d'une liste :
/// l'état sélectionné vient de la vue-modèle et s'affiche par la pseudo-classe <c>:selected</c>.
/// </summary>
public sealed class PosterCard : Border
{
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<PosterCard, bool>(nameof(IsSelected));

    static PosterCard()
    {
        IsSelectedProperty.Changed.AddClassHandler<PosterCard>((card, e) => card.PseudoClasses.Set(":selected", e.GetNewValue<bool>()));
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PosterCardPeer(this);

    /// <summary>Lecteur d'écran : la carte est annoncée comme un élément de liste, avec son titre.</summary>
    private sealed class PosterCardPeer(PosterCard owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
