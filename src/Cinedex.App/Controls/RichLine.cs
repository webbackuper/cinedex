using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Cinedex.App.Controls;

/// <summary>Ligne de texte où les passages entre ** sont en gras (rubriques de l'aide).</summary>
public sealed class RichLine : TextBlock
{
    public static readonly StyledProperty<string?> MarkupProperty =
        AvaloniaProperty.Register<RichLine, string?>(nameof(Markup));

    static RichLine() => MarkupProperty.Changed.AddClassHandler<RichLine>((line, _) => line.Rebuild());

    public string? Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        var parts = (Markup ?? string.Empty).Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                inlines.Add(new Run(parts[i]) { FontWeight = i % 2 == 1 ? FontWeight.SemiBold : FontWeight.Normal });
            }
        }

        Inlines = inlines;
    }
}
