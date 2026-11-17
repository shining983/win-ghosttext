using System.Globalization;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

/// <summary>
/// Best-effort live font probe. Bubble UI uses size only for light scaling;
/// family/weight matching is no longer required for caret-anchored suggestions.
/// Returns WPF device-independent pixel (DIP) sizes — not raw GDI points/pixels.
/// </summary>
internal static class FontProbe
{
    public readonly record struct Result(
        string? Family,
        float? SizeDip,
        int? Weight,
        /// <summary>Rendered line/glyph cell height in device pixels when known (caret/bubble height fallback).</summary>
        int TargetCellHeightPx);

    public static Result TryRead(IntPtr hwnd, IntPtr rootHwnd, string className, int caretHeightPx = 0)
    {
        string? family = null;
        float? sizeDip = null;
        int? weight = null;
        int glyphHeightPx = 0;

        // 1) UIA on focus hwnd, then Document / "Text editor" under the root (Win11 Notepad).
        TryReadUia(hwnd, rootHwnd, ref family, ref sizeDip, ref weight, ref glyphHeightPx);

        // 2) WM_GETFONT / LOGFONT (classic Edit / some RichEdit hosts).
        TryReadWin32(hwnd, ref family, ref sizeDip, ref weight);

        // Drop implausibly tiny sizes (bad UIA units) so caret/glyph calibration can win.
        if (sizeDip is <= 6f)
        {
            sizeDip = null;
        }

        // Prefer measured glyph bounds, else caret cell, as the visual target height.
        var targetCellPx = glyphHeightPx > 0 ? glyphHeightPx : caretHeightPx;

        // 3) If size still unknown, estimate em size from the cell (~75–85% of line box).
        if ((sizeDip is null or <= 0) && targetCellPx > 0)
        {
            var dpi = NativeMethods.GetWindowDpi(hwnd != IntPtr.Zero ? hwnd : rootHwnd);
            // Line/caret cell is taller than em; em ≈ 0.8 * cell matches GDI tmHeight vs lfHeight well.
            var emDip = PixelsToDip(targetCellPx, dpi) * 0.8f;
            if (emDip > 0)
            {
                sizeDip = emDip;
            }
        }

        // 4) Sensible class / host fallbacks when live probes fail.
        ApplyFallbacks(hwnd, rootHwnd, className, ref family, ref sizeDip, ref weight);

        return new Result(family, sizeDip, weight, targetCellPx);
    }

    /// <summary>Convert typographic points to WPF DIPs (96 DPI CSS pixels).</summary>
    internal static float PointsToDip(float points) => points * 96f / 72f;

    /// <summary>Convert device pixels at <paramref name="dpi"/> to WPF DIPs.</summary>
    internal static float PixelsToDip(int pixels, uint dpi)
    {
        if (pixels <= 0)
        {
            return 0f;
        }

        if (dpi == 0)
        {
            dpi = NativeMethods.DEFAULT_DPI;
        }

        return pixels * 96f / dpi;
    }

