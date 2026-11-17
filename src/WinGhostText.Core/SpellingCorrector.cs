namespace WinGhostText.Core;

/// <summary>
/// Light spelling correction: common-misspelling table first, then
/// bounded edit-distance search against dictionary words of similar length.
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

        // Distance-2 only for longer tokens to limit false positives / cost.
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
                // Require shared first character for distance searches.
                if (candidate[0] != key[0])
                {
                    continue;
                }

                // For distance 2, also require a shared second char when both long enough.
                if (effectiveMax >= 2 &&
                    key.Length >= 4 &&
                    candidate.Length >= 4 &&
                    candidate[1] != key[1] &&
                    candidate[2] != key[2])
                {
                    continue;
                }

                var dist = Levenshtein(key, candidate, effectiveMax);
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
    /// Levenshtein distance with early exit when exceeding <paramref name="max"/>.
    /// Returns -1 if distance would exceed max.
    /// </summary>
    public static int Levenshtein(string a, string b, int max)
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
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
                if (curr[j] < rowMin)
                {
                    rowMin = curr[j];
                }
            }

            if (rowMin > max)
            {
                return -1;
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length] <= max ? prev[b.Length] : -1;
    }
}
