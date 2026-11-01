using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;

namespace Rozkladka;

/// <summary>Іконка в області сповіщень: показує поточну розкладку і меню керування.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly SwitchEngine _engine;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _auto, _doubleShift, _sound, _startup, _exclude;
    private string _shownLayout = "";
    private IntPtr _iconHandle;
    private string? _lastExternalApp;

    public TrayApp(AppSettings settings, SwitchEngine engine)
    {
        _settings = settings;
        _engine = engine;

        _auto = new ToolStripMenuItem("Автоматично перемикати розкладку", null, (_, _) => Toggle(() => _settings.AutoSwitch = !_settings.AutoSwitch));
        _doubleShift = new ToolStripMenuItem("⇧⇧ — змінити розкладку слова", null, (_, _) => Toggle(() => _settings.DoubleShiftConvertsWord = !_settings.DoubleShiftConvertsWord));
        _sound = new ToolStripMenuItem("Звук при перемиканні", null, (_, _) => Toggle(() => _settings.Sound = !_settings.Sound));
        _exclude = new ToolStripMenuItem("Вимкнути в цій програмі", null, (_, _) => ToggleExclude());
        _startup = new ToolStripMenuItem("Запускати разом з Windows", null, (_, _) => { AppSettings.StartWithWindows = !AppSettings.StartWithWindows; UpdateChecks(); });

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _auto, _doubleShift, _sound, new ToolStripSeparator(),
            _exclude,
            new ToolStripMenuItem("Винятки й налаштування (файл)…", null, (_, _) => OpenSettingsFile()),
            new ToolStripMenuItem("Перечитати налаштування", null, (_, _) => ReloadSettings()),
            new ToolStripSeparator(),
            _startup,
            new ToolStripMenuItem("Про програму", null, (_, _) => ShowAbout()),
            new ToolStripMenuItem("Вийти", null, (_, _) => ExitThread()),
        });
        menu.Opening += (_, _) => UpdateChecks();

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Rozkladka" };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Toggle(() => _settings.AutoSwitch = !_settings.AutoSwitch, notify: true);
        };

        _engine.ExceptionAdded += word => _tray.ShowBalloonTip(3000, "Rozkladka",
            $"«{word}» додано у винятки — більше не змінюватиму це слово.", ToolTipIcon.Info);
        _engine.Switched += RefreshIcon;

        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) => RefreshIcon();
        _timer.Start();
        RefreshIcon();
    }

    private void Toggle(Action change, bool notify = false)
    {
        change();
        _settings.Save();
        UpdateChecks();
        _shownLayout = "";
        RefreshIcon();
        if (notify)
            _tray.ShowBalloonTip(1500, "Rozkladka", _settings.AutoSwitch ? "Автоперемикання увімкнено" : "Автоперемикання вимкнено", ToolTipIcon.None);
    }

    private void UpdateChecks()
    {
        _auto.Checked = _settings.AutoSwitch;
        _doubleShift.Checked = _settings.DoubleShiftConvertsWord;
        _sound.Checked = _settings.Sound;
        _startup.Checked = AppSettings.StartWithWindows;
        if (_lastExternalApp is { Length: > 0 } app)
        {
            bool excluded = _settings.ExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase);
            _exclude.Text = $"Вимкнути в «{app}»";
            _exclude.Checked = excluded;
            _exclude.Enabled = true;
        }
        else
        {
            _exclude.Text = "Вимкнути в цій програмі";
            _exclude.Enabled = false;
        }
    }

    private void ToggleExclude()
    {
        if (_lastExternalApp is not { Length: > 0 } app) return;
        var existing = _settings.ExcludedApps.FirstOrDefault(a => string.Equals(a, app, StringComparison.OrdinalIgnoreCase));
        if (existing is null) _settings.ExcludedApps.Add(app);
        else _settings.ExcludedApps.Remove(existing);
        _settings.Save();
    }

    private void OpenSettingsFile()
    {
        _settings.Save();
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppSettings.FilePath}\"") { UseShellExecute = true });
        _tray.ShowBalloonTip(3000, "Rozkladka", "Після змін збережіть файл і виберіть «Перечитати налаштування».", ToolTipIcon.Info);
    }

    private void ReloadSettings()
    {
        var fresh = AppSettings.Load();
        _settings.AutoSwitch = fresh.AutoSwitch;
        _settings.DoubleShiftConvertsWord = fresh.DoubleShiftConvertsWord;
        _settings.PauseConvertsSelection = fresh.PauseConvertsSelection;
        _settings.Sound = fresh.Sound;
        _settings.UndoCountToAddException = fresh.UndoCountToAddException;
        _settings.Exceptions = fresh.Exceptions;
        _settings.ExcludedApps = fresh.ExcludedApps;
        _engine.ReloadExceptions();
        _engine.Layouts.Refresh();
        UpdateChecks();
    }

    private static void ShowAbout() => MessageBox.Show(
        "Rozkladka " + Application.ProductVersion + "\n\n" +
        "Автоматично виправляє слова, набрані не в тій розкладці (ghbdsn → привіт).\n\n" +
        "⇧⇧ (двічі Shift) — змінити розкладку поточного або щойно набраного слова.\n" +
        "Pause — змінити розкладку виділеного тексту.\n" +
        "Backspace одразу після заміни — скасувати її.\n" +
        "Лівий клік по іконці — увімкнути/вимкнути автоперемикання.\n\n" +
        "Набраний текст нікуди не надсилається.",
        "Про Rozkladka", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void RefreshIcon()
    {
        var app = _engine.ForegroundProcess();
        if (!string.IsNullOrEmpty(app) && !string.Equals(app, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(app, "explorer", StringComparison.OrdinalIgnoreCase))
            _lastExternalApp = app;

        var layout = _engine.Layouts.Current(Native.GetForegroundWindow());
        var label = layout?.ShortName ?? "··";
        var key = label + (_settings.AutoSwitch ? "+" : "-");
        if (key == _shownLayout) return;
        _shownLayout = key;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var back = !_settings.AutoSwitch ? Color.FromArgb(120, 120, 120)
                : label == "UA" ? Color.FromArgb(0, 87, 183) : Color.FromArgb(40, 40, 40);
            using var brush = new SolidBrush(back);
            g.FillRectangle(brush, 0, 0, 32, 32);
            if (label == "UA" && _settings.AutoSwitch)
            {
                using var yellow = new SolidBrush(Color.FromArgb(255, 215, 0));
                g.FillRectangle(yellow, 0, 26, 32, 6);
            }
            using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString(label, font);
            g.DrawString(label, font, Brushes.White, (32 - size.Width) / 2, (28 - size.Height) / 2);
        }

        var handle = bmp.GetHicon();
        _tray.Icon = Icon.FromHandle(handle);
        if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
        _iconHandle = handle;
        _tray.Text = $"Rozkladka — {layout?.LanguageTag ?? "?"}" + (_settings.AutoSwitch ? "" : " (автоперемикання вимкнено)");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _engine.Dispose();
            if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
        }
        base.Dispose(disposing);
    }
}
