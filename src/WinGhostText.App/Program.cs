using System.Windows;

namespace WinGhostText.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        _ = args;
        app.Run();
    }
}
