using System.Windows;
using System.Windows.Controls;
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

        Loaded += (_, _) => HookDeviceNotifications();

        // Polling follows visibility, not the window's lifetime: closing hides the window to the
        // tray, and a timer left running behind a hidden window keeps scanning for nothing.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _viewModel.ActiveConnections.Start();
            }
            else
            {
                _viewModel.ActiveConnections.Stop();
            }
        };

        Closed += (_, _) => _viewModel.ActiveConnections.Stop();
        StateChanged += (_, _) => ApplyStateMargins();
    }

    /// <summary>Windows announces a drive arriving or leaving through this message.</summary>
    private const int WmDeviceChange = 0x0219;

    /// <summary>DBT_DEVICEARRIVAL and DBT_DEVICEREMOVECOMPLETE.</summary>
    private const int DeviceArrival = 0x8000;

    private const int DeviceRemoved = 0x8004;

    /// <summary>
    /// Listens for drives appearing so a USB scan needs no button.
    /// </summary>
    /// <remarks>
    /// A window message rather than a timer: polling for drives means either a delay before
    /// anyone notices the drive or a query every second forever, and the message arrives the
    /// moment Windows mounts the volume. The hook needs a window handle, which is why it waits
    /// for Loaded rather than running in the constructor.
    /// </remarks>
    private void HookDeviceNotifications()
    {
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && (wParam == DeviceArrival || wParam == DeviceRemoved))
        {
            // Not marked handled: this is a notification others may also want, and swallowing a
            // broadcast from a window hook is a good way to break something unrelated.
            _viewModel.Usb.OnDrivesChanged();
        }

        return 0;
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
    /// <summary>
    /// Re-reads whether the row under the pointer is allowed before its menu shows.
    /// </summary>
    /// <remarks>
    /// The commands are not requeried on every focus change, and the allow-list can change on
    /// its own page while the same row stays selected here - so without this the menu could
    /// offer to allow something that already is.
    /// </remarks>
    private void OnRowMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActiveConnectionsViewModel live })
        {
            live.Actions.RefreshCommands();
        }
        else if (sender is FrameworkElement { DataContext: HistoryViewModel history })
        {
            history.Actions.RefreshCommands();
        }
    }

    /// <summary>
    /// Pushes the typed password into the view model on each keystroke.
    /// </summary>
    /// <remarks>
    /// WPF's PasswordBox does not expose Password for binding, on purpose, so the value is copied
    /// across by hand. The box sits inside the SSH template, so its DataContext is that view model.
    /// </remarks>
    private void OnSshPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: SshViewModel ssh } box)
        {
            ssh.PasswordInput = box.Password;
        }
    }

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

    /// <summary>
    /// Hides to the tray rather than closing, for the title-bar button and Alt+F4 alike; only
    /// an exit from the tray menu, a relaunch or the end of the session really closes.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            App.OnHiddenToTray();
        }

        base.OnClosing(e);
    }
}
