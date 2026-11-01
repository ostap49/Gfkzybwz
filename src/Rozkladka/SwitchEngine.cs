using System.Diagnostics;
using System.Runtime.InteropServices;
using Rozkladka.Core;

namespace Rozkladka;

/// <summary>
/// Слухає клавіатуру, збирає поточне слово і, якщо воно набране не в тій розкладці,
/// стирає його та друкує правильно, перемикаючи розкладку.
/// </summary>
internal sealed class SwitchEngine : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;

    private readonly AppSettings _settings;
    private readonly LayoutManager _layouts = new();
    private readonly SwitchDecider _decider;
    private readonly SynchronizationContext _ui;
    private readonly Native.LowLevelKeyboardProc _keyboardProc;
    private readonly Native.LowLevelKeyboardProc _mouseProc;
    private IntPtr _keyboardHook, _mouseHook;

    private readonly List<KeyStroke> _word = new();
    private bool _wordEdited;              // у слові був Backspace — користувач пише свідомо
    private List<KeyStroke>? _lastWord;    // попереднє слово (для ⇧⇧ одразу після пробілу)
    private int _lastBoundary;
    private Replacement? _lastReplacement; // остання заміна, яку можна скасувати Backspace
    private IntPtr _hwnd;

    private bool _shiftHeld, _shiftClean;
    private long _lastShiftTap;

    private readonly Dictionary<uint, string> _processNames = new();
    private readonly Dictionary<string, int> _undoCounts = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Replacement(string Original, string Replaced, int BoundaryVk, WinLayout From, WinLayout To, List<KeyStroke> Keys);

    public event Action<string>? ExceptionAdded;
    public event Action? Switched;

    public SwitchEngine(AppSettings settings, IWordChecker checker)
    {
        _settings = settings;
        _decider = new SwitchDecider(checker);
        ReloadExceptions();
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    public LayoutManager Layouts => _layouts;

    public void ReloadExceptions()
    {
        _decider.Exceptions.Clear();
        foreach (var e in _settings.Exceptions) _decider.Exceptions.Add(e);
    }

    public void Start()
    {
        var module = Native.GetModuleHandle(null);
        _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = Native.SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
            throw new InvalidOperationException("Не вдалося встановити перехоплення клавіатури (код " + Marshal.GetLastWin32Error() + ").");
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
    }

    /// <summary>Ім'я процесу активного вікна (без .exe).</summary>
    public string? ForegroundProcess()
    {
        var hwnd = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return ProcessName(pid);
    }

    private string? ProcessName(uint pid)
    {
        if (pid == 0) return null;
        if (_processNames.TryGetValue(pid, out var name)) return name;
        try { name = Process.GetProcessById((int)pid).ProcessName; }
        catch (Exception) { name = ""; }
        if (_processNames.Count > 500) _processNames.Clear();
        _processNames[pid] = name;
        return name;
    }

    private bool IsExcluded(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return true;
        var name = ProcessName(pid);
        return name is not null && _settings.ExcludedApps.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }

    private void ResetWord(bool keepLast = false)
    {
        _word.Clear();
        _wordEdited = false;
        if (!keepLast) _lastWord = null;
        _lastReplacement = null;
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            ResetWord();
        return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Native.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        bool suppress = false;
        try
        {
            if ((info.flags & Native.LLKHF_INJECTED) == 0 && info.dwExtraInfo != Native.OwnInputMarker)
            {
                int msg = (int)wParam;
                bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                suppress = down ? OnKeyDown((int)info.vkCode) : OnKeyUp((int)info.vkCode);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ResetWord();
        }
        return suppress ? (IntPtr)1 : Native.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsShift(int vk) => vk is Native.VK_SHIFT or Native.VK_LSHIFT or Native.VK_RSHIFT;
    private static bool IsModifier(int vk) => IsShift(vk) || vk is Native.VK_CONTROL or Native.VK_LCONTROL or Native.VK_RCONTROL
        or Native.VK_MENU or Native.VK_LMENU or Native.VK_RMENU or Native.VK_LWIN or Native.VK_RWIN or Native.VK_CAPITAL;

    private bool OnKeyUp(int vk)
    {
        if (!IsShift(vk)) return false;
        _shiftHeld = false;
        if (!_shiftClean) return false;
        _shiftClean = false;

        long now = Environment.TickCount64;
        if (_settings.DoubleShiftConvertsWord && now - _lastShiftTap < 400)
        {
            _lastShiftTap = 0;
            _ui.Post(_ => ConvertLastWordManually(), null);
        }
        else
        {
            _lastShiftTap = now;
        }
        return false;
    }

    private bool OnKeyDown(int vk)
    {
        if (IsShift(vk))
        {
            if (!_shiftHeld) { _shiftHeld = true; _shiftClean = true; }
            return false;
        }
        _shiftClean = false;
        _lastShiftTap = 0;
        if (IsModifier(vk)) return false;

        var hwnd = Native.GetForegroundWindow();
        if (hwnd != _hwnd)
        {
            _hwnd = hwnd;
            ResetWord();
        }

        if (vk == Native.VK_PAUSE && _settings.PauseConvertsSelection)
        {
            _ui.Post(async _ => await ConvertSelectionAsync(), null);
            return true;
        }

        bool ctrlOrAlt = Native.IsDown(Native.VK_CONTROL) || Native.IsDown(Native.VK_MENU)
            || Native.IsDown(Native.VK_LWIN) || Native.IsDown(Native.VK_RWIN);
        if (ctrlOrAlt)
        {
            ResetWord();
            return false;
        }

        if (vk == Native.VK_BACK)
        {
            if (_lastReplacement is not null)
            {
                var r = _lastReplacement;
                _lastReplacement = null;
                _ui.Post(_ => Undo(r), null);
                return true;
            }
            if (_word.Count > 0)
            {
                _word.RemoveAt(_word.Count - 1);
                _wordEdited = true;
            }
            else
            {
                ResetWord();
            }
            return false;
        }

        if (Vk.IsPrintable(vk))
        {
            // З Caps Lock регістр залежить від розкладки — не ризикуємо.
            if (Native.IsToggled(Native.VK_CAPITAL)) { ResetWord(); return false; }
            if (_word.Count == 0) _lastWord = null;
            _lastReplacement = null;
            _word.Add(new KeyStroke(vk, Native.IsDown(Native.VK_SHIFT)));
            return false;
        }

        if (vk is Native.VK_SPACE or Native.VK_RETURN or Native.VK_TAB)
            return OnBoundary(vk, hwnd);

        // Стрілки, Home/End, Delete, Esc, F-клавіші тощо: курсор міг переміститися.
        ResetWord();
        return false;
    }

    private bool OnBoundary(int vk, IntPtr hwnd)
    {
        var keys = _word.ToList();
        bool edited = _wordEdited;
        _word.Clear();
        _wordEdited = false;
        _lastReplacement = null;
        _lastWord = keys.Count > 0 ? keys : null;
        _lastBoundary = vk;

        if (keys.Count == 0 || edited || !_settings.AutoSwitch || IsExcluded(hwnd)) return false;
        var current = _layouts.Current(hwnd);
        if (current is null) return false;
        var others = _layouts.Layouts.Where(l => l.Hkl != current.Hkl).Cast<ILayout>().ToList();
        if (others.Count == 0) return false;

        var decision = _decider.Decide(keys, current, others);
        if (decision is null) return false;

        var target = (WinLayout)decision.Target;
        var replacement = new Replacement(decision.OriginalText, decision.ReplacementText, vk, current, target, keys);
        // Скасування через Backspace — лише після пробілу/Tab: Enter у чатах уже міг надіслати повідомлення.
        _lastReplacement = vk == Native.VK_RETURN ? null : replacement;
        _lastWord = null;
        _ui.Post(_ =>
        {
            InputSender.Backspace(replacement.Original.Length);
            LayoutManager.Activate(hwnd, target);
            InputSender.Text(replacement.Replaced);
            InputSender.Tap(vk);
            Feedback();
        }, null);
        return true; // сам роздільник надрукуємо після заміни
    }

    /// <summary>Повертає слово, як його набрав користувач, разом із роздільником.</summary>
    private void Undo(Replacement r)
    {
        var hwnd = Native.GetForegroundWindow();
        InputSender.Backspace(r.Replaced.Length + 1);
        LayoutManager.Activate(hwnd, r.From);
        InputSender.Text(r.Original);
        InputSender.Tap(r.BoundaryVk);
        _lastWord = r.Keys;
        _lastBoundary = r.BoundaryVk;

        var core = SwitchDecider.Core(r.Original);
        int count = _undoCounts.TryGetValue(core, out var c) ? c + 1 : 1;
        _undoCounts[core] = count;
        if (_settings.UndoCountToAddException > 0 && count >= _settings.UndoCountToAddException
            && !_settings.Exceptions.Contains(core, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Exceptions.Add(core);
            _settings.Save();
            ReloadExceptions();
            ExceptionAdded?.Invoke(core);
        }
    }

    /// <summary>⇧⇧: змінити розкладку поточного (або щойно набраного) слова на наступну.</summary>
    private void ConvertLastWordManually()
    {
        var hwnd = Native.GetForegroundWindow();
        if (IsExcluded(hwnd) && _word.Count == 0 && _lastWord is null) return;

        if (_lastReplacement is not null)
        {
            var r = _lastReplacement;
            _lastReplacement = null;
            Undo(r);
            return;
        }

        var current = _layouts.Current(hwnd);
        if (current is null) return;
        var next = _layouts.Next(current);
        if (next is null) return;

        if (_word.Count > 0)
        {
            var from = current.Render(_word);
            var to = next.Render(_word);
            if (from.Length == 0 || to.Length == 0) return;
            InputSender.Backspace(from.Length);
            LayoutManager.Activate(hwnd, next);
            InputSender.Text(to);
        }
        else if (_lastWord is not null)
        {
            var from = current.Render(_lastWord);
            var to = next.Render(_lastWord);
            if (from.Length == 0 || to.Length == 0) return;
            InputSender.Backspace(from.Length + 1);
            LayoutManager.Activate(hwnd, next);
            InputSender.Text(to);
            InputSender.Tap(_lastBoundary);
        }
        else
        {
            LayoutManager.Activate(hwnd, next);
        }
        Feedback();
    }

    /// <summary>Pause: перетворити виділений текст (через буфер обміну) і перемкнути розкладку.</summary>
    private async Task ConvertSelectionAsync()
    {
        var hwnd = Native.GetForegroundWindow();
        string? backup = null;
        try { if (Clipboard.ContainsText()) backup = Clipboard.GetText(); } catch (ExternalException) { }

        try { Clipboard.Clear(); } catch (ExternalException) { }
        InputSender.Ctrl('C');

        string? selected = null;
        for (int i = 0; i < 15 && selected is null; i++)
        {
            await Task.Delay(30);
            try { if (Clipboard.ContainsText()) selected = Clipboard.GetText(); } catch (ExternalException) { }
        }

        if (string.IsNullOrEmpty(selected))
        {
            Restore(backup);
            ConvertLastWordManually();
            return;
        }

        var layouts = _layouts.Layouts.Cast<ILayout>().ToList();
        var source = TextConverter.GuessSource(selected, layouts) as WinLayout ?? _layouts.Current(hwnd);
        var target = source is null ? null : _layouts.Next(source);
        if (source is null || target is null) { Restore(backup); return; }

        var converted = TextConverter.Convert(selected, source, target);
        try { Clipboard.SetText(converted); } catch (ExternalException) { Restore(backup); return; }
        InputSender.Ctrl('V');
        LayoutManager.Activate(hwnd, target);
        ResetWord();
        Feedback();

        await Task.Delay(400);
        Restore(backup);
    }

    private static void Restore(string? backup)
    {
        try
        {
            if (backup is null) Clipboard.Clear();
            else Clipboard.SetText(backup);
        }
        catch (ExternalException) { }
    }

    private void Feedback()
    {
        if (_settings.Sound) System.Media.SystemSounds.Asterisk.Play();
        Switched?.Invoke();
    }
}
