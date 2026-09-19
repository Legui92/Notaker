using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Notaker;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window window) { window.Icon ??= new BitmapImage(new Uri("pack://application:,,,/Assets/notaker.ico")); Native.DarkCaption(window); } }));
        try
        {
            if (e.Args.Length == 2 && e.Args[0] == "--apply-update")
            {
                await UpdateInstaller.ApplyAsync(e.Args[1]); Shutdown(0); return;
            }
            if (e.Args.Length == 3 && e.Args[0] == "--update-confirm" && e.Args[2] == "--update-test-fail")
            {
                Shutdown(1); return;
            }
            if (e.Args.Length == 3 && e.Args[0] == "--update-confirm" && e.Args[2] == "--update-test")
            {
                await File.WriteAllTextAsync(e.Args[1], UpdateService.VersionLabel); Shutdown(0); return;
            }
            if (e.Args.Length >= 4 && e.Args[0] == "--transcribe")
            {
                using var engine = new Transcriber();
                var result = await engine.TranscribeAsync(e.Args[2], await File.ReadAllBytesAsync(e.Args[1]), e.Args.Length > 4 ? e.Args[4] : "auto", CancellationToken.None);
                await File.WriteAllTextAsync(e.Args[3], result);
                Shutdown(0); return;
            }
            var smoke = e.Args.Length == 2 && e.Args[0] == "--smoke-test";
            instance = new Mutex(true, smoke ? "Local\\Notaker.SmokeTest" : "Local\\Notaker.Desktop", out var first);
            if (!first && e.Args.Contains("--startup")) { Shutdown(); return; }
            if (!first) { System.Windows.MessageBox.Show("Notaker ya está abierto. Búscalo en la bandeja del sistema.", "Notaker"); Shutdown(); return; }
            var window = new MainWindow(smoke ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "smoke-data") : null);
            MainWindow = window;
            window.Show();
            if (e.Args.Length == 2 && e.Args[0] == "--update-confirm")
                await File.WriteAllTextAsync(e.Args[1], UpdateService.VersionLabel);
            if (smoke)
            {
                await Task.Delay(600);
                window.UpdateLayout();
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var output = File.Create(e.Args[1])) encoder.Save(output);
                var settings = new WritingSettingsWindow(new Storage(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "smoke-data"))) { Owner = window };
                settings.Show();
                await Task.Delay(250);
                settings.UpdateLayout();
                var settingsImage = new RenderTargetBitmap((int)settings.ActualWidth, (int)settings.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                settingsImage.Render(settings);
                var settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsImage));
                using (var output = File.Create(Path.ChangeExtension(e.Args[1], ".settings.png"))) settingsEncoder.Save(output);
                settings.Close();
                var updates = new UpdateWindow(new Storage(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "smoke-data")), () => Task.CompletedTask) { Owner = window };
                updates.Show(); await Task.Delay(200); updates.UpdateLayout();
                var updateImage = new RenderTargetBitmap((int)updates.ActualWidth, (int)updates.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                updateImage.Render(updates);
                var updateEncoder = new PngBitmapEncoder(); updateEncoder.Frames.Add(BitmapFrame.Create(updateImage));
                using (var output = File.Create(Path.ChangeExtension(e.Args[1], ".updates.png"))) updateEncoder.Save(output);
                updates.Close();
                var statistics = new StatisticsWindow(new Storage(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "smoke-data"))) { Owner = window };
                statistics.Show(); await Task.Delay(200); statistics.UpdateLayout();
                var statsImage = new RenderTargetBitmap((int)statistics.ActualWidth, (int)statistics.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                statsImage.Render(statistics);
                var statsEncoder = new PngBitmapEncoder(); statsEncoder.Frames.Add(BitmapFrame.Create(statsImage));
                using (var output = File.Create(Path.ChangeExtension(e.Args[1], ".statistics.png"))) statsEncoder.Save(output);
                statistics.Close();
                await window.ExitAsync();
            }
        }
        catch (Exception ex)
        {
            if (e.Args.Length > 0) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostic-error.txt"), ex.ToString()); Shutdown(1); }
            else { System.Windows.MessageBox.Show(ex.Message, "No se pudo iniciar Notaker"); Shutdown(1); }
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
