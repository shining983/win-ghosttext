using System.Drawing;
using System.Windows.Forms;
using WinGhostText.App.Services;

namespace WinGhostText.App.Ui;

/// <summary>
/// System-tray enable/disable + exit. Uses WinForms NotifyIcon inside the WPF process.
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _notify;
    private readonly CompletionController _controller;
    private bool _disposed;

    public TrayHost(CompletionController controller)
    {
        _controller = controller;
        _notify = new NotifyIcon
        {
            Visible = true,
            Text = "WinGhostText (enabled)",
            Icon = SystemIcons.Application
        };

        var menu = new ContextMenuStrip();
        var toggle = new ToolStripMenuItem("Enabled")
        {
            Checked = true,
            CheckOnClick = true
        };
        toggle.CheckedChanged += (_, _) =>
        {
            _controller.Enabled = toggle.Checked;
            _notify.Text = toggle.Checked ? "WinGhostText (enabled)" : "WinGhostText (disabled)";
        };

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            _notify.Visible = false;
            System.Windows.Application.Current?.Shutdown();
        };

        menu.Items.Add(toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        _notify.ContextMenuStrip = menu;
        _notify.DoubleClick += (_, _) =>
        {
            toggle.Checked = !toggle.Checked;
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notify.Visible = false;
        _notify.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
