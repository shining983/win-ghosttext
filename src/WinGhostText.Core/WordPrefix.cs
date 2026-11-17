namespace WinGhostText.Core;

public static class WordPrefix
{
    public static string Extract(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0)
        {
            return string.Empty;
        }

        if (caretIndex > text.Length)
        {
            caretIndex = text.Length;
        }

        var i = caretIndex - 1;
        while (i >= 0 && IsLatinLetter(text[i]))
        {
            i--;
        }

        var start = i + 1;
        if (start >= caretIndex)
        {
            return string.Empty;
        }

        return text[start..caretIndex].ToLowerInvariant();
    }

    public static bool IsLatinLetter(char ch) =>
        ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}
