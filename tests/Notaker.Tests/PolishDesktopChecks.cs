using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Notaker;

internal static class PolishDesktopChecks
{
    public static Task Run(string root, string screenshot, string? physicalModel = null)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "src/Notaker/App.xaml"));
            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var resources = xaml.Root!.Element(ns + "Application.Resources")!;
            var dictionary = new System.Xml.Linq.XElement(ns + "ResourceDictionary", new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), resources.Elements());
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
            var settings = new Storage(root);
            settings.Settings.CleanWithAi = true; settings.Settings.AutoPaste = false;
            settings.Settings.ProtectedApiKey = SecretStore.Protect("synthetic"); settings.SaveSettings();
            var recovery = new RecoveryStore(root);
            var handler = new PolishTimeoutChecks.StalledHandler();
            using var polisher = new TextPolisher(handler);
            var client = (HttpClient)typeof(TextPolisher).GetField("http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(polisher)!;
            client.Timeout = TimeSpan.FromSeconds(2);
            var window = new MainWindow(root, polisher);
            window.Loaded += async (_, _) =>
            {
                try
                {
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        const string raw = "Texto original de prueba. Keep the pull request.";
                        string id;
                        using (var capture = recovery.Begin()) { capture.Append(new byte[32000], 32000); id = capture.Id; }
                        recovery.SaveText(id, raw);
                        var task = (Task)typeof(MainWindow).GetMethod("RetryRecoveryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [id])!;
                        var button = (Button)window.FindName("UseOriginalButton");
                        for (int i = 0; i < 40 && button.Visibility != Visibility.Visible; i++) await Task.Delay(25);
                        if (button.Visibility != Visibility.Visible || !button.IsEnabled) throw new Exception("Original text escape not available while polishing");
                        if (attempt == 0)
                        {
                            window.UpdateLayout();
                            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(screenshot); encoder.Save(file);
                        }
                        else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await task.WaitAsync(TimeSpan.FromSeconds(25));
                        if (!((Grid)window.FindName("SettingsPanel")).IsEnabled || button.Visibility != Visibility.Collapsed) throw new Exception("Controls stayed blocked after AI wait");
                        if (new Storage(root).History[0].Text != raw) throw new Exception("Original text was lost");
                        Console.WriteLine("PASS: recovery " + (attempt == 0 ? "timeout" : "manual skip") + " delivers original text and unlocks controls");
                    }
                    const string liveText = "Mañana revisamos el pull request. Keep this sentence.";
                    var live = new LiveTranscription((_, _) => Task.FromResult(liveText), CancellationToken.None);
                    live.Append(new byte[] { 1, 0, 2, 0 }, 4, .1f);
                    typeof(MainWindow).GetField("liveTranscription", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, live);
                    string liveId;
                    using (var capture = recovery.Begin()) { capture.Append(new byte[32000], 32000); liveId = capture.Id; }
                    var complete = (Task)typeof(MainWindow).GetMethod("CompleteDictationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, [Array.Empty<byte>(), 20.0, liveId])!;
                    await complete.WaitAsync(TimeSpan.FromSeconds(15));
                    var delivered = new Storage(root).History[0];
                    if (delivered.Text != liveText || !delivered.RecognitionDuringRecording || recovery.Entries().Count != 0)
                        throw new Exception("Live result did not survive AI timeout/history/recovery completion");
                    Console.WriteLine("PASS: live result survives AI timeout, records final-wait mode and completes recovery lifecycle");
                    await live.DisposeAsync();
                    handler.Reply = "Texto pulido de prueba. Keep the pull request.";
                    var callsBefore = handler.Calls;
                    var second = new LiveTranscription((_, _) => Task.FromResult(liveText), CancellationToken.None);
                    second.Append(new byte[] { 1, 0 }, 2, .1f);
                    typeof(MainWindow).GetField("liveTranscription", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, second);
                    using (var capture = recovery.Begin()) { capture.Append(new byte[32000], 32000); liveId = capture.Id; }
                    complete = (Task)typeof(MainWindow).GetMethod("CompleteDictationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, [Array.Empty<byte>(), 20.0, liveId])!;
                    await complete.WaitAsync(TimeSpan.FromSeconds(10));
                    using var requestJson = System.Text.Json.JsonDocument.Parse(handler.LastBody!);
                    using var inputJson = System.Text.Json.JsonDocument.Parse(requestJson.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
                    delivered = new Storage(root).History[0];
                    if (handler.Calls != callsBefore + 1 || inputJson.RootElement.GetProperty("dictated_text").GetString() != liveText
                        || delivered.Text != handler.Reply || delivered.OriginalText != liveText)
                        throw new Exception("Live mode skipped or duplicated the final AI call");
                    Console.WriteLine("PASS: live mode makes exactly one final AI request and preserves polished and original text (mock HTTP)");
                    await second.DisposeAsync();
                    typeof(MainWindow).GetField("liveTranscription", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var overlay = (Window)typeof(MainWindow).GetField("overlay", flags)!.GetValue(window)!;
                    var update = overlay.GetType().GetMethod("Update", flags)!;
                    foreach (var state in new[] { "Preparando · aún no graba", "Escuchando · 02:53", "Transcribiendo…", "Puliendo escritura…" })
                    {
                        update.Invoke(overlay, [state, .05f, !state.StartsWith("Escuchando"), true]);
                        overlay.UpdateLayout();
                        await overlay.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                        await Task.Delay(30);
                        var image = new RenderTargetBitmap((int)overlay.Width, (int)overlay.Height, 96,96,PixelFormats.Pbgra32); image.Render(overlay);
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                        using var output = File.Create(screenshot + "." + (state.StartsWith("Escuchando") ? "listening" : state.StartsWith("Preparando") ? "preparing" : state.StartsWith("Transcribiendo") ? "transcribing" : "polishing") + ".png"); png.Save(output);
                        if (overlay.Height > 44 || overlay.Width > 280) throw new Exception("Overlay is not compact");
                    }
                    Console.WriteLine("PASS: all four compact overlay states render at up to280x44");
                    var historyCount = new Storage(root).History.Count;
                    var cancelButton = (Button)overlay.GetType().GetField("cancel", flags)!.GetValue(overlay)!;
                    for (var phase = 0; phase < 2; phase++)
                    {
                        using var cancellation = new CancellationTokenSource();
                        typeof(MainWindow).GetField("dictationCancellation", flags)!.SetValue(window, cancellation);
                        typeof(MainWindow).GetField("busy", flags)!.SetValue(window, true);
                        using (var capture = recovery.Begin()) { capture.Append(new byte[32000],32000); liveId = capture.Id; }
                        typeof(MainWindow).GetField("activeRecoveryId", flags)!.SetValue(window, liveId);
                        if (phase == 0)
                        {
                            var pending = new LiveTranscription(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return "Never delivered"; }, cancellation.Token);
                            pending.Append(new byte[] {1,0},2,.1f);
                            typeof(MainWindow).GetField("liveTranscription", flags)!.SetValue(window, pending);
                        }
                        else { recovery.SaveText(liveId, liveText); handler.Reply = null; }
                        Clipboard.SetText("cancel-sentinel");
                        var previousCalls = handler.Calls;
                        complete = (Task)typeof(MainWindow).GetMethod("CompleteDictationAsync",flags)!.Invoke(window,[Array.Empty<byte>(),20.0,liveId])!;
                        await Task.Delay(80);
                        cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        try { await complete.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Cancelled pipeline delivered"); }
                        catch (OperationCanceledException) { }
                        await (Task)typeof(MainWindow).GetMethod("DiscardDictationAsync",flags)!.Invoke(window,null)!;
                        if (Clipboard.GetText() != "cancel-sentinel" || new Storage(root).History.Count != historyCount || recovery.Entries().Count != 0
                            || handler.Calls != previousCalls + phase) throw new Exception("Cancelled dictation leaked output or an AI call");
                        typeof(MainWindow).GetField("dictationCancellation", flags)!.SetValue(window,null);
                        typeof(MainWindow).GetField("busy", flags)!.SetValue(window,false);
                    }
                    Console.WriteLine("PASS: overlay cancel drains live recognition and aborts AI without clipboard/history output or a new AI call");
                    using (var capture = recovery.Begin()) { capture.Append(new byte[32000],32000); liveId = capture.Id; }
                    typeof(MainWindow).GetField("activeRecoveryId", flags)!.SetValue(window,null);
                    await (Task)typeof(MainWindow).GetMethod("DiscardDictationAsync",flags)!.Invoke(window,null)!;
                    if (recovery.Entries().Count != 1) throw new Exception("Preflight cancel deleted an unrelated recovery");
                    recovery.Delete(liveId);
                    Console.WriteLine("PASS: cancelling before capture preserves previous recovery recordings");
                    if (physicalModel != null) await PhysicalCancelChecks.Run(window,root,physicalModel);
                }
                catch (Exception ex) { failure = ex; }
                finally { await window.ExitAsync(); }
            };
            app.Run(window);
            if (failure != null) done.TrySetException(failure); else done.TrySetResult();
        });
        thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }
}
