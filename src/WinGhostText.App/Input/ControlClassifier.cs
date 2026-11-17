using System.Text;
using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

/// <summary>
/// Skips password boxes and common terminal-like controls.
/// </summary>
internal static class ControlClassifier
{
    private static readonly string[] TerminalClassFragments =
    [
        "ConsoleWindowClass",
        "CASCADIA_HOSTING_WINDOW_CLASS",
        "VirtualConsoleClass",
        "Vim",
        "PuTTY",
        "mintty",
        "Xming",
        "Windows.UI.Input.InputSite.WindowClass" // often used by modern hosts; filtered further below
    ];

    private static readonly string[] PasswordClassFragments =
    [
        "PasswordBox",
        "Edit" // Edit alone is not enough; checked with style/uia separately
    ];

    public static bool ShouldSkip(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return true;
        }

        var className = NativeMethods.GetWindowClass(hwnd);
        if (string.IsNullOrEmpty(className))
        {
            return true;
        }

        if (IsTerminalClass(className))
        {
            return true;
        }

        // Classic Win32 password edit: ES_PASSWORD = 0x0020
        if (className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
            className.Contains("Edit", StringComparison.OrdinalIgnoreCase))
        {
            var style = NativeMethods.GetWindowLongPtr(hwnd, -16); // GWL_STYLE
            if ((style.ToInt64() & 0x0020) != 0)
            {
                return true;
            }
        }

        if (className.Contains("Password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Heuristic: window title hints for terminals
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root != IntPtr.Zero)
        {
            var title = new StringBuilder(512);
            _ = NativeMethods.GetWindowText(root, title, title.Capacity);
            var t = title.ToString();
            if (t.Contains("Command Prompt", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Windows Terminal", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("wt.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTerminalClass(string className)
    {
        foreach (var fragment in TerminalClassFragments)
        {
            if (className.Equals(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
