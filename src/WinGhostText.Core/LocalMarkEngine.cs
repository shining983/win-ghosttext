namespace WinGhostText.Core;

/// <summary>
/// Word-end offline mark pipeline: capitalization, proper-noun casing,
/// light grammar, then spelling. Returns a replacement span relative to caret.
/// </summary>
public sealed class LocalMarkEngine
{
    private readonly Trie _trie;
    private readonly HashSet<string> _known;
    private readonly SpellingCorrector _spelling;
    private readonly ProperNounLexicon _properNouns;

    public LocalMarkEngine(
        Trie trie,
        IEnumerable<string> knownWords,
        SpellingCorrector spelling,
        ProperNounLexicon properNouns)
    {
        _trie = trie;
        _known = new HashSet<string>(knownWords, StringComparer.OrdinalIgnoreCase);
        _spelling = spelling;
        _properNouns = properNouns;
    }

    /// <summary>
    /// Inspect text immediately before the caret after a word-ending key (space/punct).
    /// Returns a suggestion whose <see cref="CompletionSuggestion.TypedPrefix"/> is the
    /// exact trailing span to delete (including any trailing delimiter) and
    /// <see cref="CompletionSuggestion.FullWord"/> is the replacement (same delimiter kept).
    /// </summary>
    public CompletionSuggestion? Evaluate(string textBeforeCaret)
    {
        if (string.IsNullOrEmpty(textBeforeCaret))
        {
            return null;
        }

        if (!TryParseTrailing(textBeforeCaret, out var wordStart, out var wordEnd, out var word,
                out var trailing))
        {
            return null;
        }

        // 1) Lone pronoun i → I (any position).
        var pronoun = LocalGrammarRules.SuggestPronounI(word);
        if (pronoun is not null)
        {
            return Make(word, pronoun, trailing, SuggestionKind.Capitalization);
        }

        // 2) Grammar pairs (a/an, double modal, light SVA) — before sentence caps / spelling.
        if (TryPreviousWord(textBeforeCaret, wordStart, out var prevStart, out var prevWord))
        {
            var article = LocalGrammarRules.SuggestArticle(prevWord, word);
            if (article is not null)
            {
                var typedSpan = textBeforeCaret[prevStart..wordEnd];
                var replaced = article + textBeforeCaret[(prevStart + prevWord.Length)..wordEnd];
                return new CompletionSuggestion(
                    typedSpan + trailing,
                    replaced + trailing,
                    SuggestionKind.Grammar);
            }

            var dropDup = LocalGrammarRules.SuggestDoubleModal(prevWord, word);
            if (dropDup is not null)
            {
                var typedSpan = textBeforeCaret[prevStart..wordEnd];
                return new CompletionSuggestion(
                    typedSpan + trailing,
                    prevWord + trailing,
                    SuggestionKind.Grammar);
            }

            var sva = LocalGrammarRules.SuggestLightSva(prevWord, word);
            if (sva is not null)
            {
                return Make(word, sva, trailing, SuggestionKind.Grammar);
            }
        }

        // 3) Proper-noun casing (london → London). Also suppresses spelling below.
        var proper = _properNouns.SuggestCasing(word);
        if (proper is not null)
        {
            return Make(word, proper, trailing, SuggestionKind.Capitalization);
        }

        // 4) Spelling — skip dictionary / proper-noun / known extras.
        var lower = word.ToLowerInvariant();
        var knownOk = _properNouns.Contains(word) ||
                      _trie.Contains(lower) ||
                      _known.Contains(word);
        if (!knownOk)
        {
            var corrected = _spelling.Correct(lower);
            if (corrected is not null &&
                !corrected.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                var withCase = ApplyOriginalCasePattern(word, corrected);
                return Make(word, withCase, trailing, SuggestionKind.Spelling);
            }
        }

        // 5) Sentence-start capitalization (only when the token is otherwise fine).
        if (knownOk &&
            LocalGrammarRules.IsSentenceStart(textBeforeCaret, wordStart))
        {
            var capped = LocalGrammarRules.SuggestCapitalizeWord(word);
            if (capped is not null && NeedsSentenceCap(word))
            {
                return Make(word, capped, trailing, SuggestionKind.Capitalization);
            }
        }

        return null;
    }

    private static bool NeedsSentenceCap(string word)
    {
        // Only suggest when the word looks intentionally lowercase (not already mixed/Title).
        if (string.IsNullOrEmpty(word) || char.IsUpper(word[0]))
        {
            return false;
        }

        // Skip tiny non-pronoun particles that are often left lower in titles, but still
        // capitalize normal words at sentence start (including "the", "a", …).
        return word.Length >= 1 && word.All(ch => !char.IsLetter(ch) || char.IsLower(ch));
    }

    private static CompletionSuggestion Make(
        string word,
        string replacement,
        string trailing,
        SuggestionKind kind) =>
        new(word + trailing, replacement + trailing, kind);

    private static string ApplyOriginalCasePattern(string original, string lowerCorrection)
    {
        if (original.Length == 0)
        {
            return lowerCorrection;
        }

        if (original.All(char.IsUpper))
        {
            return lowerCorrection.ToUpperInvariant();
        }

        if (char.IsUpper(original[0]))
        {
            return char.ToUpperInvariant(lowerCorrection[0]) + lowerCorrection[1..];
        }

        return lowerCorrection;
    }

    /// <summary>
    /// Parse the completed Latin word immediately before an optional trailing delimiter run
    /// (spaces and/or punctuation that just ended the word).
    /// </summary>
    internal static bool TryParseTrailing(
        string text,
        out int wordStart,
        out int wordEnd,
        out string word,
        out string trailing)
    {
        wordStart = 0;
        wordEnd = 0;
        word = string.Empty;
        trailing = string.Empty;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var end = text.Length;
        var i = end - 1;

        // Trailing spaces/punct after the word (at least one for a true word-end mark).
        while (i >= 0 && IsWordEndDelimiter(text[i]))
        {
            i--;
        }

        var delimStart = i + 1;
        if (delimStart >= end)
        {
            // No trailing delimiter — not a word-end trigger context.
            return false;
        }

        trailing = text[delimStart..end];
        wordEnd = delimStart;

        if (i < 0 || !WordPrefix.IsLatinLetter(text[i]))
        {
            return false;
        }

        while (i >= 0 && WordPrefix.IsLatinLetter(text[i]))
        {
            i--;
        }

        wordStart = i + 1;
        word = text[wordStart..wordEnd];
        return word.Length > 0;
    }

    internal static bool TryPreviousWord(
        string text,
        int currentWordStart,
        out int prevStart,
        out string prevWord)
    {
        prevStart = 0;
        prevWord = string.Empty;
        var i = currentWordStart - 1;
        while (i >= 0 && (char.IsWhiteSpace(text[i]) || text[i] is '-' or '\''))
        {
            // Only allow whitespace between words for grammar pairs.
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }

            i--;
        }

        if (i < 0 || !WordPrefix.IsLatinLetter(text[i]))
        {
            return false;
        }

        var end = i + 1;
        while (i >= 0 && WordPrefix.IsLatinLetter(text[i]))
        {
            i--;
        }

        prevStart = i + 1;
        prevWord = text[prevStart..end];
        return prevWord.Length > 0;
    }

    internal static bool IsWordEndDelimiter(char ch) =>
        char.IsWhiteSpace(ch) ||
        ch is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '"' or '\'' or
            '/' or '\\' or '—' or '–';
}
