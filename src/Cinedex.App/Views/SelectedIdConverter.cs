using System.Globalization;
using Avalonia.Data.Converters;

namespace Cinedex.App.Views;

/// <summary>Vrai si l'identifiant (1re valeur) fait partie des identifiants sélectionnés (2e valeur).</summary>
public sealed class SelectedIdConverter : IMultiValueConverter
{
    public static SelectedIdConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [long id, IReadOnlySet<long> selected] && selected.Contains(id);
}
