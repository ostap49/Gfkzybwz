using Rozkladka.Core;
using Xunit;

namespace Rozkladka.Core.Tests;

public class CoreTests
{
    private static readonly TableLayout En = KnownLayouts.EnglishUs;
    private static readonly TableLayout Uk = KnownLayouts.Ukrainian;

    private sealed class FakeChecker : IWordChecker
    {
        private readonly HashSet<string> _uk = new(StringComparer.OrdinalIgnoreCase) { "привіт", "робиш", "м'ясо", "клавіатура", "жовтий" };
        private readonly HashSet<string> _en = new(StringComparer.OrdinalIgnoreCase) { "hello", "world", "keyboard", "ghost" };
        public bool? IsWord(string word, string tag) => tag.StartsWith("uk") ? _uk.Contains(word) : tag.StartsWith("en") ? _en.Contains(word) : null;
    }

    private sealed class NoDictionary : IWordChecker
    {
        public bool? IsWord(string word, string tag) => null;
    }

    /// <summary>Клавіші, якими в розкладці layout набирається text.</summary>
    private static List<KeyStroke> Keys(string text, ILayout layout)
    {
        var rev = TextConverter.ReverseMap(layout);
        return text.Select(c => rev[c]).ToList();
    }

    private static SwitchDecision? Decide(string typed, ILayout typedIn, IWordChecker? checker = null)
    {
        var other = ReferenceEquals(typedIn, En) ? (ILayout)Uk : En;
        return new SwitchDecider(checker ?? new FakeChecker()).Decide(Keys(typed, typedIn), typedIn, new[] { other });
    }

    [Theory]
    [InlineData("ghbdsn", "привіт")]
    [InlineData("Ghbdsn", "Привіт")]
    [InlineData("ghbdsn?", "привіт,")]
    [InlineData("v`zcj", "м'ясо")]
    [InlineData(";jdnbq", "жовтий")]
    [InlineData("ye", "ну")]
    [InlineData("yt", "не")]
    public void EnglishLayoutTypedUkrainian(string typed, string expected)
    {
        var d = Decide(typed, En);
        Assert.NotNull(d);
        Assert.Same(Uk, d!.Target);
        Assert.Equal(expected, d.ReplacementText);
    }

    [Theory]
    [InlineData("руддщ", "hello")]
    [InlineData("цщкдв", "world")]
    public void UkrainianLayoutTypedEnglish(string typed, string expected)
    {
        var d = Decide(typed, Uk);
        Assert.NotNull(d);
        Assert.Equal(expected, d!.ReplacementText);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("in")]
    [InlineData("teh")]       // одрук в англійському — не перемикаємо
    [InlineData("covid19")]   // слова з цифрами не чіпаємо
    [InlineData("ghost")]     // справжнє слово
    public void KeepsEnglish(string typed) => Assert.Null(Decide(typed, En));

    [Theory]
    [InlineData("привіт")]
    [InlineData("як")]
    [InlineData("пртвіт")]
    public void KeepsUkrainian(string typed) => Assert.Null(Decide(typed, Uk));

    [Fact]
    public void ExceptionsAreRespected()
    {
        var decider = new SwitchDecider(new FakeChecker());
        decider.Exceptions.Add("ghbdsn");
        Assert.Null(decider.Decide(Keys("ghbdsn", En), En, new ILayout[] { Uk }));
    }

    [Fact]
    public void WithoutDictionaryOnlyObviousGarbageIsSwitched()
    {
        // "ghbdsn" — 6 приголосних поспіль, явно не англійське слово.
        Assert.NotNull(Decide("ghbdsn", En, new NoDictionary()));
        // "ghost" правдоподібне — без словника не чіпаємо.
        Assert.Null(Decide("ghost", En, new NoDictionary()));
    }

    [Fact]
    public void ConvertsSelectedText()
    {
        Assert.Equal("Привіт, світ!", TextConverter.Convert("Ghbdsn? cdsn!", En, Uk));
        Assert.Equal("Hello world", TextConverter.Convert("Руддщ цщкдв", Uk, En));
    }

    [Fact]
    public void GuessesSourceLayout()
    {
        var layouts = new ILayout[] { En, Uk };
        Assert.Same(En, TextConverter.GuessSource("ghbdsn", layouts));
        Assert.Same(Uk, TextConverter.GuessSource("руддщ", layouts));
    }

    [Fact]
    public void PlausibilityHeuristic()
    {
        Assert.Equal(false, Plausibility.IsPlausible("ghbdsn", "en-US"));
        Assert.Equal(true, Plausibility.IsPlausible("привіт", "uk-UA"));
        Assert.Equal(false, Plausibility.IsPlausible("hello", "uk-UA"));
        Assert.Null(Plausibility.IsPlausible("hallo", "de-DE"));
    }
}
