using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace EVBlocker.App.Services;

/// <summary>
/// The notification-area icon the app lives in once its window is closed.
/// </summary>
/// <remarks>
/// WinForms' NotifyIcon, because WPF has none of its own and the alternative is hand-written
/// Shell_NotifyIcon interop with a message window. It runs on the WPF dispatcher's message loop.
/// </remarks>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _hiddenHintShown;

    public TrayIcon(Action open, Action exit)
    {
        _menu = new Forms.ContextMenuStrip();

        var openItem = new Forms.ToolStripMenuItem("Mở EVBlocker", null, (_, _) => open());
        openItem.Font = new Drawing.Font(openItem.Font, Drawing.FontStyle.Bold);
        _menu.Items.Add(openItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(new Forms.ToolStripMenuItem("Thoát EVBlocker", null, (_, _) => exit()));

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "EVBlocker",
            ContextMenuStrip = _menu,
            Visible = true,
        };

        // Left click opens; right click is left to the menu.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                open();
            }
        };
    }

    /// <summary>
    /// Says where the app went, the first time the window is closed in this run.
    /// </summary>
    /// <remarks>
    /// Closing a window has always meant exiting, so the first close that does not needs saying
    /// once. Every time would be nagging.
    /// </remarks>
    public void ShowHiddenHint()
    {
        if (_hiddenHintShown)
        {
            return;
        }

        _hiddenHintShown = true;
        _icon.ShowBalloonTip(
            5000,
            "EVBlocker vẫn đang chạy",
            "Bấm vào biểu tượng này để mở lại. Muốn tắt hẳn: bấm phải → Thoát EVBlocker.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        // Hidden before disposal, or the icon lingers in the tray until the mouse passes over it.
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    /// <summary>
    /// Loads the small frame from the embedded icon file rather than the executable's icon,
    /// which Windows hands back at 32 px and the tray would then shrink into a blur.
    /// </summary>
    private static Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using System.IO.Stream stream = resource.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
