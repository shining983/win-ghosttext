namespace WinGhostText.Core;

/// <summary>
/// Lightweight offline grammar / capitalization heuristics — not full proofreading.
/// </summary>
public static class LocalGrammarRules
{
    private static readonly HashSet<string> Modals = new(StringComparer.OrdinalIgnoreCase)
    {
        "can", "could", "may", "might", "must", "shall", "should", "will", "would"
    };

    private static readonly HashSet<string> ThirdPersonSingularPronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "he", "she", "it"
    };

    /// <summary>
    /// Vowels for naive a/an (orthographic; ignores silent-h / yu-sound edge cases).
    /// </summary>
    public static bool StartsWithVowelSound(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return false;
        }

        var c = char.ToLowerInvariant(word[0]);
        return c is 'a' or 'e' or 'i' or 'o' or 'u';
    }

    public static string? SuggestArticle(string article, string followingWord)
    {
        if (string.IsNullOrEmpty(article) || string.IsNullOrEmpty(followingWord))
        {
            return null;
        }

        if (!article.Equals("a", StringComparison.OrdinalIgnoreCase) &&
            !article.Equals("an", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var wantAn = StartsWithVowelSound(followingWord);
        var isAn = article.Equals("an", StringComparison.OrdinalIgnoreCase);
        if (wantAn == isAn)
        {
            return null;
        }

        // Preserve capitalization of the article token.
        if (char.IsUpper(article[0]))
        {
            return wantAn ? "An" : "A";
        }

        return wantAn ? "an" : "a";
    }

    /// <summary>
    /// Double modal: "can can", "will will", etc.
    /// </summary>
    public static string? SuggestDoubleModal(string first, string second)
    {
        if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
        {
            return null;
        }

        if (!Modals.Contains(first) || !Modals.Contains(second))
        {
            return null;
        }

        if (!first.Equals(second, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return first; // drop the duplicate
    }

    /// <summary>
    /// Very light SVA: "he/she/it walk" → "walks" for plain base verbs ending without s.
    /// Only fires for short alphabetic verbs length 3–8 without irregulars.
    /// </summary>
    public static string? SuggestLightSva(string subject, string verb)
    {
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(verb))
        {
            return null;
        }

        if (!ThirdPersonSingularPronouns.Contains(subject))
        {
            return null;
        }

        if (verb.Length is < 3 or > 8)
        {
            return null;
        }

        foreach (var ch in verb)
        {
            if (ch is (< 'a' or > 'z') and (< 'A' or > 'Z'))
            {
                return null;
            }
        }

        var lower = verb.ToLowerInvariant();
        if (lower.EndsWith('s') || lower.EndsWith("ed", StringComparison.Ordinal) ||
            lower.EndsWith("ing", StringComparison.Ordinal) ||
            lower is "is" or "was" or "has" or "does" or "are" or "were" or "have" or "be" or
                "do" or "did" or "can" or "could" or "will" or "would" or "may" or "might" or
                "must" or "shall" or "should")
        {
            return null;
        }

        // Naive +s / +es
        string suggestion;
        if (lower.EndsWith('s') || lower.EndsWith('x') || lower.EndsWith('z') ||
            lower.EndsWith("ch", StringComparison.Ordinal) ||
            lower.EndsWith("sh", StringComparison.Ordinal))
        {
            suggestion = lower + "es";
        }
        else if (lower.EndsWith('y') && lower.Length > 1 &&
                 lower[^2] is not ('a' or 'e' or 'i' or 'o' or 'u'))
        {
            suggestion = lower[..^1] + "ies";
        }
        else
        {
            suggestion = lower + "s";
        }

        if (char.IsUpper(verb[0]))
        {
            suggestion = char.ToUpperInvariant(suggestion[0]) + suggestion[1..];
        }

        return suggestion;
    }

    public static string? SuggestCapitalizeWord(string word) =>
        string.IsNullOrEmpty(word) || !char.IsLetter(word[0]) || char.IsUpper(word[0])
            ? null
            : char.ToUpperInvariant(word[0]) + word[1..];

    public static string? SuggestPronounI(string word) =>
        word == "i" ? "I" : null;

    /// <summary>
    /// True when <paramref name="wordStart"/> is at the start of the text or after `.?!` + spaces.
    /// </summary>
    public static bool IsSentenceStart(string text, int wordStart)
    {
        if (wordStart <= 0)
        {
            return true;
        }

        var i = wordStart - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }

        if (i < 0)
        {
            return true;
        }

        return text[i] is '.' or '?' or '!';
    }
}
