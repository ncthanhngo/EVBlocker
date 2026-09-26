using System.Windows.Media;
using EVBlocker.App.Mvvm;

namespace EVBlocker.App.ViewModels;

/// <summary>
/// One row in the navigation rail, paired with the content it shows.
/// </summary>
/// <remarks>
/// The accent colour arrives as a hex string from the palette rather than as a brush, so the
/// rail can build the dot at whatever opacity it needs without the palette having to predict
/// every variation.
/// </remarks>
public sealed class NavSectionViewModel : ObservableObject
{
    public NavSectionViewModel(string title, string accentHex, object content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);

        Title = title;
        Content = content;
        DotBrush = BuildBrush(accentHex);
    }

    public string Title { get; }

    public object Content { get; }

    public Brush DotBrush { get; }

    /// <summary>
    /// Falls back to a neutral grey rather than throwing: a mistyped palette key should show a
    /// colourless dot, not stop the window from opening.
    /// </summary>
    private static Brush BuildBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }
}
