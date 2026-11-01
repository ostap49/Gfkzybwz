namespace Rozkladka.Core;

/// <summary>Перевірка, чи є слово справжнім словом мови.</summary>
public interface IWordChecker
{
    /// <summary>true/false — відповідь словника; null — словника для мови немає.</summary>
    bool? IsWord(string word, string languageTag);
}

/// <summary>Короткі часті слова: словники часто вважають «помилками» або, навпаки, приймають сміття з 1–3 літер.</summary>
public static class ShortWords
{
    private static readonly HashSet<string> Uk = new(StringComparer.OrdinalIgnoreCase)
    {
        "а", "в", "з", "і", "й", "о", "у", "я",
        "ах", "ви", "де", "до", "же", "за", "із", "їй", "їх", "ну", "ок", "ой", "по", "та", "те", "ти", "то", "ту", "це", "ця", "ці", "чи", "що", "як", "ми", "на", "не", "ні", "он", "от", "ще", "бо", "би", "б", "ж", "аж", "ім", "їм", "її", 
        "але", "або", "він", "вже", "все", "від", "для", "дуже", "так", "там", "тут", "щоб", "вона", "воно", "вони", "мені", "тебе", "мене", "собі", "хто", "чим", "чому", "коли", "який", "яка", "яке", "які", "ось", "ага", "ура", "без", "при", "про", "під", "над", "між", "нам", "вам", "них", "нею", "нас", "вас", "мій", "моя", "моє", "мої", "твій", "наш", "ваш", "раз", "два", "три", "сто", "рік", "день", "дім",
    };

    private static readonly HashSet<string> En = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "i",
        "am", "an", "as", "at", "be", "by", "do", "go", "he", "hi", "if", "in", "is", "it", "me", "my", "no", "of", "oh", "ok", "on", "or", "so", "to", "up", "us", "we",
        "and", "are", "but", "can", "did", "for", "get", "got", "had", "has", "her", "him", "his", "how", "its", "let", "may", "new", "not", "now", "old", "one", "our", "out", "see", "she", "the", "too", "two", "use", "was", "way", "who", "why", "yes", "yet", "you", "all", "any", "day", "few", "man", "set", "top", "try", "big", "put", "run", "off", "own", "say", "ask", "end", "far",
    };

    public static bool? IsCommon(string word, string languageTag)
    {
        var lang = languageTag.Split('-')[0].ToLowerInvariant();
        return lang switch
        {
            "uk" => Uk.Contains(word),
            "en" => En.Contains(word),
            _ => null,
        };
    }
}

/// <summary>
/// Запасна евристика без словника: чи схоже слово на слово мови з таким алфавітом.
/// Свідомо обережна — лише відкидає явне сміття.
/// </summary>
public static class Plausibility
{
    private const string UkLetters = "абвгґдеєжзиіїйклмнопрстуфхцчшщьюя'’";
    private const string UkVowels = "аеєиіїоуюя";
    private const string EnLetters = "abcdefghijklmnopqrstuvwxyz'";
    private const string EnVowels = "aeiouy";

    /// <summary>null — мова невідома.</summary>
    public static bool? IsPlausible(string word, string languageTag)
    {
        var lang = languageTag.Split('-')[0].ToLowerInvariant();
        var (letters, vowels) = lang switch
        {
            "uk" => (UkLetters, UkVowels),
            "en" => (EnLetters, EnVowels),
            _ => (null, null),
        };
        if (letters is null || vowels is null) return null;

        var lower = word.ToLowerInvariant();
        if (lower.Length == 0) return false;
        if (lower.Any(c => letters.IndexOf(c) < 0)) return false;
        if (lower[0] is '\'' or '’' || lower[^1] is '\'' or '’') return false;

        int run = 0, maxRun = 0, vowelCount = 0;
        foreach (var c in lower)
        {
            if (vowels.IndexOf(c) >= 0) { vowelCount++; run = 0; }
            else if (char.IsLetter(c)) { run++; maxRun = Math.Max(maxRun, run); }
        }
        if (lower.Length >= 3 && vowelCount == 0) return false;
        if (maxRun >= 5) return false;
        // Слово з самих голосних довше трьох — майже завжди сміття (напр. "ueue").
        if (lower.Length >= 4 && vowelCount == lower.Count(char.IsLetter)) return false;
        return true;
    }
}
