using WinGhostText.Core;
using Xunit;

namespace WinGhostText.Core.Tests;

public class CompletionEngineTests
{
    private readonly CompletionEngine _engine = CompletionEngine.CreateDefault();

    [Theory]
    [InlineData("environ", "environment")]
    [InlineData("develop", "development")]
    [InlineData("comple", "complete")]
    [InlineData("enviro", "environment")]
    public void Prefix_completion_returns_expected_word(string prefix, string expectedFull)
    {
        var suggestion = _engine.Suggest(prefix);
        Assert.NotNull(suggestion);
        Assert.Equal(SuggestionKind.Prefix, suggestion!.Kind);
        Assert.Equal(expectedFull, suggestion.FullWord);
        Assert.Equal(expectedFull.Substring(prefix.Length), suggestion.InsertionText);
        Assert.False(suggestion.ReplaceTypedPrefix);
    }

    [Fact]
    public void Prefix_shorter_than_three_returns_null()
    {
        Assert.Null(_engine.Suggest("en"));
        Assert.Null(_engine.Suggest("a"));
        Assert.Null(_engine.Suggest(""));
    }

    [Fact]
    public void Environ_ghost_suffix_is_ment()
    {
        var suggestion = _engine.Suggest("environ");
        Assert.NotNull(suggestion);
        Assert.Equal("ment", suggestion!.GhostDisplay);
    }

    [Theory]
    [InlineData("becuase", "because")]
    [InlineData("recieve", "receive")]
    [InlineData("seperate", "separate")]
    [InlineData("enviroment", "environment")]
    public void Common_misspellings_correct(string typed, string expected)
    {
        var suggestion = _engine.Suggest(typed);
        Assert.NotNull(suggestion);
        Assert.Equal(SuggestionKind.Spelling, suggestion!.Kind);
        Assert.Equal(expected, suggestion.FullWord);
        Assert.True(suggestion.ReplaceTypedPrefix);
    }

    [Fact]
    public void Non_latin_prefix_is_rejected()
    {
        Assert.Null(_engine.Suggest("环境"));
        Assert.Null(_engine.Suggest("env环境"));
    }

    [Fact]
    public void Very_rare_exact_prefix_without_continuation_returns_null()
    {
        // A made-up engine with a single exact word and no longer form.
        var trie = new Trie();
        trie.Insert("xyzzy", 0);
        var spelling = new SpellingCorrector(trie, new[] { "xyzzy" }, new Dictionary<string, string>());
        var engine = new CompletionEngine(trie, spelling, new Dictionary<string, string>());
        Assert.Null(engine.Suggest("xyzzy"));
    }

    [Fact]
    public void Dictionary_loads_tens_of_thousands()
    {
        Assert.True(_engine.DictionarySize >= 30_000, $"Got {_engine.DictionarySize}");
    }
}

public class TrieTests
{
    [Fact]
    public void Prefers_higher_frequency_completion()
    {
        var trie = new Trie();
        trie.Insert("develop", 100);
        trie.Insert("developer", 50);
        trie.Insert("development", 10);
        trie.Insert("developments", 200);

        Assert.Equal("development", trie.FindBestCompletion("develop"));
    }
}

public class SpellingCorrectorTests
{
    [Fact]
    public void Levenshtein_respects_max()
    {
        // Standard Levenshtein counts letter transposition as 2 edits.
        Assert.Equal(2, SpellingCorrector.Levenshtein("becuase", "because", 2));
        Assert.Equal(2, SpellingCorrector.Levenshtein("recieve", "receive", 2));
        Assert.Equal(1, SpellingCorrector.Levenshtein("adress", "address", 2));
        Assert.Equal(-1, SpellingCorrector.Levenshtein("abcdef", "ghijkl", 2));
    }
}

public class WordPrefixTests
{
    [Theory]
    [InlineData("hello environ", 13, "environ")]
    [InlineData("environ", 7, "environ")]
    [InlineData("hi ", 3, "")]
    [InlineData("abc def", 3, "abc")]
    public void Extracts_latin_prefix(string text, int caret, string expected)
    {
        Assert.Equal(expected, WordPrefix.Extract(text, caret));
    }
}