    private static void TryReadUia(
        IntPtr hwnd,
        IntPtr rootHwnd,
        ref string? family,
        ref float? sizeDip,
        ref int? weight,
        ref int glyphHeightPx)
    {
        try
        {
            foreach (var element in EnumerateTextElements(hwnd, rootHwnd))
            {
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) ||
                    patternObj is not TextPattern textPattern)
                {
                    continue;
                }

                var range = textPattern.GetSelection().FirstOrDefault() ?? textPattern.DocumentRange;
                ReadAttributes(range, ref family, ref sizeDip, ref weight);
                TryReadGlyphHeight(range, ref glyphHeightPx);

                if (family is not null || sizeDip is not null || glyphHeightPx > 0)
                {
                    return;
                }
            }
        }
        catch
        {
            // Fall through to Win32 / caret.
        }
    }

    private static IEnumerable<AutomationElement> EnumerateTextElements(IntPtr hwnd, IntPtr rootHwnd)
    {
        AutomationElement? focus = null;
        try
        {
            if (hwnd != IntPtr.Zero)
            {
                focus = AutomationElement.FromHandle(hwnd);
            }
        }
        catch
        {
            // ignore
        }

        if (focus is not null)
        {
            yield return focus;
        }

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

        // Win11 Notepad: font attrs live on the Document / "Text editor" node, not the focus InputSite.
        foreach (var condition in DocumentConditions())
        {
            AutomationElement? doc = null;
            try
            {
                doc = root.FindFirst(TreeScope.Descendants, condition);
            }
            catch
            {
                // ignore
            }

            if (doc is not null && !Equals(doc, focus))
            {
                yield return doc;
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

    private static void ReadAttributes(
        TextPatternRange range,
        ref string? family,
        ref float? sizeDip,
        ref int? weight)
    {
        var fontNameObj = range.GetAttributeValue(TextPattern.FontNameAttribute);
        var fontSizeObj = range.GetAttributeValue(TextPattern.FontSizeAttribute);
        var fontWeightObj = range.GetAttributeValue(TextPattern.FontWeightAttribute);

        if (fontNameObj is string name &&
            !string.IsNullOrWhiteSpace(name) &&
            !ReferenceEquals(fontNameObj, AutomationElement.NotSupported) &&
            !ReferenceEquals(fontNameObj, TextPattern.MixedAttributeValue))
        {
            family ??= name.Trim();
        }

        // UIA FontSize is documented in points (VT_R8).
        if (!ReferenceEquals(fontSizeObj, TextPattern.MixedAttributeValue) &&
            TryCoercePositiveFloat(fontSizeObj, out var points) &&
            points > 0)
        {
            sizeDip ??= PointsToDip(points);
        }

        if (!ReferenceEquals(fontWeightObj, TextPattern.MixedAttributeValue) &&
            TryCoercePositiveFloat(fontWeightObj, out var w) &&
            w > 0)
        {
            weight ??= (int)Math.Round(w);
        }
    }

    private static void TryReadGlyphHeight(TextPatternRange range, ref int glyphHeightPx)
    {
        try
        {
            // Shrink to one character before the caret when possible for a tight glyph box.
            var probe = range.Clone();
            var text = probe.GetText(8) ?? string.Empty;
            if (text.Length > 1)
            {
                probe.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, text.Length - 1);
            }

            var rects = probe.GetBoundingRectangles();
            foreach (var rect in rects)
            {
                var height = (int)Math.Round(rect.Height);
                if (height > glyphHeightPx)
                {
                    glyphHeightPx = height;
                }
            }
        }
        catch
        {
            // Bounding rects are optional.
        }
    }

    private static void TryReadWin32(IntPtr hwnd, ref string? family, ref float? sizeDip, ref int? weight)
    {
        try
        {
            var hfont = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETFONT, IntPtr.Zero, IntPtr.Zero);
            if (hfont == IntPtr.Zero)
            {
                return;
            }

            var lf = new NativeMethods.LOGFONT();
            if (NativeMethods.GetObject(hfont, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LOGFONT>(), ref lf) <= 0)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(lf.lfFaceName))
            {
                family ??= lf.lfFaceName.Trim();
            }

            if (lf.lfWeight > 0)
            {
                weight ??= lf.lfWeight;
            }

            if (lf.lfHeight != 0)
            {
                var dpi = NativeMethods.GetWindowDpi(hwnd);
                // lfHeight is in logical device pixels for the window's DPI.
                // Negative = character (em) height; positive = cell height.
                var abs = Math.Abs(lf.lfHeight);
                var dip = PixelsToDip(abs, dpi);
                if (lf.lfHeight > 0)
                {
                    // Positive height is cell height (includes external leading) — shrink toward em.
                    dip *= 0.8f;
                }

                if (dip > 0)
                {
                    sizeDip ??= dip;
                }
            }
        }
        catch
        {
            // Ignore and keep prior probes.
        }
    }

    private static void ApplyFallbacks(
        IntPtr hwnd,
        IntPtr rootHwnd,
        string className,
        ref string? family,
        ref float? sizeDip,
        ref int? weight)
    {
        var isEdit = className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                     className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase);

        var root = rootHwnd != IntPtr.Zero ? rootHwnd : NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var rootClass = root != IntPtr.Zero ? NativeMethods.GetWindowClass(root) : string.Empty;
        var isNotepad = rootClass.Equals("Notepad", StringComparison.OrdinalIgnoreCase) ||
                        rootClass.Equals("NotepadWindow", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Notepad", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(family))
        {
            if (isNotepad)
            {
                // Win11 Store Notepad often uses a proportional UI face in rich mode;
                // Cascadia Mono remains common for plain-text defaults.
                family = "Segoe UI Variable, Cascadia Mono, Consolas, Segoe UI";
            }
            else if (isEdit)
            {
                family = "Consolas";
            }
        }

        if (sizeDip is null or <= 0)
        {
            // 11pt → DIPs; common Notepad / Edit default.
            sizeDip = PointsToDip(11f);
        }

        weight ??= 400;
    }

    private static bool TryCoercePositiveFloat(object? value, out float result)
    {
        result = 0f;
        if (value is null || ReferenceEquals(value, AutomationElement.NotSupported))
        {
            return false;
        }

        switch (value)
        {
            case double d when d > 0 && !double.IsNaN(d) && !double.IsInfinity(d):
                result = (float)d;
                return true;
            case float f when f > 0 && !float.IsNaN(f) && !float.IsInfinity(f):
                result = f;
                return true;
            case int i when i > 0:
                result = i;
                return true;
            case long l when l > 0 && l < int.MaxValue:
                result = l;
                return true;
            case decimal m when m > 0:
                result = (float)m;
                return true;
            case string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0:
                result = parsed;
                return true;
            default:
                return false;
        }
    }
}
