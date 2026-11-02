using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace Rozkladka;

/// <summary>
/// Різниця між звичайним встановленням (Inno Setup / portable) і пакетом MSIX з Microsoft Store:
/// у пакеті реєстр Run не працює (віртуалізується) — автозапуск іде через StartupTask.
/// </summary>
internal static class Packaging
{
    /// <summary>Id задачі автозапуску в Package.appxmanifest.</summary>
    public const string StartupTaskId = "RozkladkaStartup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    private static readonly Lazy<bool> _isPackaged = new(() =>
    {
        try
        {
            int length = 0;
            // 15700 = APPMODEL_ERROR_NO_PACKAGE
            return GetCurrentPackageFullName(ref length, null) != 15700;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public static bool IsPackaged => _isPackaged.Value;

    /// <summary>Тека даних: у пакеті — LocalState (звичайна тека, її бачить і Блокнот), інакше %AppData%\Rozkladka.</summary>
    public static string DataFolder
    {
        get
        {
            if (IsPackaged)
            {
                try { return Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
                catch (Exception) { /* запасний варіант нижче */ }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rozkladka");
        }
    }

    /// <summary>Чи запущено автоматично під час входу в Windows (тоді без привітань).</summary>
    public static bool LaunchedAtStartup(string[] args)
    {
        if (args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase))) return true;
        if (!IsPackaged) return false;
        try
        {
            return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool StartWithWindows
    {
        get
        {
            if (IsPackaged)
            {
                try
                {
                    var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                    return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                }
                catch (Exception ex)
                {
                    Log.Error("StartupTask", ex);
                    return false;
                }
            }
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Rozkladka") is string;
        }
    }

    /// <summary>Вмикає/вимикає автозапуск. Повертає текст для користувача, якщо Windows не дозволила.</summary>
    public static string? SetStartWithWindows(bool enable)
    {
        if (IsPackaged)
        {
            try
            {
                var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                if (!enable)
                {
                    task.Disable();
                    return null;
                }
                var state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
                return state switch
                {
                    StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => null,
                    StartupTaskState.DisabledByUser =>
                        "Автозапуск вимкнено в Параметрах Windows. Увімкніть Rozkladka в розділі Параметри → Програми → Автозавантаження.",
                    StartupTaskState.DisabledByPolicy => "Автозапуск заборонено політикою організації.",
                    _ => "Windows не дозволила автозапуск.",
                };
            }
            catch (Exception ex)
            {
                Log.Error("StartupTask", ex);
                return "Не вдалося змінити автозапуск: " + ex.Message;
            }
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) key.SetValue("Rozkladka", $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue("Rozkladka", false);
        return null;
    }
}
