using System.IO;
using System.Windows;

namespace Notaker;

public partial class UpdateWindow : Window
{
    private readonly Storage storage;
    private readonly Func<Task> shutdown;
    private readonly UpdateService service = new();
    private readonly CancellationTokenSource lifetime = new();
    private AvailableUpdate? available;
    private bool working;
    public UpdateWindow(Storage storage, Func<Task> shutdown)
    {
        this.storage = storage; this.shutdown = shutdown;
        InitializeComponent(); VersionText.Text = "Versión instalada · " + UpdateService.VersionLabel;
        Closing += (_, e) => { if (working) { lifetime.Cancel(); e.Cancel = true; Status.Text = "Cancelando la descarga…"; } };
        Closed += (_, _) => { lifetime.Cancel(); service.Dispose(); };
    }
    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (working) return;
        working = true; ActionButton.IsEnabled = false;
        try
        {
            if (available == null)
            {
                Status.Text = "Consultando versiones en GitHub…";
                available = await service.CheckAsync(lifetime.Token);
                Status.Text = available == null ? "Estás al día. No hay una versión estable más reciente publicada." : $"La versión {available.Version} está lista. Pulsa Actualizar y reiniciar para instalarla.";
                ActionButton.Content = available == null ? "Volver a comprobar" : "Actualizar y reiniciar";
            }
            else
            {
                Status.Text = "Descargando la actualización…"; Progress.Visibility = Visibility.Visible;
                var directory = Path.Combine(storage.Root, "updates", Guid.NewGuid().ToString("N"));
                var path = await service.DownloadAsync(available, directory, new Progress<double>(value => { Progress.Value = value; Status.Text = $"Descargando · {value:0}%"; }), lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                UpdateService.LaunchInstaller(path, available.Sha256);
                working = false; Close(); await shutdown();
            }
        }
        catch (OperationCanceledException) { working = false; Close(); }
        catch (Exception ex) { Status.Text = "No se pudo actualizar: " + ex.Message; }
        finally { working = false; ActionButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed; }
    }
}
