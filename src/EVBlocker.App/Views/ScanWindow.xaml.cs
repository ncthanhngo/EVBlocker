using System.Windows;
using EVBlocker.App.ViewModels;
using EVBlocker.Core.Monitor;

namespace EVBlocker.App.Views;

public partial class ScanWindow : Window
{
    private readonly ScanViewModel _viewModel;

    public ScanWindow(ScanViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>What the user ticked. Only meaningful after the dialog returns true.</summary>
    public IReadOnlyList<RunningApp> Selected { get; private set; } = Array.Empty<RunningApp>();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        // Read before closing: the view model's selection is derived from rows that the closing
        // window would otherwise be free to tear down.
        Selected = _viewModel.Selected;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
