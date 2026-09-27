using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Notaker;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private BundleLease? bundleLease;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && (e.Args[0] is "--smoke-test" or "--recovery-smoke-test"))
            AppLog.Configure(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "smoke-data"));
        AppLog.Write("app.start");
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Write("app.unhandled", args.ExceptionObject as Exception);
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, args) =>
        {
            AppLog.Write("winforms.callback.failed", args.Exception);
            Dispatcher.BeginInvoke(new Action(() => { if (MainWindow is MainWindow main) main.ReportUiError(args.Exception); }));
        };
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window window) { window.Icon ??= BrandAssets.WindowIcon(); Native.DarkCaption(window); } }));
        try
        {
            bundleLease = BundleLease.Acquire();
            AppLog.Write("dependencies.protected");
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
            var smoke = e.Args.Length == 2 && (e.Args[0] is "--smoke-test" or "--recovery-smoke-test");
            instance = new Mutex(true, smoke ? "Local\\Notaker.SmokeTest" : "Local\\Notaker.Desktop", out var first);
            if (!first && e.Args.Contains("--startup")) { Shutdown(); return; }
            if (!first) { System.Windows.MessageBox.Show("Notaker ya está abierto. Búscalo en la bandeja del sistema.", "Notaker"); Shutdown(); return; }
            var window = new MainWindow(smoke ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "smoke-data") : null);
            MainWindow = window;
            window.Show();
            if (smoke && e.Args[0] == "--recovery-smoke-test") await window.VerifyRecoveryFailureForSmokeAsync();
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
                var recovery = new RecoveryWindow(new RecoveryStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "smoke-data"))) { Owner = window };
                recovery.Show(); await Task.Delay(200); recovery.UpdateLayout();
                var recoveryImage = new RenderTargetBitmap((int)recovery.ActualWidth, (int)recovery.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                recoveryImage.Render(recovery);
                var recoveryEncoder = new PngBitmapEncoder(); recoveryEncoder.Frames.Add(BitmapFrame.Create(recoveryImage));
                using (var output = File.Create(Path.ChangeExtension(e.Args[1], ".recovery.png"))) recoveryEncoder.Save(output);
                recovery.Close();
                await window.ExitAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("app.start.failed", ex);
            if (e.Args.Length > 0) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostic-error.txt"), ex.ToString()); Shutdown(1); }
            else { System.Windows.MessageBox.Show(ex.Message, "No se pudo iniciar Notaker"); Shutdown(1); }
        }
    }
    protected override void OnExit(ExitEventArgs e) { AppLog.Write("app.exit"); bundleLease?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}
