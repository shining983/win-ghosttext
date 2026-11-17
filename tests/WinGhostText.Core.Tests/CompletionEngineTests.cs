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
    [InlineData("teh", "the")]
    [InlineData("wiht", "with")]
    [InlineData("taht", "that")]
    public void Common_misspellings_correct(string typed, string expected)
    {
        var suggestion = _engine.Suggest(typed);
        Assert.NotNull(suggestion);
        Assert.Equal(SuggestionKind.Spelling, suggestion!.Kind);
        Assert.Equal(expected, suggestion.FullWord);
        Assert.True(suggestion.ReplaceTypedPrefix);
        Assert.Equal(expected, suggestion.InsertionText);
        Assert.Equal(expected, suggestion.GhostDisplay);
    }

    [Fact]
    public void Spelling_accept_path_replaces_typed_prefix()
    {
        var suggestion = _engine.Suggest("becuase");
        Assert.NotNull(suggestion);
        Assert.True(suggestion!.ReplaceTypedPrefix);
        Assert.Equal("becuase", suggestion.TypedPrefix);
        Assert.Equal("because", suggestion.FullWord);
        Assert.Equal("because", suggestion.InsertionText);
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
        var miss = new Dictionary<string, string>();
        var spelling = new SpellingCorrector(trie, new[] { "xyzzy" }, miss);
        var proper = new ProperNounLexicon(Array.Empty<string>());
        var marks = new LocalMarkEngine(trie, new[] { "xyzzy" }, spelling, proper);
        var engine = new CompletionEngine(trie, spelling, miss, marks, proper, knownWordCount: 1);
        Assert.Null(engine.Suggest("xyzzy"));
    }

    [Fact]
    public void Dictionary_loads_tens_of_thousands()
    {
        Assert.True(_engine.DictionarySize >= 70_000, $"Got {_engine.DictionarySize}");
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
    public void Damerau_counts_adjacent_transposition_as_one()
    {
        Assert.Equal(1, SpellingCorrector.DamerauLevenshtein("becuase", "because", 2));
        Assert.Equal(1, SpellingCorrector.DamerauLevenshtein("recieve", "receive", 2));
        Assert.Equal(1, SpellingCorrector.DamerauLevenshtein("adress", "address", 2));
        Assert.Equal(-1, SpellingCorrector.DamerauLevenshtein("abcdef", "ghijkl", 2));
    }

    [Fact]
    public void Adjacent_transposition_finds_dictionary_word()
    {
        var trie = new Trie();
        trie.Insert("because", 10);
        trie.Insert("receive", 10);
        trie.Insert("from", 10);
        var spelling = new SpellingCorrector(
            trie,
            new[] { "because", "receive", "from" },
            new Dictionary<string, string>());

        Assert.Equal("because", spelling.Correct("becuase"));
        Assert.Equal("receive", spelling.Correct("recieve"));
        Assert.Equal("from", spelling.Correct("form"));
    }

    [Fact]
    public void Explicit_transposition_requires_length_at_least_four()
    {
        var trie = new Trie();
        trie.Insert("the", 10);
        trie.Insert("because", 10);
        var spelling = new SpellingCorrector(
            trie,
            new[] { "the", "because" },
            new Dictionary<string, string>());

        // Length 3: adjacent-swap helper skips; length ≥ 4 finds dictionary hits.
        Assert.Null(spelling.TryAdjacentTransposition("teh"));
        Assert.Equal("because", spelling.TryAdjacentTransposition("becuase"));
    }

    [Fact]
    public void Short_words_use_max_distance_one()
    {
        var trie = new Trie();
        trie.Insert("apple", 10);
        trie.Insert("apply", 10);
        // "apxle" → "apple" is 1 substitution; "aqxle" needs 2 and should fail for length 5.
        var spelling = new SpellingCorrector(
            trie,
            new[] { "apple", "apply" },
            new Dictionary<string, string>());

        Assert.Equal("apple", spelling.Correct("apxle"));
        Assert.Null(spelling.Correct("aqxle"));
    }

    [Fact]
    public void Long_words_allow_distance_two()
    {
        var trie = new Trie();
        trie.Insert("environment", 10);
        var spelling = new SpellingCorrector(
            trie,
            new[] { "environment" },
            new Dictionary<string, string>());

        // Two substitutions on a length-11 token (needs maxDist=2).
        Assert.Equal("environment", spelling.Correct("envixanment"));
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
