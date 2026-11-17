using WinGhostText.Core;
using Xunit;

namespace WinGhostText.Core.Tests;

public class LocalMarkEngineTests
{
    private readonly CompletionEngine _engine = CompletionEngine.CreateDefault();

    [Fact]
    public void Word_end_spelling_teh_suggests_the()
    {
        var mark = _engine.SuggestMark("teh ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Spelling, mark!.Kind);
        Assert.Equal("teh ", mark.TypedPrefix);
        Assert.Equal("the ", mark.FullWord);
        Assert.Equal("the", mark.DisplayWord);
        Assert.True(mark.ReplaceTypedPrefix);
        Assert.True(mark.IsCorrectionMark);
    }

    [Fact]
    public void A_an_rule_suggests_an_apple()
    {
        var mark = _engine.SuggestMark("a apple ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Grammar, mark!.Kind);
        Assert.Equal("a apple ", mark.TypedPrefix);
        Assert.Equal("an apple ", mark.FullWord);
        Assert.Equal("an apple", mark.DisplayWord);
    }

    [Fact]
    public void An_consonant_suggests_a()
    {
        var mark = _engine.SuggestMark("an book ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Grammar, mark!.Kind);
        Assert.Equal("a book ", mark.FullWord);
    }

    [Fact]
    public void Pronoun_i_capitalizes()
    {
        var mark = _engine.SuggestMark("i ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Capitalization, mark!.Kind);
        Assert.Equal("i ", mark.TypedPrefix);
        Assert.Equal("I ", mark.FullWord);
        Assert.Equal("I", mark.DisplayWord);
    }

    [Fact]
    public void Sentence_start_lowercase_capitalizes()
    {
        var mark = _engine.SuggestMark("hello ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Capitalization, mark!.Kind);
        Assert.Equal("Hello ", mark.FullWord);
    }

    [Fact]
    public void After_period_sentence_start_capitalizes()
    {
        var mark = _engine.SuggestMark("Done. next ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Capitalization, mark!.Kind);
        Assert.Equal("Next ", mark.FullWord);
    }

    [Fact]
    public void Mid_sentence_common_word_not_capitalized()
    {
        Assert.Null(_engine.SuggestMark("say hello "));
    }

    [Fact]
    public void Proper_noun_lowercase_suggests_title_case()
    {
        var mark = _engine.SuggestMark("london ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Capitalization, mark!.Kind);
        Assert.Equal("London ", mark.FullWord);
        Assert.Equal("London", mark.DisplayWord);
    }

    [Fact]
    public void Proper_noun_canonical_not_flagged_as_spelling()
    {
        Assert.Null(_engine.SuggestMark("London "));
        Assert.True(_engine.ProperNouns.Contains("london"));
    }

    [Fact]
    public void Double_modal_suggests_dropping_duplicate()
    {
        var mark = _engine.SuggestMark("can can ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Grammar, mark!.Kind);
        Assert.Equal("can ", mark.FullWord);
    }

    [Fact]
    public void Light_sva_he_walk_suggests_walks()
    {
        var mark = _engine.SuggestMark("he walk ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Grammar, mark!.Kind);
        Assert.Equal("walks ", mark.FullWord);
    }

    [Fact]
    public void Correctly_spelled_mid_sentence_returns_null()
    {
        Assert.Null(_engine.SuggestMark("say the "));
        Assert.Null(_engine.SuggestMark("the environment "));
    }

    [Fact]
    public void Sentence_start_the_suggests_The()
    {
        var mark = _engine.SuggestMark("the ");
        Assert.NotNull(mark);
        Assert.Equal(SuggestionKind.Capitalization, mark!.Kind);
        Assert.Equal("The ", mark.FullWord);
    }

    [Fact]
    public void Without_trailing_delimiter_returns_null()
    {
        Assert.Null(_engine.SuggestMark("teh"));
    }
}

public class LocalGrammarRulesTests
{
    [Theory]
    [InlineData("apple", true)]
    [InlineData("orange", true)]
    [InlineData("book", false)]
    [InlineData("university", true)] // orthographic u — naive rule
    public void Vowel_sound_heuristic(string word, bool expectVowel) =>
        Assert.Equal(expectVowel, LocalGrammarRules.StartsWithVowelSound(word));

    [Theory]
    [InlineData("a", "apple", "an")]
    [InlineData("an", "book", "a")]
    [InlineData("A", "apple", "An")]
    [InlineData("a", "book", null)]
    public void Article_suggestions(string article, string next, string? expected) =>
        Assert.Equal(expected, LocalGrammarRules.SuggestArticle(article, next));

    [Fact]
    public void Sentence_start_detection()
    {
        Assert.True(LocalGrammarRules.IsSentenceStart("hello", 0));
        Assert.True(LocalGrammarRules.IsSentenceStart("Hi. there", 4));
        Assert.False(LocalGrammarRules.IsSentenceStart("say hello", 4));
    }
}

public class ProperNounLexiconTests
{
    [Fact]
    public void Embedded_proper_nouns_load()
    {
        var engine = CompletionEngine.CreateDefault();
        Assert.True(engine.ProperNounCount >= 50);
        Assert.Equal("Monday", engine.ProperNouns.SuggestCasing("monday"));
        Assert.Equal("Google", engine.ProperNouns.SuggestCasing("google"));
        Assert.Null(engine.ProperNouns.SuggestCasing("Monday"));
    }
}

public class LexiconLoadTests
{
    [Fact]
    public void Expanded_frequency_list_loads()
    {
        var engine = CompletionEngine.CreateDefault();
        Assert.True(engine.DictionarySize >= 70_000, $"Got {engine.DictionarySize}");
        Assert.True(engine.KnownWordCount >= 100_000, $"Got {engine.KnownWordCount}");
    }

    [Fact]
    public void WordListLoader_embedded_resources_present()
    {
        var lex = WordListLoader.LoadEmbedded();
        Assert.True(lex.FrequencyWords.Count >= 70_000);
        Assert.True(lex.ExtraKnownWords.Count >= 30_000);
        Assert.True(lex.ProperNouns.Count >= 50);
        Assert.Contains("because", lex.Misspellings.Values);
    }
}
