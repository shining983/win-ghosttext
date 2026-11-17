using System.Windows.Automation;
using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

internal static class FontProbe
{
    public static (string? Family, float? SizePt) TryRead(IntPtr hwnd, string className)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (element is null)
            {
                return (null, null);
            }

            // UIA does not expose font universally; try TextPattern attributes when present.
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) &&
                patternObj is TextPattern textPattern)
            {
                var range = textPattern.GetSelection().FirstOrDefault() ?? textPattern.DocumentRange;
                var fontNameObj = range.GetAttributeValue(TextPattern.FontNameAttribute);
                var fontSizeObj = range.GetAttributeValue(TextPattern.FontSizeAttribute);

                string? family = fontNameObj as string;
                float? size = null;
                if (fontSizeObj is double d)
                {
                    size = (float)d;
                }

                if (!string.IsNullOrWhiteSpace(family) || size is not null)
                {
                    return (family, size);
                }
            }
        }
        catch
        {
            // Fall through to defaults.
        }

        // Notepad classic uses Consolas / Lucida Console depending on version.
        if (className.Equals("Edit", StringComparison.OrdinalIgnoreCase))
        {
            return ("Consolas", 11f);
        }

        return (null, null);
    }
}
