namespace WinGhostText.Core;

public enum SuggestionKind
{
    Prefix,
    Spelling,
    Grammar,
    Capitalization
}

public sealed record CompletionSuggestion(
    string TypedPrefix,
    string FullWord,
    SuggestionKind Kind)
{
    /// <summary>
    /// Text to inject (suffix for prefix completion; full replacement for marks).
    /// </summary>
    public string InsertionText =>
        ReplaceTypedPrefix
            ? FullWord
            : FullWord.Substring(Math.Min(TypedPrefix.Length, FullWord.Length));

    /// <summary>
    /// Legacy suffix-style display for prefix completions; correction marks show the fix word.
    /// The caret-anchored bubble prefers <see cref="DisplayWord"/>.
    /// </summary>
    public string GhostDisplay =>
        Kind == SuggestionKind.Prefix
            ? FullWord.Substring(Math.Min(TypedPrefix.Length, FullWord.Length))
            : DisplayWord;

    /// <summary>
    /// Word shown in the suggestion bubble (trims trailing delimiters kept for accept).
    /// </summary>
    public string DisplayWord
    {
        get
        {
            var s = FullWord;
            var i = s.Length - 1;
            while (i >= 0 && LocalMarkEngine.IsWordEndDelimiter(s[i]))
            {
                i--;
            }

            return i < 0 ? s.Trim() : s[..(i + 1)];
        }
    }

    /// <summary>
    /// When true, accept should delete the typed span before inserting FullWord.
    /// </summary>
    public bool ReplaceTypedPrefix => Kind != SuggestionKind.Prefix;

    /// <summary>True for word-end spelling / grammar / capitalization marks.</summary>
    public bool IsCorrectionMark =>
        Kind is SuggestionKind.Spelling or SuggestionKind.Grammar or SuggestionKind.Capitalization;
}
