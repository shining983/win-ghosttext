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
    string BeforeCaret,
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
        var isNotepad = ControlClassifier.IsNotepadHost(hwnd, root, className);

        var caret = ResolveCaretScreen(hwnd, root, gui, isNotepad, out var caretHeightPx);
        var read = PrefixExtractor.TryReadPrefix(hwnd, className);
        // Bubble UI only lightly scales font size — FontProbe always supplies defaults.
        var font = FontProbe.TryRead(hwnd, root, className, caretHeightPx);

        return new FocusInfo(
            hwnd,
            root,
            className,
            caret,
            caretHeightPx,
            read.Prefix,
            read.HasSelection,
            read.BeforeCaret,
            font.Family,
            font.SizeDip,
            font.Weight,
            font.TargetCellHeightPx,
            dpi);
    }

    private static Point ResolveCaretScreen(
        IntPtr hwnd,
        IntPtr rootHwnd,
        NativeMethods.GUITHREADINFO gui,
        bool preferGuiFallback,
        out int caretHeightPx)
    {
        caretHeightPx = Math.Max(0, gui.rcCaret.Bottom - gui.rcCaret.Top);

        // Win11 Notepad (WinUI/InputSite): UIA on the focus hwnd often lacks TextPattern /
        // DocumentRange. Prefer a real GUI caret when present; otherwise probe Document UIA,
        // then EM_GETRECT — do not require Document TextPattern if a caret rect exists.
        if (preferGuiFallback && TryGetGuiCaretScreen(hwnd, gui, out var guiCaret, out var guiHeight))
        {
            if (guiHeight > 0)
            {
                caretHeightPx = guiHeight;
            }

            return guiCaret;
        }

        // 1) UIA TextPattern selection/caret bounding rect — Right edge is the
        // insertion point after the caret (bubble anchor X).
        if (TryGetUiaCaretScreen(hwnd, rootHwnd, out var uiaCaret, out var uiaHeight) &&
            IsPlausibleScreenPoint(uiaCaret))
        {
            if (uiaHeight > 0)
            {
                caretHeightPx = uiaHeight;
            }

            return uiaCaret;
        }

        // 2) GUI thread caret (non-Notepad path, or when preferGui path missed).
        if (TryGetGuiCaretScreen(hwnd, gui, out guiCaret, out guiHeight))
        {
            if (guiHeight > 0)
            {
                caretHeightPx = guiHeight;
            }

            return guiCaret;
        }

        // 3) Thread-local GetCaretPos (same process / attached caret).
        if (NativeMethods.GetCaretPos(out var caretPos))
        {
            var pt = caretPos;
            if (NativeMethods.ClientToScreen(hwnd, ref pt) && IsPlausibleScreenPoint(new Point(pt.X, pt.Y)))
            {
                return new Point(pt.X, pt.Y);
            }
        }

        // 4) Edit client rect left/top — last-ditch so the bubble is not at (0,0).
        if (TryGetEditRectScreen(hwnd, out var editPt))
        {
            return editPt;
        }

        // 5) Win11 Notepad: EM_GETRECT on focus InputSite often fails — try root client area.
        if (rootHwnd != IntPtr.Zero && rootHwnd != hwnd &&
            TryGetEditRectScreen(rootHwnd, out editPt))
        {
            return editPt;
        }

        return Point.Empty;
    }

    private static bool TryGetGuiCaretScreen(
        IntPtr hwnd,
        NativeMethods.GUITHREADINFO gui,
        out Point point,
        out int heightPx)
    {
        point = Point.Empty;
        heightPx = 0;
        if (!HasGuiCaret(gui))
        {
            return false;
        }

        heightPx = Math.Max(0, gui.rcCaret.Bottom - gui.rcCaret.Top);
        var pt = new NativeMethods.POINT
        {
            X = gui.rcCaret.Right,
            Y = gui.rcCaret.Top
        };
        var caretHwnd = gui.hwndCaret != IntPtr.Zero ? gui.hwndCaret : hwnd;
        if (!NativeMethods.ClientToScreen(caretHwnd, ref pt))
        {
            return false;
        }

        point = new Point(pt.X, pt.Y);
        return IsPlausibleScreenPoint(point);
    }

    private static bool TryGetEditRectScreen(IntPtr hwnd, out Point point)
    {
        point = Point.Empty;
        var rect = new NativeMethods.RECT();
        _ = NativeMethods.SendMessage(hwnd, NativeMethods.EM_GETRECT, IntPtr.Zero, ref rect);
        // Empty EM_GETRECT → use client origin via (0,0) ClientToScreen.
        var fallback = new NativeMethods.POINT
        {
            X = Math.Max(0, rect.Left),
            Y = Math.Max(0, rect.Top)
        };
        if (!NativeMethods.ClientToScreen(hwnd, ref fallback))
        {
            return false;
        }

        point = new Point(fallback.X, fallback.Y);
        return IsPlausibleScreenPoint(point);
    }

    /// <summary>
    /// Win32 caret is typically a zero-width line (Left==Right) with non-zero height.
    /// Require a non-empty rect so a stale hwndCaret with 0×0 does not block UIA.
    /// </summary>
    private static bool HasGuiCaret(NativeMethods.GUITHREADINFO gui) =>
        gui.rcCaret.Right != gui.rcCaret.Left ||
        gui.rcCaret.Bottom != gui.rcCaret.Top;

    /// <summary>
    /// Reject (0,0) / near-origin ghosts that shove the bubble off the editor.
    /// </summary>
    private static bool IsPlausibleScreenPoint(Point point) =>
        point.X > 2 || point.Y > 2;

    private static bool TryGetUiaCaretScreen(IntPtr hwnd, IntPtr rootHwnd, out Point point, out int heightPx)
    {
        point = Point.Empty;
        heightPx = 0;
        try
        {
            foreach (var textPattern in EnumerateTextPatterns(hwnd, rootHwnd))
            {
                if (TryReadCaretFromPattern(textPattern, out point, out heightPx))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<TextPattern> EnumerateTextPatterns(IntPtr hwnd, IntPtr rootHwnd)
    {
        AutomationElement? fromHwnd = null;
        try
        {
            if (hwnd != IntPtr.Zero)
            {
                fromHwnd = AutomationElement.FromHandle(hwnd);
            }
        }
        catch
        {
            // ignore
        }

        if (fromHwnd is not null &&
            fromHwnd.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) &&
            patternObj is TextPattern hwndPattern)
        {
            yield return hwndPattern;
        }

        AutomationElement? focused = null;
        try
        {
            focused = AutomationElement.FocusedElement;
        }
        catch
        {
            // ignore
        }

        if (focused is not null &&
            !Equals(focused, fromHwnd) &&
            focused.TryGetCurrentPattern(TextPattern.Pattern, out patternObj) &&
            patternObj is TextPattern focusedPattern)
        {
            yield return focusedPattern;
        }

        // Win11 Notepad: caret lives on Document / "Text editor", not InputSite focus.
        AutomationElement? root = null;
        try
        {
            var rootHandle = rootHwnd != IntPtr.Zero ? rootHwnd : hwnd;
            if (rootHandle != IntPtr.Zero)
            {
                root = AutomationElement.FromHandle(rootHandle);
            }
        }
        catch
        {
            // ignore
        }

        if (root is null)
        {
            yield break;
        }

        foreach (var condition in DocumentConditions())
        {
            AutomationElement? doc = null;
            try
            {
                doc = root.FindFirst(TreeScope.Descendants, condition);
            }
            catch
            {
                continue;
            }

            if (doc is null || Equals(doc, fromHwnd) || Equals(doc, focused))
            {
                continue;
            }

            if (doc.TryGetCurrentPattern(TextPattern.Pattern, out patternObj) &&
                patternObj is TextPattern docPattern)
            {
                yield return docPattern;
            }
        }
    }

    private static IEnumerable<Condition> DocumentConditions()
    {
        yield return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
        yield return new PropertyCondition(AutomationElement.NameProperty, "Text editor");
        yield return new PropertyCondition(AutomationElement.AutomationIdProperty, "RichEditBox");
        yield return new PropertyCondition(AutomationElement.ClassNameProperty, "RichEditD2DPT");
        yield return new PropertyCondition(AutomationElement.ClassNameProperty, "RichEditD2DWindow");
    }

    private static bool TryReadCaretFromPattern(TextPattern textPattern, out Point point, out int heightPx)
    {
        point = Point.Empty;
        heightPx = 0;
        try
        {
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
            return IsPlausibleScreenPoint(point);
        }
        catch
        {
            return false;
        }
    }
}
