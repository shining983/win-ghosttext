using System.Text;
using System.Windows.Automation;
using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

/// <summary>
/// Skips password boxes and common terminal-like controls.
/// Win11 Notepad focuses <c>Windows.UI.Input.InputSite.WindowClass</c> — that is NOT a terminal.
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
        "Xming"
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

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        // Win11 Store Notepad / classic Notepad — never treat as terminal.
        if (IsNotepadHost(hwnd, root, className))
        {
            return IsPasswordControl(hwnd, className);
        }

        if (IsTerminalClass(className))
        {
            return true;
        }

        if (IsPasswordControl(hwnd, className))
        {
            return true;
        }

        // Heuristic: window title hints for terminals
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

    /// <summary>
    /// Win11 Notepad uses InputSite as hwndFocus; classic Notepad uses Edit / Notepad root.
    /// </summary>
    internal static bool IsNotepadHost(IntPtr hwnd, IntPtr rootHwnd, string? className = null)
    {
        className ??= NativeMethods.GetWindowClass(hwnd);
        if (!string.IsNullOrEmpty(className) &&
            className.Contains("Notepad", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var root = rootHwnd != IntPtr.Zero
            ? rootHwnd
            : NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
        {
            return false;
        }

        var rootClass = NativeMethods.GetWindowClass(root);
        return rootClass.Equals("Notepad", StringComparison.OrdinalIgnoreCase) ||
               rootClass.Equals("NotepadWindow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPasswordControl(IntPtr hwnd, string className)
    {
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

        return IsUiaPassword(hwnd);
    }

    private static bool IsUiaPassword(IntPtr hwnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (element is null)
            {
                return false;
            }

            var value = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty);
            return value is true;
        }
        catch
        {
            return false;
        }
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
