namespace EVBlocker.App.ViewModels;

/// <summary>
/// An empty state for a section that is planned but not built.
/// </summary>
/// <remarks>
/// Deliberately not a mock-up. A grid of invented rows would demo well and then mislead whoever
/// reviews the UI about what works; naming the phase it arrives in says the same thing honestly.
/// </remarks>
public sealed class PlaceholderViewModel
{
    public PlaceholderViewModel(string heading, string detail)
    {
        Heading = heading;
        Detail = detail;
    }

    public string Heading { get; }

    public string Detail { get; }
}
