using System.Windows.Input;

namespace Notaker;

public sealed record DictationShortcut(uint Modifiers, uint VirtualKey)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => (Modifiers & ~15u) == 0 && VirtualKey is >= 0x08 and <= 0xFE
        && VirtualKey is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x7B)
        && (Modifiers != 0 || VirtualKey is >= 0x70 and <= 0x87)
        && !(VirtualKey == 0x2E && (Modifiers & 3) == 3);

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & 2) != 0) parts.Add("Ctrl");
            if ((Modifiers & 1) != 0) parts.Add("Alt");
            if ((Modifiers & 4) != 0) parts.Add("Shift");
            if ((Modifiers & 8) != 0) parts.Add("Win");
            var key = KeyInterop.KeyFromVirtualKey((int)VirtualKey);
            parts.Add(key switch
            {
                Key.Space => "Espacio", Key.Return => "Enter", Key.Escape => "Esc",
                Key.Back => "Retroceso", Key.Delete => "Supr", Key.Insert => "Insert",
                Key.PageUp => "RePág", Key.PageDown => "AvPág",
                Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓",
                >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
                _ => new KeyConverter().ConvertToString(key) ?? key.ToString()
            });
            return string.Join(" + ", parts);
        }
    }

    public static DictationShortcut FromLegacy(int index) => index switch
    {
        1 => new(6, 0x20), 2 => new(9, 0x44), _ => new(3, 0x20)
    };
    public static DictationShortcut? Capture(Key key, ModifierKeys modifiers)
    {
        if (key is Key.None or Key.System or Key.DeadCharProcessed or Key.ImeProcessed) return null;
        var candidate = new DictationShortcut((uint)modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        return candidate.IsValid ? candidate : null;
    }
}
