using System.Runtime.InteropServices;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace Notaker;

// Own the shell registration so notifications can supply hBalloonIcon (NIIF_USER).
// WinForms.NotifyIcon.ShowBalloonTip only exposes the standard system symbols.
public sealed class TrayIcon : Forms.NativeWindow, IDisposable
{
    private const int Callback = 0x8001;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Guid id;
    private readonly Icon icon;
    private bool disposed;
    public Forms.ContextMenuStrip? ContextMenuStrip { get; set; }
    public event EventHandler? DoubleClick;

    public TrayIcon(Icon icon, Guid? identity = null)
    {
        this.icon = icon;
        id = identity ?? new Guid("5a907cc7-b705-4b51-b828-a6b79df76cf1");
        CreateHandle(new Forms.CreateParams { Caption = "Notaker notifications" });
        Register();
    }

    private Data CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<Data>(), Window = Handle, Id = 1, Guid = id,
        Flags = 0x20, Icon = icon.Handle, Tip = "Notaker · Dictado", Info = "", Title = ""
    };

    private void Register()
    {
        var data = CreateData();
        data.Flags |= 1 | 2 | 4 | 0x80; // message, icon, tooltip, show standard tooltip
        data.Callback = Callback;
        if (Shell_NotifyIcon(0, ref data))
        {
            data.Flags = 0x20; data.TimeoutOrVersion = 4;
            Shell_NotifyIcon(4, ref data);
        }
    }

    public bool ShowBalloonTip(int timeout, string title, string text, Forms.ToolTipIcon kind)
    {
        if (disposed) return false;
        var data = CreateData();
        data.Flags |= 0x10; // NIF_INFO
        data.Title = title.Length > 63 ? title[..63] : title;
        data.Info = text.Length > 255 ? text[..255] : text;
        data.TimeoutOrVersion = (uint)Math.Max(0, timeout);
        // Retain semantic warning/error icons. Normal background notice uses our N.
        data.InfoFlags = kind switch { Forms.ToolTipIcon.Warning => 2u, Forms.ToolTipIcon.Error => 3u, _ => 4u | 0x20u };
        data.InfoFlags |= 0x80; // respect Windows quiet time
        data.BalloonIcon = icon.Handle;
        return Shell_NotifyIcon(1, ref data);
    }

    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == taskbarCreated && !disposed) Register();
        if (message.Msg == Callback && !disposed)
        {
            var action = (int)(message.LParam.ToInt64() & 0xffff);
            if (action is 0x203 or 0x401 or 0x405) DoubleClick?.Invoke(this, EventArgs.Empty);
            else if (action == 0x7b && ContextMenuStrip != null)
            {
                // Version 4 packs the anchor point into wParam (also for keyboard activation).
                var point = message.WParam.ToInt64();
                var x = (short)(point & 0xffff); var y = (short)((point >> 16) & 0xffff);
                SetForegroundWindow(Handle);
                ContextMenuStrip.Show(new Point(x, y));
                PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (disposed) return;
        var data = CreateData();
        Shell_NotifyIcon(2, ref data);
        disposed = true;
        ContextMenuStrip?.Dispose();
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Data
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref Data data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
