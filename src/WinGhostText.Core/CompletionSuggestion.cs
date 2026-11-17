namespace WinGhostText.Core;

public enum SuggestionKind
{
    Prefix,
    Spelling
}

public sealed record CompletionSuggestion(
    string TypedPrefix,
    string FullWord,
    SuggestionKind Kind)
{
    /// <summary>
    /// Text to inject (suffix for prefix completion; full replacement word for spelling).
    /// </summary>
    public string InsertionText =>
        Kind == SuggestionKind.Spelling
            ? FullWord
            : FullWord.Substring(TypedPrefix.Length);

    /// <summary>
    /// GhostText display string shown after the caret.
    /// Prefix completions show the unfinished suffix; spelling always shows the full
    /// corrected word so the replace-on-Tab path stays readable (not an empty/opaque hint).
    /// </summary>
    public string GhostDisplay =>
        Kind == SuggestionKind.Spelling
            ? FullWord
            : FullWord.Substring(TypedPrefix.Length);

    /// <summary>
    /// When true, accept should delete the typed prefix before inserting FullWord.
    /// </summary>
    public bool ReplaceTypedPrefix => Kind == SuggestionKind.Spelling;
}
