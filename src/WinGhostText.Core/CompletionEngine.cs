namespace WinGhostText.Core;

/// <summary>
/// Local English GhostText engine: prefix completion with light spelling fallback.
/// </summary>
public sealed class CompletionEngine
{
    public const int DefaultMinPrefixLength = 3;

    private readonly Trie _trie;
    private readonly SpellingCorrector _spelling;
    private readonly IReadOnlyDictionary<string, string> _misspellings;
    private readonly int _minPrefixLength;

    public CompletionEngine(
        Trie trie,
        SpellingCorrector spelling,
        IReadOnlyDictionary<string, string> misspellings,
        int minPrefixLength = DefaultMinPrefixLength)
    {
        _trie = trie;
        _spelling = spelling;
        _misspellings = misspellings;
        _minPrefixLength = minPrefixLength;
    }

    public static CompletionEngine CreateDefault(int minPrefixLength = DefaultMinPrefixLength)
    {
        var (trie, words, miss) = WordListLoader.LoadEmbedded();
        // Known misspellings that leaked into frequency corpora should not count as valid words.
        foreach (var bad in miss.Keys)
        {
            trie.RemoveWord(bad);
        }

        var spelling = new SpellingCorrector(trie, words.Where(w => !miss.ContainsKey(w)), miss);
        return new CompletionEngine(trie, spelling, miss, minPrefixLength);
    }

    public int DictionarySize => _trie.WordCount;
    public int MinPrefixLength => _minPrefixLength;

    public CompletionSuggestion? Suggest(string typedPrefix)
    {
        if (string.IsNullOrEmpty(typedPrefix))
        {
            return null;
        }

        if (!IsLatinPrefix(typedPrefix))
        {
            return null;
        }

        var prefix = typedPrefix.ToLowerInvariant();
        if (prefix.Length < _minPrefixLength)
        {
            return null;
        }

        // Prefer explicit spelling fixes before treating a typo as a valid token.
        if (_misspellings.TryGetValue(prefix, out var mapped) &&
            !string.Equals(mapped, prefix, StringComparison.Ordinal))
        {
            return new CompletionSuggestion(prefix, mapped, SuggestionKind.Spelling);
        }

        var completion = _trie.FindBestCompletion(prefix, _minPrefixLength);
        if (completion is not null)
        {
            return new CompletionSuggestion(prefix, completion, SuggestionKind.Prefix);
        }

        var corrected = _spelling.Correct(prefix);
        if (corrected is null || string.Equals(corrected, prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return new CompletionSuggestion(prefix, corrected, SuggestionKind.Spelling);
    }

    public static bool IsLatinPrefix(string text)
    {
        foreach (var ch in text)
        {
            if (ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
            {
                continue;
            }

            return false;
        }

        return text.Length > 0;
    }
}
