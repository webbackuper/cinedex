using System.Globalization;
using Avalonia.Data.Converters;
using Cinedex.Core.Titles;

namespace Cinedex.App.Views;

/// <summary>Icône de chaque filtre de la barre latérale.</summary>
public sealed class FilterIconConverter : IValueConverter
{
    public static FilterIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        QuickFilter.All => "library",
        QuickFilter.Owned => "database",
        QuickFilter.Wishlist => "bookmark",
        QuickFilter.Seen => "check",
        QuickFilter.Unseen => "eye",
        QuickFilter.Favorites => "heart",
        QuickFilter.Shortlist => "star",
        QuickFilter.Movies => "film",
        QuickFilter.Series => "tv",
        QuickFilter.Loaned => "loan",
        QuickFilter.StandardDefinition or QuickFilter.HighDefinition or QuickFilter.UltraHighDefinition => "view",
        QuickFilter.ThreeD => "default",
        QuickFilter.PhysicalMedia => "harddisk",
        QuickFilter.MediaFiles => "folder",
        QuickFilter.MissingFiles => "alert",
        QuickFilter.NoMetadata => "cloud",
        _ => "funnel",
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
