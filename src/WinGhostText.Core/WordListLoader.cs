using System.Reflection;

namespace WinGhostText.Core;

public static class WordListLoader
{
    public const string EmbeddedFreqResource = "WinGhostText.Core.Data.english-freq.txt";
    public const string EmbeddedExtraResource = "WinGhostText.Core.Data.english-extra.txt";
    public const string EmbeddedMisspellResource = "WinGhostText.Core.Data.common-misspellings.txt";
    public const string EmbeddedProperNounResource = "WinGhostText.Core.Data.proper-nouns.txt";

    public sealed record Lexicon(
        Trie Trie,
        IReadOnlyList<string> FrequencyWords,
        IReadOnlyList<string> ExtraKnownWords,
        IReadOnlyDictionary<string, string> Misspellings,
        ProperNounLexicon ProperNouns);

    public static Lexicon LoadEmbedded()
    {
        var assembly = typeof(WordListLoader).Assembly;
        using var freqStream = OpenRequired(assembly, EmbeddedFreqResource);
        using var missStream = OpenRequired(assembly, EmbeddedMisspellResource);
        using var extraStream = OpenRequired(assembly, EmbeddedExtraResource);
        using var properStream = OpenRequired(assembly, EmbeddedProperNounResource);
        return Load(freqStream, missStream, extraStream, properStream);
    }

    /// <summary>
    /// Optional external word list: lines of "word" or "word\\trank".
    /// </summary>
    public static Lexicon LoadFromFiles(
        string frequencyPath,
        string? misspellingsPath = null,
        string? extraPath = null,
        string? properNounsPath = null)
    {
        using var freq = File.OpenRead(frequencyPath);
        Stream? miss = null;
        Stream? extra = null;
        Stream? proper = null;
        try
        {
            var assembly = typeof(WordListLoader).Assembly;
            miss = OpenFileOrEmbedded(misspellingsPath, assembly, EmbeddedMisspellResource);
            extra = OpenFileOrEmbedded(extraPath, assembly, EmbeddedExtraResource);
            proper = OpenFileOrEmbedded(properNounsPath, assembly, EmbeddedProperNounResource);
            return Load(freq, miss, extra, proper);
        }
        finally
        {
            miss?.Dispose();
            extra?.Dispose();
            proper?.Dispose();
        }
    }

    public static Lexicon Load(
        Stream frequencyStream,
        Stream misspellingsStream,
        Stream? extraKnownStream = null,
        Stream? properNounsStream = null)
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

        var extra = new List<string>();
        if (extraKnownStream is not null)
        {
            using var reader = new StreamReader(extraKnownStream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var word = line.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries)[0]
                    .ToLowerInvariant();
                if (IsValidWord(word))
                {
                    extra.Add(word);
                }
            }
        }

        var properForms = new List<string>();
        if (properNounsStream is not null)
        {
            using var reader = new StreamReader(properNounsStream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                properForms.Add(line);
            }
        }

        return new Lexicon(trie, words, extra, miss, new ProperNounLexicon(properForms));
    }

    private static Stream OpenFileOrEmbedded(string? path, Assembly assembly, string resource)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.OpenRead(path);
        }

        return OpenRequired(assembly, resource);
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
