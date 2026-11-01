namespace Rozkladka;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Rozkladka.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Контекст синхронізації потрібен рушію ще до Application.Run.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var settings = AppSettings.Load();
        bool firstRun = !File.Exists(AppSettings.FilePath);
        if (firstRun) settings.Save();

        var checker = new WindowsSpellChecker();
        var engine = new SwitchEngine(settings, checker);
        try
        {
            engine.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rozkladka", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (firstRun) WarnAboutMissingDictionaries(engine, checker);
        Application.Run(new TrayApp(settings, engine));
    }

    private static void WarnAboutMissingDictionaries(SwitchEngine engine, WindowsSpellChecker checker)
    {
        var layouts = engine.Layouts.Layouts;
        if (layouts.Count < 2)
        {
            MessageBox.Show("Увімкнено лише одну розкладку клавіатури. Додайте ще одну (наприклад, українську) у Параметрах Windows → Час і мова → Мова.",
                "Rozkladka", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var missing = layouts.Where(l => !checker.IsSupported(l.LanguageTag)).Select(l => l.LanguageTag).Distinct().ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(
                "Немає словника перевірки орфографії Windows для: " + string.Join(", ", missing) + ".\n\n" +
                "Rozkladka працюватиме обережніше (лише явні випадки). Щоб додати словник, встановіть мовний пакет " +
                "(Параметри → Час і мова → Мова → мова → Параметри мови) або покладіть словник Hunspell (.dic + .aff) у\n" +
                "%AppData%\\Microsoft\\Spelling\\<мова>\\",
                "Rozkladka", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
