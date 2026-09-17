using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Notaker;

internal static class Native
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    internal static void DarkCaption(Window window)
    {
        var dark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public UNION Data; }
    [StructLayout(LayoutKind.Explicit)] private struct UNION
    {
        [FieldOffset(0)] public KEYBOARD Keyboard;
        [FieldOffset(0)] public MOUSE Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBOARD { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSE { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    private static INPUT Key(ushort key, bool up = false) => new() { Type = 1, Data = new UNION { Keyboard = new KEYBOARD { Key = key, Flags = up ? 2u : 0u } } };

    internal static bool Register(IntPtr hwnd, int option, int id = 1)
        => Register(hwnd, DictationShortcut.FromLegacy(option), id);
    internal static bool Register(IntPtr hwnd, DictationShortcut combination, int id = 1)
    {
        return combination.IsValid && RegisterHotKey(hwnd, id, combination.Modifiers | 0x4000, combination.VirtualKey);
    }
    internal static void Unregister(IntPtr hwnd, int id = 1) => UnregisterHotKey(hwnd, id);
    internal static void NoActivate(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000L | 0x00000080L));
    }
    internal static async Task<bool> CopyAsync(string text)
    {
        for (var i = 0; i < 8; i++)
        {
            try { System.Windows.Clipboard.SetText(text); return true; }
            catch (ExternalException) { await Task.Delay(75); }
        }
        return false;
    }
    internal static async Task<bool> PasteAsync(IntPtr target, string text)
    {
        // Do not send Ctrl+V while a modifier from the trigger is still held.
        for (var i = 0; i < 40; i++)
        {
            if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.All(k => (GetAsyncKeyState(k) & 0x8000) == 0)) break;
            await Task.Delay(50);
        }
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0)) return false;
        if (!IsWindow(target)) return false;
        if (GetForegroundWindow() != target)
        {
            if (!SetForegroundWindow(target)) return false;
            await Task.Delay(150);
        }
        if (GetForegroundWindow() != target) return false;
        // Another app may have changed the clipboard during transcription.
        if (!await CopyAsync(text) || GetForegroundWindow() != target) return false;
        var inputs = new[] { Key(0x11), Key(0x56), Key(0x56, true), Key(0x11, true) };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }
}
