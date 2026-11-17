namespace WinGhostText.Core;

/// <summary>
/// Light spelling correction: common-misspelling table first, adjacent transposition,
/// then bounded Damerau-Levenshtein search against dictionary words of similar length.
/// </summary>
public sealed class SpellingCorrector
{
    private readonly Trie _trie;
    private readonly Dictionary<string, string> _common;
    private readonly Dictionary<int, List<string>> _byLength;

    public SpellingCorrector(
        Trie trie,
        IEnumerable<string> dictionaryWords,
        IReadOnlyDictionary<string, string> commonMisspellings)
    {
        _trie = trie;
        _common = new Dictionary<string, string>(commonMisspellings, StringComparer.OrdinalIgnoreCase);
        _byLength = dictionaryWords
            .GroupBy(w => w.Length)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Returns a corrected word when confident; otherwise null.
    /// Does not return the input itself.
    /// </summary>
    public string? Correct(string typed, int maxEditDistance = 2)
    {
        if (string.IsNullOrWhiteSpace(typed) || typed.Length < 3)
        {
            return null;
        }

        var key = typed.ToLowerInvariant();
        if (_trie.Contains(key))
        {
            return null;
        }

        if (_common.TryGetValue(key, out var mapped) &&
            !string.Equals(mapped, key, StringComparison.Ordinal))
        {
            return mapped;
        }

        // Explicit adjacent transposition (Damerau): length ≥ 4, any swap that lands in-dict.
        if (key.Length >= 4)
        {
            var transposed = TryAdjacentTransposition(key);
            if (transposed is not null)
            {
                return transposed;
            }
        }

        // Short words: maxDist=1; length ≥ 6 may allow 2. Transposition counts as 1 (Damerau).
        var effectiveMax = key.Length >= 6 ? Math.Min(maxEditDistance, 2) : 1;
        if (effectiveMax < 1)
        {
            return null;
        }

        string? best = null;
        var bestDist = int.MaxValue;
        var ties = 0;

        for (var len = Math.Max(2, key.Length - effectiveMax);
             len <= key.Length + effectiveMax;
             len++)
        {
            if (!_byLength.TryGetValue(len, out var candidates))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                // Soft first-letter preference: allow mismatch only when lengths match
                // (covers first-two-letter transposition already handled above, plus rare cases).
                if (candidate[0] != key[0] &&
                    (key.Length != candidate.Length || effectiveMax < 2))
                {
                    continue;
                }

                var dist = DamerauLevenshtein(key, candidate, effectiveMax);
                if (dist <= 0 || dist > effectiveMax)
                {
                    continue;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                    ties = 1;
                }
                else if (dist == bestDist)
                {
                    ties++;
                    // Prefer the candidate closer in length, then ordinal.
                    if (best is not null)
                    {
                        var bestLenDelta = Math.Abs(best.Length - key.Length);
                        var candLenDelta = Math.Abs(candidate.Length - key.Length);
                        if (candLenDelta < bestLenDelta ||
                            (candLenDelta == bestLenDelta &&
                             string.CompareOrdinal(candidate, best) < 0))
                        {
                            best = candidate;
                        }
                    }
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        // Ambiguous distance-2 matches: skip.
        if (bestDist >= 2 && ties > 3)
        {
            return null;
        }

        return best;
    }

    /// <summary>
    /// Tries every adjacent letter swap; returns the first in-dictionary result
    /// (preferring the candidate closest to the typed form when multiple match).
    /// </summary>
    public string? TryAdjacentTransposition(string key)
    {
        if (key.Length < 4)
        {
            return null;
        }

        string? best = null;
        for (var i = 0; i < key.Length - 1; i++)
        {
            if (key[i] == key[i + 1])
            {
                continue;
            }

            var chars = key.ToCharArray();
            (chars[i], chars[i + 1]) = (chars[i + 1], chars[i]);
            var candidate = new string(chars);
            if (!_trie.Contains(candidate))
            {
                continue;
            }

            if (best is null || string.CompareOrdinal(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Damerau-Levenshtein (optimal string alignment) distance with early exit when
    /// exceeding <paramref name="max"/>. Adjacent transposition costs 1.
    /// Returns -1 if distance would exceed max.
    /// </summary>
    public static int DamerauLevenshtein(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max)
        {
            return -1;
        }

        if (a.Length == 0)
        {
            return b.Length <= max ? b.Length : -1;
        }

        if (b.Length == 0)
        {
            return a.Length <= max ? a.Length : -1;
        }

        // dp[i,j] with two previous rows for transposition lookback.
        var prev2 = new int[b.Length + 1];
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var del = prev[j] + 1;
                var ins = curr[j - 1] + 1;
                var sub = prev[j - 1] + cost;
                var value = Math.Min(Math.Min(del, ins), sub);

                // Adjacent transposition.
                if (i > 1 && j > 1 &&
                    a[i - 1] == b[j - 2] &&
                    a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, prev2[j - 2] + 1);
                }

                curr[j] = value;
                if (value < rowMin)
                {
                    rowMin = value;
                }
            }

            if (rowMin > max)
            {
                return -1;
            }

            var tmp = prev2;
            prev2 = prev;
            prev = curr;
            curr = tmp;
            // After rotate, curr is stale prev2 buffer — zero not required (fully overwritten).
        }

        return prev[b.Length] <= max ? prev[b.Length] : -1;
    }

    /// <summary>
    /// Classic Levenshtein (no transposition). Prefer <see cref="DamerauLevenshtein"/> for correction.
    /// </summary>
    public static int Levenshtein(string a, string b, int max) =>
        DamerauLevenshtein(a, b, max);
}
