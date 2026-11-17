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
    /// Legacy suffix-style display (prefix remainder / full spelling word).
    /// The caret-anchored suggestion bubble shows <see cref="FullWord"/> instead.
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
