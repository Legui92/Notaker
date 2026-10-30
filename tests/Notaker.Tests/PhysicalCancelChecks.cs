using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Notaker;

internal static class PhysicalCancelChecks
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, Flags)!.SetValue(window, value);
    static object? Get(MainWindow window, string name) => typeof(MainWindow).GetField(name, Flags)!.GetValue(window);
    public static async Task Run(MainWindow window, string root, string model)
    {
        // Real device audio stays in this disposable test recording and is never transcribed or sent to an API.
        var recovery = new RecoveryStore(root);
        var overlay = (Window)Get(window, "overlay")!;
        var update = overlay.GetType().GetMethod("Update", Flags)!;
        var editor = new Window { Title = "Notaker isolated focus test", Width = 400, Height = 140, Content = new TextBox { Text = "focus-sentinel" }, Topmost = true };
        GetCursorPos(out var originalCursor);
        try
        {
            editor.Show(); editor.Activate(); ((TextBox)editor.Content).Focus();
            await Task.Delay(150);
            var target = new WindowInteropHelper(editor).Handle;
            for (var attempt = 0; attempt < 300 && GetForegroundWindow() != target; attempt++) await Task.Delay(100);
            if (GetForegroundWindow() != target) throw new Exception("Cannot establish foreground for physical focus test");
            Clipboard.SetText("physical-cancel-sentinel");
            for (var failure = 0; failure < 2; failure++)
            {
                using var cancellation = new CancellationTokenSource();
                var backup = recovery.Begin();
                var recorder = new Recorder(-1, backup);
                Set(window, "activeRecoveryId", backup.Id); Set(window, "dictationCancellation", cancellation);
                Set(window, "recorder", recorder); Set(window, "busy", false);
                var drained = false;
                var live = new LiveTranscription(async (_, token) => { try { await Task.Delay(Timeout.Infinite, token); return "never"; } finally { drained = true; } }, cancellation.Token);
                // Queue one synthetic chunk so the cancellation must also drain a decoder worker.
                for (int i = 0; i < 300; i++) live.Append(new byte[3200],3200,.1f);
                for (int i = 0; i < 7; i++) live.Append(new byte[3200],3200,0);
                Set(window, "liveTranscription", live);
                await Task.Delay(100);
                if (failure == 0) { recorder.Start(); await Task.Delay(500); }
                else
                {
                    var stopped = (TaskCompletionSource<byte[]>)typeof(Recorder).GetField("stopped",Flags)!.GetValue(recorder)!;
                    stopped.TrySetException(new IOException("Synthetic device-stop failure"));
                }
                update.Invoke(overlay, ["Escuchando · 00:01",.1f,false,true]);
                await Task.Delay(150);
                var location = overlay.PointToScreen(new Point(overlay.Width - 21,overlay.Height / 2));
                SetCursorPos((int)location.X,(int)location.Y);
                mouse_event(2,0,0,0,UIntPtr.Zero); mouse_event(4,0,0,0,UIntPtr.Zero);
                await Task.Delay(100);
                var operation = (Task?)Get(window,"operation");
                if (operation == null) throw new Exception("Physical cancel click did not invoke operation");
                await operation.WaitAsync(TimeSpan.FromSeconds(8));
                if (GetForegroundWindow() != target || Get(window,"recorder") != null || Get(window,"liveTranscription") != null || !drained
                    || (bool)Get(window,"busy")! || Clipboard.GetText() != "physical-cancel-sentinel")
                    throw new Exception("Physical cancel lost focus, delivered text or released before decoder drain");
                if (failure == 0 && recovery.Entries().Count != 0) throw new Exception("Cancelled microphone recording retained unexpectedly");
                if (failure == 1 && recovery.Entries().Count != 1) throw new Exception("Device-stop failure did not retain backup");
                foreach (var item in recovery.Entries()) recovery.Delete(item.Id);
                Console.WriteLine(failure == 0 ? "PASS: real microphone cancel via physical mouse preserves foreground and clipboard and drains live decoder" : "PASS: injected recorder-stop failure retains backup and drains decoder before unlocking controls");
            }
            using var engine = new Transcriber();
            using var cancelled = new CancellationTokenSource();
            var log = Path.Combine(root,"logs","notaker.log");
            var previousLines = File.Exists(log) ? File.ReadAllLines(log).Length : 0;
            var preparation = engine.PrepareAsync(model,true,cancelled.Token);
            for (int i = 0; i < 100 && !preparation.IsCompleted; i++)
            {
                if (File.Exists(log) && File.ReadLines(log).Skip(previousLines).Any(line => line.Contains("model.load.gpu"))) break;
                await Task.Delay(5);
            }
            if (preparation.IsCompleted || !File.ReadLines(log).Skip(previousLines).Any(line => line.Contains("model.load.gpu"))) throw new Exception("Cannot establish native load in progress");
            cancelled.Cancel();
            try { await preparation; throw new Exception("Native preparation ignored cancellation"); }
            catch (OperationCanceledException) { }
            await engine.PrepareAsync(model,true,CancellationToken.None);
            var text = await engine.TranscribeAsync(model,await File.ReadAllBytesAsync("artifacts/qa/spanish.wav"),"es",CancellationToken.None);
            if (!text.Contains("mañana",StringComparison.OrdinalIgnoreCase)) throw new Exception("Engine could not recover after preparation cancellation");
            Console.WriteLine("PASS: actual native preparation cancellation returns safely and next recognition succeeds");
        }
        finally { overlay.Hide(); editor.Close(); SetCursorPos(originalCursor.X,originalCursor.Y); }
    }
    [StructLayout(LayoutKind.Sequential)] struct PointInt { public int X,Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out PointInt point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
