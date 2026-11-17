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
    int TargetCellHeightPx,
    uint Dpi)
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
        var dpi = NativeMethods.GetWindowDpi(hwnd);

        var caret = ResolveCaretScreen(hwnd, gui, out var caretHeightPx);
        var (prefix, hasSelection) = PrefixExtractor.TryReadPrefix(hwnd, className);
        // Bubble UI only lightly scales font size — skip heavy face/weight matching.
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
            font.TargetCellHeightPx,
            dpi);
    }

    private static Point ResolveCaretScreen(IntPtr hwnd, NativeMethods.GUITHREADINFO gui, out int caretHeightPx)
    {
        caretHeightPx = Math.Max(0, gui.rcCaret.Bottom - gui.rcCaret.Top);

        // 1) Prefer UIA TextPattern selection/caret bounding rect — Right edge is the
        // insertion point after the caret (bubble anchor X). Word / Win11 Notepad / browsers.
        if (TryGetUiaCaretScreen(hwnd, out var uiaCaret, out var uiaHeight))
        {
            if (uiaHeight > 0)
            {
                caretHeightPx = uiaHeight;
            }

            return uiaCaret;
        }

        // 2) GUI thread caret: use Right/Top (not Left) so the bubble sits after the caret bar.
        if (HasGuiCaret(gui))
        {
            var pt = new NativeMethods.POINT
            {
                X = gui.rcCaret.Right,
                Y = gui.rcCaret.Top
            };
            var caretHwnd = gui.hwndCaret != IntPtr.Zero ? gui.hwndCaret : hwnd;
            if (NativeMethods.ClientToScreen(caretHwnd, ref pt))
            {
                return new Point(pt.X, pt.Y);
            }
        }

        // 3) Thread-local GetCaretPos (same process / attached caret).
        if (NativeMethods.GetCaretPos(out var caretPos))
        {
            var pt = caretPos;
            if (NativeMethods.ClientToScreen(hwnd, ref pt) && (pt.X != 0 || pt.Y != 0))
            {
                return new Point(pt.X, pt.Y);
            }
        }

        // 4) Edit client rect left/top — last-ditch so the bubble is not at (0,0).
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

    private static bool TryGetUiaCaretScreen(IntPtr hwnd, out Point point, out int heightPx)
    {
        point = Point.Empty;
        heightPx = 0;
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
                double.IsInfinity(rect.X) || double.IsInfinity(rect.Y) ||
                double.IsNaN(rect.Width) || double.IsNaN(rect.Height))
            {
                return false;
            }

            // Right edge = insertion point after the caret / selection end.
            var right = rect.X + Math.Max(0, rect.Width);
            point = new Point((int)Math.Round(right), (int)Math.Round(rect.Y));
            heightPx = Math.Max(0, (int)Math.Round(rect.Height));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
