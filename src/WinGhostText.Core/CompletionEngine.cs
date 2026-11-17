namespace WinGhostText.Core;

/// <summary>
/// Local English GhostText engine: prefix completion with light spelling fallback,
/// plus a word-end mark pipeline for spelling / grammar / capitalization.
/// </summary>
public sealed class CompletionEngine
{
    public const int DefaultMinPrefixLength = 3;

    private readonly Trie _trie;
    private readonly SpellingCorrector _spelling;
    private readonly IReadOnlyDictionary<string, string> _misspellings;
    private readonly LocalMarkEngine _marks;
    private readonly ProperNounLexicon _properNouns;
    private readonly int _minPrefixLength;
    private readonly int _knownWordCount;

    public CompletionEngine(
        Trie trie,
        SpellingCorrector spelling,
        IReadOnlyDictionary<string, string> misspellings,
        LocalMarkEngine marks,
        ProperNounLexicon properNouns,
        int knownWordCount,
        int minPrefixLength = DefaultMinPrefixLength)
    {
        _trie = trie;
        _spelling = spelling;
        _misspellings = misspellings;
        _marks = marks;
        _properNouns = properNouns;
        _knownWordCount = knownWordCount;
        _minPrefixLength = minPrefixLength;
    }

    public static CompletionEngine CreateDefault(int minPrefixLength = DefaultMinPrefixLength)
    {
        var lex = WordListLoader.LoadEmbedded();
        // Known misspellings that leaked into frequency corpora should not count as valid words.
        foreach (var bad in lex.Misspellings.Keys)
        {
            lex.Trie.RemoveWord(bad);
        }

        var frequencyForSpelling = lex.FrequencyWords.Where(w => !lex.Misspellings.ContainsKey(w));
        var spelling = new SpellingCorrector(lex.Trie, frequencyForSpelling, lex.Misspellings);

        // Membership allowlist: frequency + extras + proper nouns (lowercase).
        // Exclude known misspellings so word-end marks can still fire (e.g. teh → the).
        var known = new HashSet<string>(
            lex.FrequencyWords.Where(w => !lex.Misspellings.ContainsKey(w)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var w in lex.ExtraKnownWords)
        {
            if (!lex.Misspellings.ContainsKey(w))
            {
                known.Add(w);
            }
        }

        foreach (var form in EnumerateProperLower(lex.ProperNouns))
        {
            known.Add(form);
        }

        var marks = new LocalMarkEngine(lex.Trie, known, spelling, lex.ProperNouns);
        return new CompletionEngine(
            lex.Trie,
            spelling,
            lex.Misspellings,
            marks,
            lex.ProperNouns,
            known.Count,
            minPrefixLength);
    }

    public int DictionarySize => _trie.WordCount;
    public int KnownWordCount => _knownWordCount;
    public int ProperNounCount => _properNouns.Count;
    public int MinPrefixLength => _minPrefixLength;
    public LocalMarkEngine Marks => _marks;
    public ProperNounLexicon ProperNouns => _properNouns;

    /// <summary>
    /// Mid-word prefix / spelling suggestion (completion wins while typing a word).
    /// </summary>
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

        // Proper nouns: allow mid-word completion from canonical form when possible.
        if (_properNouns.TryGetCanonical(prefix, out var canonical))
        {
            var lower = canonical.ToLowerInvariant();
            if (lower.StartsWith(prefix, StringComparison.Ordinal) && lower.Length > prefix.Length)
            {
                return new CompletionSuggestion(prefix, lower, SuggestionKind.Prefix);
            }

            // Exact proper-noun token while typing — no mid-word bubble.
            return null;
        }

        var corrected = _spelling.Correct(prefix);
        if (corrected is null || string.Equals(corrected, prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return new CompletionSuggestion(prefix, corrected, SuggestionKind.Spelling);
    }

    /// <summary>
    /// Word-end mark pipeline (after space/punctuation). Correction wins over completion.
    /// </summary>
    public CompletionSuggestion? SuggestMark(string textBeforeCaret) =>
        _marks.Evaluate(textBeforeCaret);

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

    private static IEnumerable<string> EnumerateProperLower(ProperNounLexicon lexicon)
    {
        // ProperNounLexicon does not expose values directly for enumeration without reflection;
        // membership is checked via Contains/TryGetCanonical. Seed common lowers via Suggest path.
        // Re-load is avoided: caller already added forms when constructing known set in CreateDefault
        // through a side channel — use a tiny probe list is wrong. Instead expose forms.
        return lexicon.CanonicalForms.Select(f => f.ToLowerInvariant());
    }
}
