namespace Rozkladka;

/// <summary>Простий журнал у %AppData%\Rozkladka\rozkladka.log (без набраного тексту).</summary>
internal static class Log
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(AppSettings.Folder, "rozkladka.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 512 * 1024) info.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Журнал не повинен ламати програму.
        }
    }
}
