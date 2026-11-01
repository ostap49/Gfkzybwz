using System.Text.Json;
using Microsoft.Win32;

namespace Rozkladka;

internal sealed class AppSettings
{
    public bool AutoSwitch { get; set; } = true;
    public bool DoubleShiftConvertsWord { get; set; } = true;
    public bool PauseConvertsSelection { get; set; } = true;
    public bool Sound { get; set; }

    /// <summary>Скільки разів скасувати заміну того самого слова, щоб воно потрапило у винятки.</summary>
    public int UndoCountToAddException { get; set; } = 2;

    public List<string> Exceptions { get; set; } = new();

    /// <summary>Імена процесів без .exe, у яких автоперемикання вимкнене.</summary>
    public List<string> ExcludedApps { get; set; } = new()
    {
        "WindowsTerminal", "cmd", "powershell", "pwsh", "conhost", "mintty", "putty",
        "Code", "devenv", "rider64", "idea64", "pycharm64", "webstorm64", "sublime_text", "notepad++",
        "KeePass", "KeePassXC", "1Password", "Bitwarden",
    };

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rozkladka");
    public static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Пошкоджений файл — стартуємо з налаштувань за замовчуванням, файл не перезаписуємо одразу.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Rozkladka") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Rozkladka", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("Rozkladka", false);
        }
    }
}
