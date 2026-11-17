namespace WinGhostText.Core;

/// <summary>
/// Frequency-aware trie for English prefix completion.
/// Lower rank values mean higher frequency (0 = most common).
/// </summary>
public sealed class Trie
{
    private static readonly HashSet<string> SimpleSuffixes = new(StringComparer.Ordinal)
    {
        "s", "es", "ed", "ing", "ly", "er", "ers", "est", "ness", "d"
    };

    private readonly TrieNode _root = new();

    public int WordCount { get; private set; }

    public void Insert(string word, int frequencyRank)
    {
        if (string.IsNullOrEmpty(word))
        {
            return;
        }

        var node = _root;
        foreach (var ch in word)
        {
            if (!node.Children.TryGetValue(ch, out var child))
            {
                child = new TrieNode();
                node.Children[ch] = child;
            }

            node = child;
        }

        if (!node.IsWord)
        {
            WordCount++;
        }

        node.IsWord = true;
        if (node.FrequencyRank < 0 || frequencyRank < node.FrequencyRank)
        {
            node.FrequencyRank = frequencyRank;
        }
    }

    /// <summary>
    /// Removes a word from completion eligibility without rebuilding the trie.
    /// </summary>
    public bool RemoveWord(string word)
    {
        var node = FindNode(word);
        if (node is not { IsWord: true })
        {
            return false;
        }

        node.IsWord = false;
        node.FrequencyRank = -1;
        WordCount--;
        return true;
    }

    public bool Contains(string word)
    {
        var node = FindNode(word);
        return node is { IsWord: true };
    }

    /// <summary>
    /// Returns the best completion for <paramref name="prefix"/>, or null if none.
    /// </summary>
    public string? FindBestCompletion(string prefix, int minPrefixLength = 3)
    {
        if (string.IsNullOrEmpty(prefix) || prefix.Length < minPrefixLength)
        {
            return null;
        }

        var node = FindNode(prefix);
        if (node is null)
        {
            return null;
        }

        var best = FindBestWordFrom(node, prefix);
        if (best is null || string.Equals(best, prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return best;
    }

    public IReadOnlyList<string> FindCompletions(string prefix, int limit = 5, int minPrefixLength = 3)
    {
        if (string.IsNullOrEmpty(prefix) || prefix.Length < minPrefixLength || limit <= 0)
        {
            return Array.Empty<string>();
        }

        var node = FindNode(prefix);
        if (node is null)
        {
            return Array.Empty<string>();
        }

        var results = new List<(string Word, int Rank, int Score)>();
        CollectWords(node, prefix, results, limit * 16);
        return results
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Word.Length)
            .ThenBy(x => x.Word, StringComparer.Ordinal)
            .Where(x => !string.Equals(x.Word, prefix, StringComparison.Ordinal))
            .Take(limit)
            .Select(x => x.Word)
            .ToList();
    }

    private TrieNode? FindNode(string prefix)
    {
        var node = _root;
        foreach (var ch in prefix)
        {
            if (!node.Children.TryGetValue(ch, out var child))
            {
                return null;
            }

            node = child;
        }

        return node;
    }

    private static string? FindBestWordFrom(TrieNode node, string prefix)
    {
        var scored = new List<(string Word, int Score)>();

        void Visit(TrieNode current, string built)
        {
            if (current.IsWord && current.FrequencyRank >= 0 && built.Length > prefix.Length)
            {
                scored.Add((built, Score(built, prefix, current.FrequencyRank)));
            }

            foreach (var (ch, child) in current.Children)
            {
                Visit(child, built + ch);
            }
        }

        Visit(node, prefix);
        if (scored.Count == 0)
        {
            return null;
        }

        // If a candidate is a simple inflection of another candidate, demote it.
        var words = new HashSet<string>(scored.Select(x => x.Word), StringComparer.Ordinal);
        for (var i = 0; i < scored.Count; i++)
        {
            var word = scored[i].Word;
            foreach (var suf in new[] { "ly", "ed", "ing", "er", "ers", "est", "s", "es", "ness" })
            {
                if (word.Length <= suf.Length || !word.EndsWith(suf, StringComparison.Ordinal))
                {
                    continue;
                }

                var stem = word[..^suf.Length];
                if (words.Contains(stem) || string.Equals(stem, prefix, StringComparison.Ordinal))
                {
                    scored[i] = (word, scored[i].Score + 1500);
                    break;
                }
            }
        }

        return scored
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Word.Length)
            .ThenBy(x => x.Word, StringComparer.Ordinal)
            .Select(x => x.Word)
            .First();
    }

    private static int Score(string word, string prefix, int frequencyRank)
    {
        var suffix = word.Substring(prefix.Length);
        var score = frequencyRank;
        if (SimpleSuffixes.Contains(suffix))
        {
            score += 1500;
        }

        return score;
    }

    private static void CollectWords(
        TrieNode node,
        string built,
        List<(string Word, int Rank, int Score)> results,
        int maxCollect)
    {
        if (results.Count >= maxCollect)
        {
            return;
        }

        if (node.IsWord && node.FrequencyRank >= 0 && built.Length > 0)
        {
            // prefix is unknown here for scoring suffix; use rank only for list API.
            results.Add((built, node.FrequencyRank, node.FrequencyRank));
        }

        foreach (var (ch, child) in node.Children.OrderBy(kv => kv.Key))
        {
            if (results.Count >= maxCollect)
            {
                break;
            }

            CollectWords(child, built + ch, results, maxCollect);
        }
    }

    private sealed class TrieNode
    {
        public Dictionary<char, TrieNode> Children { get; } = new();
        public bool IsWord { get; set; }
        public int FrequencyRank { get; set; } = -1;
    }
}
