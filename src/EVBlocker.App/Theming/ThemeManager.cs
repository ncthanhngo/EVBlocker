using System.Collections.ObjectModel;
using System.Windows;

namespace EVBlocker.App.Theming;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Swaps the palette dictionary at runtime.
/// </summary>
/// <remarks>
/// Works because every palette colour is referenced with DynamicResource and every value that
/// does not change with the theme - radii, typefaces - lives in Tokens.xaml and is referenced
/// with StaticResource. A single StaticResource pointing at a palette key would keep its original
/// colour after a switch and look like a rendering bug.
///
/// The two palettes are key-for-key twins; a key present in one and missing from the other would
/// resolve to nothing the moment somebody flipped the switch.
/// </remarks>
public static class ThemeManager
{
    /// <summary>Identifies the palette among the merged dictionaries.</summary>
    private const string PaletteMarker = "Themes/Palette.";

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static void Apply(AppTheme theme)
    {
        Collection<ResourceDictionary> dictionaries = Application.Current.Resources.MergedDictionaries;

        ResourceDictionary? existing = dictionaries.FirstOrDefault(IsPalette);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"Themes/Palette.{theme}.xaml", UriKind.Relative),
        };

        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            // Added before the old one is removed, and at the same position. Removing first would
            // leave a frame with no palette at all, during which every DynamicResource resolves
            // to nothing and the window paints itself blank.
            int index = dictionaries.IndexOf(existing);
            dictionaries.Insert(index, replacement);
            dictionaries.Remove(existing);
        }

        Current = theme;
    }

    public static AppTheme Toggle()
    {
        AppTheme next = Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        Apply(next);
        return next;
    }

    private static bool IsPalette(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString.Contains(PaletteMarker, StringComparison.OrdinalIgnoreCase) == true;
}
