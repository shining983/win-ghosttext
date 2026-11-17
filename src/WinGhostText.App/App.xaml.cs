using System.Windows;
using WinGhostText.App.Services;
using WinGhostText.App.Ui;
using WinGhostText.Core;
using WpfApplication = System.Windows.Application;

namespace WinGhostText.App;

public partial class App : WpfApplication
{
    private CompletionController? _controller;
    private TrayHost? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WinForms visual styles for the tray menu.
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var engine = CompletionEngine.CreateDefault();
        _controller = new CompletionController(engine);
        _tray = new TrayHost(_controller);

        try
        {
            _controller.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "Failed to install the keyboard hook:\n" + ex.Message +
                "\n\nTry running again; some environments require accessibility permission.",
                "WinGhostText",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        // Keep a hidden window so the WPF dispatcher stays alive with the tray.
        var hidden = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden
        };
        hidden.Show();
        hidden.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _controller?.Dispose();
        base.OnExit(e);
    }
}
