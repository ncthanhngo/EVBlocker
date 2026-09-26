using System.Windows;
using EVBlocker.App.ViewModels;

namespace EVBlocker.App.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Margin the shell card keeps when the window floats, reinstated on restore. Maximising
    /// drops it to zero: a rounded, inset card with a drop shadow is wrong when the window
    /// covers the screen, because there is nothing for it to float above.
    /// </summary>
    private static readonly Thickness FloatingShellMargin = new(30, 16, 22, 26);

    private static readonly Thickness MaximisedShellMargin = new(8, 8, 8, 8);

    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        // Polling starts once the window is up and stops with it: a timer left running against
        // a closed window keeps scanning for nothing.
        Loaded += (_, _) => _viewModel.ActiveConnections.Start();
        Closed += (_, _) => _viewModel.ActiveConnections.Stop();
        StateChanged += (_, _) => ApplyStateMargins();
    }

    private void ApplyStateMargins()
    {
        bool maximised = WindowState == WindowState.Maximized;

        Shell.Margin = maximised ? MaximisedShellMargin : FloatingShellMargin;
        Shell.CornerRadius = new CornerRadius(maximised ? 10 : 18);

        // The rail is positioned against the shell card's edges, so its inset has to move with
        // the card or it detaches from it.
        RailCard.Margin = maximised
            ? new Thickness(0, 48, 0, 48)
            : new Thickness(6, 56, 0, 66);
    }

    /// <summary>
    /// Drops the button's menu under the button on a left click.
    /// </summary>
    /// <remarks>
    /// A ContextMenu is used as the dropdown because WPF has no split button, and a context menu
    /// otherwise opens on right click at the pointer. Placement is set here rather than in XAML
    /// so the menu belongs to whichever button was pressed.
    /// </remarks>
    private void OnOpenAddMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button || button.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

        // Inherited from the button, so the menu items bind to the same view model.
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }

    private void OnMinimise(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximise(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
