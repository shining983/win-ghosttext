using System.Reflection;

namespace WinGhostText.Core;

public static class WordListLoader
{
    public const string EmbeddedFreqResource = "WinGhostText.Core.Data.english-freq.txt";
    public const string EmbeddedMisspellResource = "WinGhostText.Core.Data.common-misspellings.txt";

    public static (Trie Trie, IReadOnlyList<string> Words, IReadOnlyDictionary<string, string> Misspellings)
        LoadEmbedded()
    {
        var assembly = typeof(WordListLoader).Assembly;
        using var freqStream = OpenRequired(assembly, EmbeddedFreqResource);
        using var missStream = OpenRequired(assembly, EmbeddedMisspellResource);
        return Load(freqStream, missStream);
    }

    /// <summary>
    /// Optional external word list: lines of "word" or "word\\trank".
    /// </summary>
    public static (Trie Trie, IReadOnlyList<string> Words, IReadOnlyDictionary<string, string> Misspellings)
        LoadFromFiles(string frequencyPath, string? misspellingsPath = null)
    {
        using var freq = File.OpenRead(frequencyPath);
        Stream? miss = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(misspellingsPath) && File.Exists(misspellingsPath))
            {
                miss = File.OpenRead(misspellingsPath);
            }
            else
            {
                miss = OpenRequired(typeof(WordListLoader).Assembly, EmbeddedMisspellResource);
            }

            return Load(freq, miss);
        }
        finally
        {
            miss?.Dispose();
        }
    }

    public static (Trie Trie, IReadOnlyList<string> Words, IReadOnlyDictionary<string, string> Misspellings)
        Load(Stream frequencyStream, Stream misspellingsStream)
    {
        var trie = new Trie();
        var words = new List<string>();
        using (var reader = new StreamReader(frequencyStream))
        {
            string? line;
            var autoRank = 0;
            while ((line = reader.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var parts = line.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries);
                var word = parts[0].ToLowerInvariant();
                if (!IsValidWord(word))
                {
                    continue;
                }

                var rank = parts.Length > 1 && int.TryParse(parts[1], out var parsed)
                    ? parsed
                    : autoRank;
                trie.Insert(word, rank);
                words.Add(word);
                autoRank++;
            }
        }

        var miss = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = new StreamReader(misspellingsStream))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var parts = line.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                miss[parts[0].ToLowerInvariant()] = parts[1].ToLowerInvariant();
            }
        }

        return (trie, words, miss);
    }

    private static Stream OpenRequired(Assembly assembly, string name)
    {
        var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            var available = string.Join(", ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Missing embedded resource '{name}'. Available: {available}");
        }

        return stream;
    }

    private static bool IsValidWord(string word)
    {
        if (word.Length < 2)
        {
            return false;
        }

        foreach (var ch in word)
        {
            if (ch is < 'a' or > 'z')
            {
                return false;
            }
        }

        return true;
    }
}
