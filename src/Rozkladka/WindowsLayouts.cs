using System.Globalization;
using System.Text;
using Rozkladka.Core;

namespace Rozkladka;

/// <summary>Розкладка Windows (HKL), таблиця символів знята через ToUnicodeEx.</summary>
internal sealed class WinLayout : ILayout
{
    private readonly TableLayout _table;

    public WinLayout(IntPtr hkl)
    {
        Hkl = hkl;
        int langId = (int)((long)hkl & 0xFFFF);
        try
        {
            var culture = CultureInfo.GetCultureInfo(langId);
            LanguageTag = culture.Name;
            ShortName = culture.TwoLetterISOLanguageName.ToUpperInvariant();
        }
        catch (CultureNotFoundException)
        {
            LanguageTag = "und";
            ShortName = "??";
        }
        if (ShortName == "UK") ShortName = "UA";
        _table = new TableLayout(LanguageTag, BuildMap(hkl));
    }

    public IntPtr Hkl { get; }
    public string LanguageTag { get; }
    public string ShortName { get; }

    public char? CharFor(KeyStroke key) => _table.CharFor(key);

    private static Dictionary<KeyStroke, char> BuildMap(IntPtr hkl)
    {
        var map = new Dictionary<KeyStroke, char>();
        var state = new byte[256];
        var buf = new StringBuilder(8);
        foreach (var key in TextConverter.AllKeys())
        {
            Array.Clear(state);
            if (key.Shift) state[Native.VK_SHIFT] = 0x80;
            buf.Clear();
            uint scan = Native.MapVirtualKeyEx((uint)key.Vk, 0, hkl);
            // Прапорець 0x4: не змінювати стан клавіатури ядра (не ламає «мертві» клавіші).
            int n = Native.ToUnicodeEx((uint)key.Vk, scan, state, buf, buf.Capacity, 0x4, hkl);
            if (n == 1 && !char.IsControl(buf[0])) map[key] = buf[0];
        }
        return map;
    }
}

/// <summary>Встановлені розкладки та перемикання між ними.</summary>
internal sealed class LayoutManager
{
    private readonly Dictionary<IntPtr, WinLayout> _cache = new();
    private List<WinLayout> _layouts = new();
    private DateTime _refreshedAt = DateTime.MinValue;

    public IReadOnlyList<WinLayout> Layouts
    {
        get
        {
            if (DateTime.UtcNow - _refreshedAt > TimeSpan.FromSeconds(5)) Refresh();
            return _layouts;
        }
    }

    public void Refresh()
    {
        int count = Native.GetKeyboardLayoutList(0, null);
        var list = new IntPtr[Math.Max(count, 0)];
        if (count > 0) Native.GetKeyboardLayoutList(count, list);
        _layouts = list.Select(h => _cache.TryGetValue(h, out var l) ? l : _cache[h] = new WinLayout(h)).ToList();
        _refreshedAt = DateTime.UtcNow;
    }

    public WinLayout? Current(IntPtr hwnd)
    {
        uint thread = Native.GetWindowThreadProcessId(hwnd, out _);
        var hkl = Native.GetKeyboardLayout(thread);
        var layouts = Layouts;
        var found = layouts.FirstOrDefault(l => l.Hkl == hkl);
        if (found is null && hkl != IntPtr.Zero)
        {
            Refresh();
            found = _layouts.FirstOrDefault(l => l.Hkl == hkl);
        }
        return found;
    }

    /// <summary>Наступна розкладка після поточної (по колу).</summary>
    public WinLayout? Next(WinLayout current)
    {
        var layouts = Layouts;
        if (layouts.Count < 2) return null;
        int i = layouts.ToList().FindIndex(l => l.Hkl == current.Hkl);
        return layouts[(i + 1) % layouts.Count];
    }

    public static void Activate(IntPtr hwnd, WinLayout layout) =>
        Native.PostMessage(hwnd, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout.Hkl);
}
