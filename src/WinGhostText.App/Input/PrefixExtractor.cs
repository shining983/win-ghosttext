using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using WinGhostText.App.Native;
using WinGhostText.Core;

namespace WinGhostText.App.Input;

/// <summary>
/// Prefix + casing-preserving text before the caret (for word-end marks).
/// </summary>
internal readonly record struct PrefixReadResult(string Prefix, bool HasSelection, string BeforeCaret)
{
    public static PrefixReadResult Empty => new(string.Empty, false, string.Empty);
    public static PrefixReadResult Selected => new(string.Empty, true, string.Empty);
}

/// <summary>
/// Reads the Latin word prefix immediately before the caret.
/// Classic Edit/RichEdit prefer Win32; everything else uses UIA TextPattern
/// (with FocusedElement / shallow tree search) and ValuePattern fallbacks.
/// </summary>
internal static class PrefixExtractor
{
    private const int MaxDocumentProbeChars = 8 * 1024;
    private const int MaxTreeNodes = 48;
    private const int MaxTreeDepth = 4;

    public static PrefixReadResult TryReadPrefix(IntPtr hwnd, string className)
    {
        try
        {
            if (IsWin32Edit(className))
            {
                var win32 = ReadFromWin32Edit(hwnd);
                // Trust Win32 when it sees text or a selection. If the control
                // advertises Edit/RichEdit but exposes no Win32 text (common in
                // some hosts), fall through to UIA.
                if (win32.Prefix.Length > 0 || win32.HasSelection || Win32ReportsText(hwnd))
                {
                    return win32;
                }
            }

            return ReadFromUiAutomation(hwnd);
        }
        catch
        {
            return PrefixReadResult.Empty;
        }
    }

