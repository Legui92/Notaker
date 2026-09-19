using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Notaker;
using Forms = System.Windows.Forms;

static class BrandingChecks
{
    public static void Run(bool showNotification)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window { Width = 200, Height = 100, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Icon = BrandAssets.WindowIcon();
                    window.Show();
                    var handle = new WindowInteropHelper(window).Handle;
                    var nativeIcon = SendMessage(handle, 0x7f, (IntPtr)1, IntPtr.Zero);
                    using var bitmap = Icon.FromHandle(nativeIcon).ToBitmap();
                    int left = bitmap.Width, right = -1;
                    for (int y = 0; y < bitmap.Height; y++)
                        for (int x = 0; x < bitmap.Width; x++)
                            if (bitmap.GetPixel(x, y).A > 128) { left = Math.Min(left, x); right = Math.Max(right, x); }
                    if (right - left + 1 < bitmap.Width * 0.7) throw new Exception("Taskbar logo occupies less than 70% of native large icon");
                    Console.WriteLine($"PASS: Native taskbar glyph is {right-left+1}px inside {bitmap.Width}px icon (not centered 16px frame)");
                    using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Notaker;component/Assets/notaker.ico")).Stream;
                    using var icon = new Icon(stream);
                    using var tray = new TrayIcon(icon, Guid.NewGuid());
                    bool clicked = false;
                    tray.DoubleClick += (_, _) => clicked = true;
                    SendMessage(tray.Handle, 0x8001, IntPtr.Zero, (IntPtr)0x203);
                    if (!clicked) throw new Exception("Tray double click callback missing");
                    clicked = false;
                    SendMessage(tray.Handle, 0x8001, IntPtr.Zero, (IntPtr)0x401);
                    if (!clicked) throw new Exception("Tray keyboard activation missing");
                    Console.WriteLine("PASS: Native tray mouse and keyboard callbacks open Notaker");
                    tray.ContextMenuStrip = new Forms.ContextMenuStrip();
                    tray.ContextMenuStrip.Items.Add("Abrir Notaker");
                    SendMessage(tray.Handle, 0x8001, (IntPtr)((100 << 16) | 100), (IntPtr)0x7b);
                    Forms.Application.DoEvents();
                    if (!tray.ContextMenuStrip.Visible) throw new Exception("Tray context menu missing");
                    tray.ContextMenuStrip.Close();
                    Console.WriteLine("PASS: Native tray context menu opens and closes");
                    if (showNotification)
                    {
                        if (!tray.ShowBalloonTip(2500, "Notaker sigue activo", "Usa el atajo para dictar. Para cerrar, elige Salir en la bandeja.", Forms.ToolTipIcon.Info))
                            throw new Exception("Windows rejected branded notification");
                        Console.WriteLine("PASS: Windows accepted notification with Notaker hBalloonIcon");
                        var end = DateTime.UtcNow.AddSeconds(4);
                        while (DateTime.UtcNow < end) { Forms.Application.DoEvents(); Thread.Sleep(30); }
                    }
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
