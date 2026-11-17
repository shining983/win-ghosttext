using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using WinGhostText.App.Native;
using WinGhostText.Core;

namespace WinGhostText.App.Input;

internal static class PrefixExtractor
{
    public static (string Prefix, bool HasSelection) TryReadPrefix(IntPtr hwnd, string className)
    {
        try
        {
            if (IsWin32Edit(className))
            {
                return ReadFromWin32Edit(hwnd);
            }

            return ReadFromUiAutomation(hwnd);
        }
        catch
        {
            return (string.Empty, false);
        }
    }

    private static bool IsWin32Edit(string className) =>
        className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("RichEdit20A", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("RichEdit50W", StringComparison.OrdinalIgnoreCase) ||
        className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase);

    private static (string Prefix, bool HasSelection) ReadFromWin32Edit(IntPtr hwnd)
    {
        var sel = NativeMethods.SendMessage(hwnd, NativeMethods.EM_GETSEL, IntPtr.Zero, IntPtr.Zero);
        var start = (int)(sel.ToInt64() & 0xFFFF);
        var end = (int)((sel.ToInt64() >> 16) & 0xFFFF);
        var hasSelection = end > start;

        var length = (int)NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        if (length <= 0 || start <= 0)
        {
            return (string.Empty, hasSelection);
        }

        var sb = new StringBuilder(length + 1);
        _ = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXT, (IntPtr)sb.Capacity, sb);
        var text = sb.ToString();
        if (start > text.Length)
        {
            start = text.Length;
        }

        return (WordPrefix.Extract(text, start), hasSelection);
    }

    private static (string Prefix, bool HasSelection) ReadFromUiAutomation(IntPtr hwnd)
    {
        var element = AutomationElement.FromHandle(hwnd);
        if (element is null)
        {
            return (string.Empty, false);
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) &&
            patternObj is TextPattern textPattern)
        {
            var selection = textPattern.GetSelection();
            var hasSelection = selection.Length > 0 &&
                               selection[0].GetText(-1).Length > 0;

            var caretRange = selection.Length > 0
                ? selection[0]
                : textPattern.GetVisibleRanges().FirstOrDefault() ?? textPattern.DocumentRange;

            // Expand backward for the current word using a clone moved to the selection start.
            var range = caretRange.Clone();
            range.ExpandToEnclosingUnit(TextUnit.Word);
            var word = range.GetText(128) ?? string.Empty;

            // If there is a selection spanning multiple chars, hide suggestions.
            if (hasSelection)
            {
                return (string.Empty, true);
            }

            // Prefer characters immediately before caret inside the word range.
            // UIA word units can include trailing spaces — trim carefully.
            word = TrimWordToken(word);
            if (!CompletionEngine.IsLatinPrefix(word) || word.Length == 0)
            {
                // Fallback: document text up to selection.
                try
                {
                    var doc = textPattern.DocumentRange.GetText(8 * 1024) ?? string.Empty;
                    var selText = selection.Length > 0 ? selection[0].GetText(-1) : string.Empty;
                    // Without a reliable caret index, use trailing Latin run of the word unit.
                    return (ExtractTrailingLatinRun(word), false);
                }
                catch
                {
                    return (ExtractTrailingLatinRun(word), false);
                }
            }

            return (word.ToLowerInvariant(), false);
        }

        // ValuePattern fallback (single-line)
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObj) &&
            valueObj is ValuePattern valuePattern)
        {
            var value = valuePattern.Current.Value ?? string.Empty;
            return (WordPrefix.Extract(value, value.Length), false);
        }

        return (string.Empty, false);
    }

    private static string TrimWordToken(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return string.Empty;
        }

        return word.Trim().Trim('\u200b', '\r', '\n', '\t');
    }

    private static string ExtractTrailingLatinRun(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var end = text.Length - 1;
        while (end >= 0 && !WordPrefix.IsLatinLetter(text[end]))
        {
            end--;
        }

        if (end < 0)
        {
            return string.Empty;
        }

        var start = end;
        while (start >= 0 && WordPrefix.IsLatinLetter(text[start]))
        {
            start--;
        }

        return text[(start + 1)..(end + 1)].ToLowerInvariant();
    }
}
