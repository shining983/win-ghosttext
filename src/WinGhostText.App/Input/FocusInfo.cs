using System.Drawing;
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

        _ = NativeMethods.GetWindowThreadProcessId(foreground, out _);
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
        // Prefer GUI thread caret rect when available.
        if (gui.hwndCaret != IntPtr.Zero ||
            gui.rcCaret.Right != gui.rcCaret.Left ||
            gui.rcCaret.Bottom != gui.rcCaret.Top)
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

        if (NativeMethods.GetCaretPos(out var caretPos))
        {
            var pt = caretPos;
            if (NativeMethods.ClientToScreen(hwnd, ref pt))
            {
                return new Point(pt.X, pt.Y);
            }
        }

        // Fallback: edit client rect left/top.
        var rect = new NativeMethods.RECT();
        _ = NativeMethods.SendMessage(hwnd, NativeMethods.EM_GETRECT, IntPtr.Zero, ref rect);
        var fallback = new NativeMethods.POINT { X = rect.Left, Y = rect.Top };
        if (NativeMethods.ClientToScreen(hwnd, ref fallback))
        {
            return new Point(fallback.X, fallback.Y);
        }

        return Point.Empty;
    }
}
