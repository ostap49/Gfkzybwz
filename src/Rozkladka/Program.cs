namespace Rozkladka;

internal static class Program
{
    /// <summary>Повідомлення, яким друга копія просить першу показати себе.</summary>
    public static readonly int ShowMeMessage = NativeShow.RegisterWindowMessage("Rozkladka.ShowMe");

    [STAThread]
    private static void Main(string[] args)
    {
        bool silent = Packaging.LaunchedAtStartup(args);

        using var mutex = new Mutex(true, @"Local\Rozkladka.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Уже працює — просимо першу копію показати сповіщення, де її шукати.
            NativeShow.PostMessage((IntPtr)0xFFFF, ShowMeMessage, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Fatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);
        // Контекст синхронізації потрібен рушію ще до Application.Run.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        try
        {
            Log.Info($"Старт {Application.ProductVersion}, {Environment.OSVersion}, шлях {Environment.ProcessPath}, MSIX: {Packaging.IsPackaged}");

            var settings = AppSettings.Load();
            bool firstRun = !File.Exists(AppSettings.FilePath);
            if (firstRun)
            {
                settings.Save();
                // Із Microsoft Store інсталятора немає — вмикаємо автозапуск при першому запуску.
                if (Packaging.IsPackaged) Packaging.SetStartWithWindows(true);
            }

            var checker = new WindowsSpellChecker();
            var engine = new SwitchEngine(settings, checker);
            engine.Start();

            var layouts = engine.Layouts.Layouts;
            Log.Info("Розкладки: " + string.Join(", ", layouts.Select(l => l.LanguageTag)));
            var missing = layouts.Where(l => !checker.IsSupported(l.LanguageTag)).Select(l => l.LanguageTag).Distinct().ToList();
            if (missing.Count > 0) Log.Info("Немає словника для: " + string.Join(", ", missing));

            var tray = new TrayApp(settings, engine);
            if (!silent) tray.ShowWelcome(firstRun, layouts.Count, missing);
            Application.Run(tray);
            Log.Info("Вихід");
        }
        catch (Exception ex)
        {
            Fatal(ex);
        }
    }

    private static void Fatal(Exception? ex)
    {
        Log.Error("Збій", ex);
        MessageBox.Show(
            "Rozkladka зупинилася через помилку:\n\n" + ex?.Message + "\n\nПодробиці в журналі:\n" + Log.FilePath,
            "Rozkladka", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Environment.Exit(1);
    }
}

internal static class NativeShow
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int RegisterWindowMessage(string message);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
