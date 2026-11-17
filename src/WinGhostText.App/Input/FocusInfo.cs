using System.Drawing;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

internal sealed record FocusInfo(
    IntPtr Hwnd,
    IntPtr RootHwnd,
    string ClassName,
    Point CaretScreen,
    int CaretHeightPx,
    string Prefix,
    bool HasSelection,
    string? FontFamily,
    float? FontSizeDip,
    int? FontWeight,
    int TargetCellHeightPx)
{
    public static FocusInfo? TryCapture()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return null;
        }

        var gui = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        var threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        if (!NativeMethods.GetGUIThreadInfo(threadId, ref gui))
        {
            return null;
        }

        var hwnd = gui.hwndFocus != IntPtr.Zero ? gui.hwndFocus : foreground;
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var className = NativeMethods.GetWindowClass(hwnd);
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);

        var caret = ResolveCaretScreen(hwnd, gui);
        var caretHeightPx = Math.Max(0, gui.rcCaret.Bottom - gui.rcCaret.Top);
        var (prefix, hasSelection) = PrefixExtractor.TryReadPrefix(hwnd, className);
        var font = FontProbe.TryRead(hwnd, root, className, caretHeightPx);

        return new FocusInfo(
            hwnd,
            root,
            className,
            caret,
            caretHeightPx,
            prefix,
            hasSelection,
            font.Family,
            font.SizeDip,
            font.Weight,
            font.TargetCellHeightPx);
    }

    private static Point ResolveCaretScreen(IntPtr hwnd, NativeMethods.GUITHREADINFO gui)
    {
        // 1) Prefer GUI thread caret rect when available (classic Edit / many Win32 hosts).
        if (HasGuiCaret(gui))
        {
            var pt = new NativeMethods.POINT
            {
                X = gui.rcCaret.Left,
                Y = gui.rcCaret.Top
            };
            var caretHwnd = gui.hwndCaret != IntPtr.Zero ? gui.hwndCaret : hwnd;
            if (NativeMethods.ClientToScreen(caretHwnd, ref pt))
            {
                return new Point(pt.X, pt.Y);
            }
        }

        // 2) Thread-local GetCaretPos (same process / attached caret).
        if (NativeMethods.GetCaretPos(out var caretPos))
        {
            var pt = caretPos;
            if (NativeMethods.ClientToScreen(hwnd, ref pt) && (pt.X != 0 || pt.Y != 0))
            {
                return new Point(pt.X, pt.Y);
            }
        }

        // 3) UIA TextPattern selection bounding rect (Word, Win11 Notepad, some browsers).
        if (TryGetUiaCaretScreen(hwnd, out var uiaCaret))
        {
            return uiaCaret;
        }

        // 4) Edit client rect left/top — last-ditch so overlay is not at (0,0).
        var rect = new NativeMethods.RECT();
        _ = NativeMethods.SendMessage(hwnd, NativeMethods.EM_GETRECT, IntPtr.Zero, ref rect);
        var fallback = new NativeMethods.POINT { X = rect.Left, Y = rect.Top };
        if (NativeMethods.ClientToScreen(hwnd, ref fallback))
        {
            return new Point(fallback.X, fallback.Y);
        }

        return Point.Empty;
    }

    /// <summary>
    /// Win32 caret is typically a zero-width line (Left==Right) with non-zero height.
    /// Require a non-empty rect so a stale hwndCaret with 0×0 does not block UIA.
    /// </summary>
    private static bool HasGuiCaret(NativeMethods.GUITHREADINFO gui) =>
        gui.rcCaret.Right != gui.rcCaret.Left ||
        gui.rcCaret.Bottom != gui.rcCaret.Top;

    private static bool TryGetUiaCaretScreen(IntPtr hwnd, out Point point)
    {
        point = Point.Empty;
        try
        {
            AutomationElement? element = null;
            try
            {
                element = AutomationElement.FromHandle(hwnd);
            }
            catch
            {
                // ignore
            }

            if (element is null ||
                !element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) ||
                patternObj is not TextPattern textPattern)
            {
                try
                {
                    element = AutomationElement.FocusedElement;
                }
                catch
                {
                    return false;
                }

                if (element is null ||
                    !element.TryGetCurrentPattern(TextPattern.Pattern, out patternObj) ||
                    patternObj is not TextPattern focusedPattern)
                {
                    return false;
                }

                textPattern = focusedPattern;
            }

            var selection = textPattern.GetSelection();
            if (selection.Length == 0)
            {
                return false;
            }

            // WPF UIA returns screen Rect[]; degenerate carets often have Width==0.
            var rects = selection[0].GetBoundingRectangles();
            if (rects is null || rects.Length == 0)
            {
                return false;
            }

            var rect = rects[0];
            if (double.IsNaN(rect.X) || double.IsNaN(rect.Y) ||
                double.IsInfinity(rect.X) || double.IsInfinity(rect.Y))
            {
                return false;
            }

            point = new Point((int)Math.Round(rect.X), (int)Math.Round(rect.Y));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
