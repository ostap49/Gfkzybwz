namespace Rozkladka.Core;

public sealed record SwitchDecision(ILayout Target, string OriginalText, string ReplacementText);

/// <summary>Вирішує, чи слово набрано не в тій розкладці і на яку його змінити.</summary>
public sealed class SwitchDecider
{
    private readonly IWordChecker _checker;

    public SwitchDecider(IWordChecker checker) => _checker = checker;

    /// <summary>Слова (у будь-якій розкладці), які ніколи не змінюються.</summary>
    public ISet<string> Exceptions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private enum Score { Invalid = 0, Unknown = 1, Valid = 2 }

    public SwitchDecision? Decide(IReadOnlyList<KeyStroke> keys, ILayout current, IReadOnlyList<ILayout> others)
    {
        if (keys.Count == 0) return null;
        // Слова з цифрами (2nd, A4, covid19) не чіпаємо.
        if (keys.Any(k => k.Vk is >= 0x30 and <= 0x39 && !k.Shift)) return null;

        var curText = current.Render(keys);
        if (curText.Length == 0) return null;
        var curCore = Core(curText);
        if (curCore.Length == 0 || Exceptions.Contains(curCore)) return null;

        var curScore = Evaluate(curCore, current.LanguageTag);
        if (curScore == Score.Valid) return null;
        bool curImplausible = Plausibility.IsPlausible(curCore, current.LanguageTag) == false;

        SwitchDecision? best = null;
        var bestScore = Score.Invalid;
        foreach (var layout in others)
        {
            if (ReferenceEquals(layout, current)) continue;
            var text = layout.Render(keys);
            var core = Core(text);
            if (core.Length == 0 || Exceptions.Contains(core)) continue;
            if (core.Length != curCore.Length && core.Length < 2) continue;

            var score = Evaluate(core, layout.LanguageTag);
            bool accept = score == Score.Valid
                || (score == Score.Unknown && curScore == Score.Invalid && curImplausible && core.Length >= 3);
            if (accept && score > bestScore)
            {
                best = new SwitchDecision(layout, curText, text);
                bestScore = score;
            }
        }

        if (best is null) return null;
        // Поточне слово «невідоме» (напр. словника немає) — міняємо лише на точно відоме слово.
        if (curScore == Score.Unknown && bestScore != Score.Valid) return null;
        return best;
    }

    private Score Evaluate(string word, string tag)
    {
        if (ShortWords.IsCommon(word, tag) == true) return Score.Valid;

        // Короткі слова: словники приймають купу абревіатур, тож тут віримо лише списку частих слів.
        if (word.Length <= 2)
            return Plausibility.IsPlausible(word, tag) == false ? Score.Invalid : Score.Unknown;

        var dict = _checker.IsWord(word, tag);
        if (dict == true) return Score.Valid;
        if (dict == false) return Score.Invalid;
        return Plausibility.IsPlausible(word, tag) == false ? Score.Invalid : Score.Unknown;
    }

    /// <summary>Слово без розділових знаків на краях: "привіт," → "привіт".</summary>
    public static string Core(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && !char.IsLetter(text[start])) start++;
        while (end > start && !char.IsLetter(text[end - 1])) end--;
        return text[start..end];
    }
}
