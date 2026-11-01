namespace Rozkladka.Core;

/// <summary>Перетворює вже набраний текст з однієї розкладки в іншу (для виділеного тексту).</summary>
public static class TextConverter
{
    public static IEnumerable<KeyStroke> AllKeys()
    {
        for (int vk = 0; vk < 256; vk++)
        {
            if (!Vk.IsPrintable(vk)) continue;
            yield return new KeyStroke(vk, false);
            yield return new KeyStroke(vk, true);
        }
    }

    public static Dictionary<char, KeyStroke> ReverseMap(ILayout layout)
    {
        var map = new Dictionary<char, KeyStroke>();
        foreach (var k in AllKeys())
        {
            var c = layout.CharFor(k);
            if (c is not null && !map.ContainsKey(c.Value)) map[c.Value] = k;
        }
        return map;
    }

    /// <summary>Переписує text так, ніби ті самі клавіші натиснули в розкладці to.</summary>
    public static string Convert(string text, ILayout from, ILayout to)
    {
        var reverse = ReverseMap(from);
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (reverse.TryGetValue(ch, out var key) && to.CharFor(key) is char converted)
                sb.Append(converted);
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Визначає, в якій із розкладок, найімовірніше, набрано текст: та, де знайдеться більше його букв.
    /// </summary>
    public static ILayout? GuessSource(string text, IReadOnlyList<ILayout> layouts)
    {
        ILayout? best = null;
        int bestScore = 0;
        foreach (var layout in layouts)
        {
            var reverse = ReverseMap(layout);
            int score = text.Count(c => char.IsLetter(c) && reverse.ContainsKey(c));
            if (score > bestScore)
            {
                best = layout;
                bestScore = score;
            }
        }
        return best;
    }
}
