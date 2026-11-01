namespace Rozkladka.Core;

/// <summary>Одне натискання клавіші, незалежне від розкладки: віртуальний код + Shift.</summary>
public readonly record struct KeyStroke(int Vk, bool Shift);

/// <summary>Розкладка клавіатури: що друкує кожна клавіша.</summary>
public interface ILayout
{
    /// <summary>Тег мови для перевірки орфографії, напр. "uk-UA", "en-US".</summary>
    string LanguageTag { get; }

    /// <summary>Символ для натискання або null, якщо клавіша не друкує символ.</summary>
    char? CharFor(KeyStroke key);
}

public static class LayoutExtensions
{
    public static string Render(this ILayout layout, IEnumerable<KeyStroke> keys)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var k in keys)
        {
            var c = layout.CharFor(k);
            if (c is null) return string.Empty;
            sb.Append(c.Value);
        }
        return sb.ToString();
    }
}

/// <summary>Розкладка, задана таблицею (для тестів і як запасний варіант).</summary>
public sealed class TableLayout : ILayout
{
    private readonly Dictionary<KeyStroke, char> _map;

    public TableLayout(string languageTag, Dictionary<KeyStroke, char> map)
    {
        LanguageTag = languageTag;
        _map = map;
    }

    public string LanguageTag { get; }

    public char? CharFor(KeyStroke key) => _map.TryGetValue(key, out var c) ? c : null;

    public IEnumerable<KeyValuePair<KeyStroke, char>> Entries => _map;
}

/// <summary>Віртуальні коди клавіш Windows, що друкують символи.</summary>
public static class Vk
{
    public const int Oem1 = 0xBA;      // ;:
    public const int OemPlus = 0xBB;   // =+
    public const int OemComma = 0xBC;  // ,<
    public const int OemMinus = 0xBD;  // -_
    public const int OemPeriod = 0xBE; // .>
    public const int Oem2 = 0xBF;      // /?
    public const int Oem3 = 0xC0;      // `~
    public const int Oem4 = 0xDB;      // [{
    public const int Oem5 = 0xDC;      // \|
    public const int Oem6 = 0xDD;      // ]}
    public const int Oem7 = 0xDE;      // '"

    /// <summary>Чи може клавіша бути частиною слова (букви, цифри та OEM-клавіші).</summary>
    public static bool IsPrintable(int vk) =>
        vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A
        || vk is Oem1 or OemPlus or OemComma or OemMinus or OemPeriod or Oem2 or Oem3 or Oem4 or Oem5 or Oem6 or Oem7;
}

/// <summary>Вбудовані таблиці: US QWERTY і стандартна українська розкладка Windows.</summary>
public static class KnownLayouts
{
    // Порядок клавіш: цифровий ряд, верхній, середній, нижній.
    private static readonly int[] Keys =
    {
        Vk.Oem3, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, Vk.OemMinus, Vk.OemPlus,
        0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50, Vk.Oem4, Vk.Oem6, Vk.Oem5,
        0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, Vk.Oem1, Vk.Oem7,
        0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D, Vk.OemComma, Vk.OemPeriod, Vk.Oem2,
    };

    private const string UsLower = "`1234567890-=" + "qwertyuiop[]\\" + "asdfghjkl;'" + "zxcvbnm,./";
    private const string UsUpper = "~!@#$%^&*()_+" + "QWERTYUIOP{}|" + "ASDFGHJKL:\"" + "ZXCVBNM<>?";

    private const string UkLower = "'1234567890-=" + "йцукенгшщзхї\\" + "фівапролджє" + "ячсмитьбю.";
    private const string UkUpper = "₴!\"№;%:?*()_+" + "ЙЦУКЕНГШЩЗХЇ/" + "ФІВАПРОЛДЖЄ" + "ЯЧСМИТЬБЮ,";

    public static TableLayout EnglishUs { get; } = Build("en-US", UsLower, UsUpper);
    public static TableLayout Ukrainian { get; } = Build("uk-UA", UkLower, UkUpper);

    private static TableLayout Build(string tag, string lower, string upper)
    {
        if (lower.Length != Keys.Length || upper.Length != Keys.Length)
            throw new InvalidOperationException($"Layout table {tag} has wrong length");
        var map = new Dictionary<KeyStroke, char>();
        for (int i = 0; i < Keys.Length; i++)
        {
            map[new KeyStroke(Keys[i], false)] = lower[i];
            map[new KeyStroke(Keys[i], true)] = upper[i];
        }
        return new TableLayout(tag, map);
    }
}
