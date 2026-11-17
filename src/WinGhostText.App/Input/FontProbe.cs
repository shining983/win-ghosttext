using System.Globalization;
using System.Windows.Automation;
using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

/// <summary>
/// Best-effort live font probe for GhostText overlay matching.
/// Returns WPF device-independent pixel (DIP) sizes — not raw GDI points/pixels.
/// </summary>
internal static class FontProbe
{
    public readonly record struct Result(string? Family, float? SizeDip, int? Weight);

    public static Result TryRead(IntPtr hwnd, string className, int caretHeightPx = 0)
    {
        string? family = null;
        float? sizeDip = null;
        int? weight = null;

        // 1) UIA caret/selection attributes (Win11 Notepad / rich text).
        TryReadUia(hwnd, ref family, ref sizeDip, ref weight);

        // 2) WM_GETFONT / LOGFONT (classic Edit / many RichEdit hosts).
        TryReadWin32(hwnd, ref family, ref sizeDip, ref weight);

        // Drop implausibly tiny sizes (bad UIA units) so caret/LOGFONT can win.
        if (sizeDip is <= 6f)
        {
            sizeDip = null;
        }

        // 3) Caret pixel height → DIP when size is still unknown (esp. Win11 Notepad).
        if ((sizeDip is null or <= 0) && caretHeightPx > 0)
        {
            var dpi = NativeMethods.GetWindowDpi(hwnd);
            var fromCaret = PixelsToDip(caretHeightPx, dpi);
            if (fromCaret > 0)
            {
                sizeDip = fromCaret;
            }
        }

        // 4) Sensible class / host fallbacks when live probes fail.
        ApplyFallbacks(hwnd, className, ref family, ref sizeDip, ref weight);

        return new Result(family, sizeDip, weight);
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

    private static void TryReadUia(IntPtr hwnd, ref string? family, ref float? sizeDip, ref int? weight)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (element is null)
            {
                return;
            }

            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) ||
                patternObj is not TextPattern textPattern)
            {
                return;
            }

            var range = textPattern.GetSelection().FirstOrDefault() ?? textPattern.DocumentRange;
            var fontNameObj = range.GetAttributeValue(TextPattern.FontNameAttribute);
            var fontSizeObj = range.GetAttributeValue(TextPattern.FontSizeAttribute);
            var fontWeightObj = range.GetAttributeValue(TextPattern.FontWeightAttribute);

            if (fontNameObj is string name &&
                !string.IsNullOrWhiteSpace(name) &&
                fontNameObj != AutomationElement.NotSupported)
            {
                family ??= name.Trim();
            }

            // UIA FontSize is documented in points.
            if (TryCoercePositiveFloat(fontSizeObj, out var points) && points > 0)
            {
                sizeDip ??= PointsToDip(points);
            }

            if (TryCoercePositiveFloat(fontWeightObj, out var w) && w > 0)
            {
                weight ??= (int)Math.Round(w);
            }
        }
        catch
        {
            // Fall through to Win32 / caret.
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
                // Negative = character (em) height; positive = cell height — both map to WPF FontSize in DIPs.
                var dip = PixelsToDip(Math.Abs(lf.lfHeight), dpi);
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
        string className,
        ref string? family,
        ref float? sizeDip,
        ref int? weight)
    {
        var isEdit = className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                     className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase);

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var rootClass = root != IntPtr.Zero ? NativeMethods.GetWindowClass(root) : string.Empty;
        var isNotepad = rootClass.Equals("Notepad", StringComparison.OrdinalIgnoreCase) ||
                        rootClass.Equals("NotepadWindow", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Notepad", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(family))
        {
            if (isNotepad)
            {
                // Win11 Store Notepad default; classic Notepad often Consolas.
                family = "Cascadia Mono";
            }
            else if (isEdit)
            {
                family = "Consolas";
            }
        }

        if (sizeDip is null or <= 0)
        {
            // 11pt → DIPs; matches common Notepad / Edit defaults more closely than raw "12".
            sizeDip = PointsToDip(11f);
        }

        weight ??= 400;
    }

    private static bool TryCoercePositiveFloat(object? value, out float result)
    {
        result = 0f;
        if (value is null || value == AutomationElement.NotSupported)
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