    private static bool IsWin32Edit(string className) =>
        className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("RichEdit20A", StringComparison.OrdinalIgnoreCase) ||
        className.Equals("RichEdit50W", StringComparison.OrdinalIgnoreCase) ||
        className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase);

    private static bool Win32ReportsText(IntPtr hwnd)
    {
        var length = (int)NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        return length > 0;
    }

    private static PrefixReadResult ReadFromWin32Edit(IntPtr hwnd)
    {
        var (start, end) = ReadWin32Selection(hwnd);
        var hasSelection = end > start;

        var length = (int)NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        if (length <= 0 || start <= 0)
        {
            return new PrefixReadResult(string.Empty, hasSelection, string.Empty);
        }

        var sb = new StringBuilder(length + 1);
        _ = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXT, (IntPtr)sb.Capacity, sb);
        var text = sb.ToString();
        if (start > text.Length)
        {
            start = text.Length;
        }

        return FromBeforeCaret(text, start, hasSelection);
    }

    /// <summary>
    /// Rich Edit caret indices are 32-bit; classic EM_GETSEL only returns 16-bit
    /// packed values and breaks past 64k characters. EM_EXGETSEL always returns 0
    /// (result is in CHARRANGE), so we pick the API by class name.
    /// </summary>
    private static (int Start, int End) ReadWin32Selection(IntPtr hwnd)
    {
        var className = NativeMethods.GetWindowClass(hwnd);
        if (className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase))
        {
            var range = new NativeMethods.CHARRANGE();
            _ = NativeMethods.SendMessage(hwnd, NativeMethods.EM_EXGETSEL, IntPtr.Zero, ref range);
            return (range.cpMin, range.cpMax);
        }

        var sel = NativeMethods.SendMessage(hwnd, NativeMethods.EM_GETSEL, IntPtr.Zero, IntPtr.Zero);
        var start = (int)(sel.ToInt64() & 0xFFFF);
        var end = (int)((sel.ToInt64() >> 16) & 0xFFFF);
        return (start, end);
    }

    private static PrefixReadResult ReadFromUiAutomation(IntPtr hwnd)
    {
        var element = ResolveTextElement(hwnd);
        if (element is null)
        {
            return PrefixReadResult.Empty;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) &&
            patternObj is TextPattern textPattern)
        {
            return ReadFromTextPattern(textPattern);
        }

        // ValuePattern fallback (many single-line WPF/WinUI/omnibox-like fields).
        // Without a caret index, assume end-of-value (common while typing).
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObj) &&
            valueObj is ValuePattern valuePattern)
        {
            var value = valuePattern.Current.Value ?? string.Empty;
            return FromBeforeCaret(value, value.Length, hasSelection: false);
        }

        return PrefixReadResult.Empty;
    }

    private static PrefixReadResult ReadFromTextPattern(TextPattern textPattern)
    {
        var selection = textPattern.GetSelection();
        if (selection.Length == 0)
        {
            // No selection ranges — try document end as last resort (optional).
            // Win11 Notepad may throw on DocumentRange; caret-based paths below still work.
            try
            {
                var docText = SafeGetText(textPattern.DocumentRange, MaxDocumentProbeChars);
                return FromBeforeCaret(docText, docText.Length, hasSelection: false);
            }
            catch
            {
                return PrefixReadResult.Empty;
            }
        }

        var caretRange = selection[0];
        var selectedText = SafeGetText(caretRange, 256);
        var hasSelection = selectedText.Length > 0;
        if (hasSelection)
        {
            return PrefixReadResult.Selected;
        }

        // Preferred: text from document start → caret, then WordPrefix at end.
        // Caps document probe so large Word/browser docs stay cheap.
        // DocumentRange is optional — Win11 Notepad RichEdit sometimes rejects it.
        try
        {
            var beforeCaret = caretRange.Clone();
            beforeCaret.MoveEndpointByRange(
                TextPatternRangeEndpoint.Start,
                textPattern.DocumentRange,
                TextPatternRangeEndpoint.Start);

            var beforeText = SafeGetText(beforeCaret, MaxDocumentProbeChars);
            if (beforeText.Length > 0)
            {
                // If the range was truncated from the left, keep a tail large
                // enough for a word prefix / grammar context and extract from its end.
                var tail = beforeText.Length > 512 ? beforeText[^512..] : beforeText;
                var prefix = WordPrefix.Extract(tail, tail.Length);
                if (prefix.Length > 0 || EndsWithNonLetter(tail))
                {
                    return FromBeforeCaret(tail, tail.Length, hasSelection: false);
                }
            }
        }
        catch
        {
            // Fall through to word-unit strategy — do not require DocumentRange.
        }

        // Fallback: expand to enclosing word, then take only the span from
        // word-start → caret (avoids including characters after the caret).
        try
        {
            var wordRange = caretRange.Clone();
            wordRange.ExpandToEnclosingUnit(TextUnit.Word);
            var toCaret = wordRange.Clone();
            toCaret.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                caretRange,
                TextPatternRangeEndpoint.Start);
            var partial = TrimWordToken(SafeGetText(toCaret, 128));
            if (CompletionEngine.IsLatinPrefix(partial) && partial.Length > 0)
            {
                // Preserve original casing in BeforeCaret for capitalization marks.
                return new PrefixReadResult(partial.ToLowerInvariant(), false, partial);
            }

            var whole = TrimWordToken(SafeGetText(wordRange, 128));
            var trailing = ExtractTrailingLatinRun(whole);
            return new PrefixReadResult(trailing, false, trailing);
        }
        catch
        {
            return PrefixReadResult.Empty;
        }
    }

    private static PrefixReadResult FromBeforeCaret(string text, int caretIndex, bool hasSelection)
    {
        if (caretIndex < 0)
        {
            caretIndex = 0;
        }

        if (caretIndex > text.Length)
        {
            caretIndex = text.Length;
        }

        var before = caretIndex <= 0
            ? string.Empty
            : text[..caretIndex];
        if (before.Length > 512)
        {
            before = before[^512..];
        }

        return new PrefixReadResult(
            WordPrefix.Extract(text, caretIndex),
            hasSelection,
            before);
    }

    /// <summary>
    /// Prefer the hwnd element; else focused UIA node; else Document/"Text editor"
    /// under the root (Win11 Notepad InputSite); else shallow TextPattern search.
    /// Do not require DocumentRange success up-front — caret/prefix may still work.
    /// </summary>
    private static AutomationElement? ResolveTextElement(IntPtr hwnd)
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
            fromHwnd = null;
        }

        if (fromHwnd is not null && SupportsTextOrValue(fromHwnd))
        {
            return fromHwnd;
        }

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null && SupportsTextOrValue(focused))
            {
                return focused;
            }
        }
        catch
        {
            // Ignore focus lookup failures.
        }

        // Win11 Notepad: hwndFocus is InputSite; text lives on Document / RichEditD2D*.
        var rootHandle = hwnd != IntPtr.Zero
            ? NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT)
            : IntPtr.Zero;
        if (rootHandle != IntPtr.Zero)
        {
            var doc = FindDocumentElement(rootHandle);
            if (doc is not null)
            {
                return doc;
            }
        }

        if (fromHwnd is not null)
        {
            var nested = FindTextPatternDescendant(fromHwnd);
            if (nested is not null)
            {
                return nested;
            }

            return fromHwnd;
        }

        return null;
    }

    private static AutomationElement? FindDocumentElement(IntPtr rootHwnd)
    {
        AutomationElement? root;
        try
        {
            root = AutomationElement.FromHandle(rootHwnd);
        }
        catch
        {
            return null;
        }

        if (root is null)
        {
            return null;
        }

        foreach (var condition in DocumentConditions())
        {
            try
            {
                var doc = root.FindFirst(TreeScope.Descendants, condition);
                if (doc is not null && SupportsTextOrValue(doc))
                {
                    return doc;
                }
            }
            catch
            {
                // Skip inaccessible conditions.
            }
        }

        return FindTextPatternDescendant(root);
    }

    private static IEnumerable<Condition> DocumentConditions()
    {
        yield return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
        yield return new PropertyCondition(AutomationElement.NameProperty, "Text editor");
        yield return new PropertyCondition(AutomationElement.AutomationIdProperty, "RichEditBox");
        yield return new PropertyCondition(AutomationElement.ClassNameProperty, "RichEditD2DPT");
        yield return new PropertyCondition(AutomationElement.ClassNameProperty, "RichEditD2DWindow");
    }

    private static bool SupportsTextOrValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out _))
            {
                return true;
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out _))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static AutomationElement? FindTextPatternDescendant(AutomationElement root)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((root, 0));
            var visited = 0;

            while (queue.Count > 0 && visited < MaxTreeNodes)
            {
                var (current, depth) = queue.Dequeue();
                visited++;

                if (depth > 0)
                {
                    try
                    {
                        if (current.TryGetCurrentPattern(TextPattern.Pattern, out _))
                        {
                            return current;
                        }
                    }
                    catch
                    {
                        // Skip inaccessible nodes.
                    }
                }

                if (depth >= MaxTreeDepth)
                {
                    continue;
                }

                AutomationElement? child;
                try
                {
                    child = walker.GetFirstChild(current);
                }
                catch
                {
                    continue;
                }

                while (child is not null)
                {
                    queue.Enqueue((child, depth + 1));
                    try
                    {
                        child = walker.GetNextSibling(child);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string SafeGetText(TextPatternRange range, int maxLength)
    {
        try
        {
            return range.GetText(maxLength) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool EndsWithNonLetter(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        return !WordPrefix.IsLatinLetter(text[^1]);
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
