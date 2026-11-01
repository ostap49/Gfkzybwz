using System.Runtime.InteropServices;

namespace Rozkladka;

/// <summary>Надсилає натискання від імені користувача (позначені, щоб наш хук їх ігнорував).</summary>
internal static class InputSender
{
    private static Native.INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = Native.OwnInputMarker },
        },
    };

    private static void Send(List<Native.INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        Native.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Native.INPUT>());
    }

    public static void Tap(int vk, int times = 1)
    {
        var list = new List<Native.INPUT>();
        for (int i = 0; i < times; i++)
        {
            list.Add(Key((ushort)vk, 0, 0));
            list.Add(Key((ushort)vk, 0, Native.KEYEVENTF_KEYUP));
        }
        Send(list);
    }

    public static void Backspace(int times) => Tap(Native.VK_BACK, times);

    /// <summary>Друкує текст як Unicode, незалежно від поточної розкладки.</summary>
    public static void Text(string text)
    {
        var list = new List<Native.INPUT>();
        foreach (var ch in text)
        {
            if (ch == '\n') { list.Add(Key(Native.VK_RETURN, 0, 0)); list.Add(Key(Native.VK_RETURN, 0, Native.KEYEVENTF_KEYUP)); continue; }
            if (ch == '\r') continue;
            list.Add(Key(0, ch, Native.KEYEVENTF_UNICODE));
            list.Add(Key(0, ch, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP));
        }
        Send(list);
    }

    /// <summary>Ctrl + клавіша (Ctrl+C, Ctrl+V). Спершу відпускає затиснуті користувачем модифікатори.</summary>
    public static void Ctrl(int vk)
    {
        var list = new List<Native.INPUT>();
        foreach (var mod in new[] { Native.VK_LSHIFT, Native.VK_RSHIFT, Native.VK_LMENU, Native.VK_RMENU })
            if (Native.IsDown(mod)) list.Add(Key((ushort)mod, 0, Native.KEYEVENTF_KEYUP));
        list.Add(Key(Native.VK_CONTROL, 0, 0));
        list.Add(Key((ushort)vk, 0, 0));
        list.Add(Key((ushort)vk, 0, Native.KEYEVENTF_KEYUP));
        list.Add(Key(Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP));
        Send(list);
    }
}
