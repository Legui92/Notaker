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
    public static Task Run(string root, string screenshot)
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
            using var polisher = new TextPolisher(new PolishTimeoutChecks.StalledHandler());
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
