using System.Windows;
using EVBlocker.App.ViewModels;

namespace EVBlocker.App.Views;

public partial class InstalledProgramsWindow : Window
{
    private readonly InstalledProgramsViewModel _viewModel;

    public InstalledProgramsWindow(InstalledProgramsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>What the user ticked. Only meaningful after the dialog returns true.</summary>
    public IReadOnlyList<ScannedSelection> Selected { get; private set; } =
        Array.Empty<ScannedSelection>();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        // Read before closing, like the scan dialog: the selection is derived from rows the
        // closing window is free to tear down.
        Selected = _viewModel.Selected;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
