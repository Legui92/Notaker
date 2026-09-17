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
    private string key = "";
    public UpdateWindow(Storage storage, Func<Task> shutdown)
    {
        this.storage = storage; this.shutdown = shutdown;
        InitializeComponent(); VersionText.Text = "Versión instalada · " + UpdateService.VersionLabel;
        TokenState.Text = string.IsNullOrEmpty(storage.Settings.ProtectedGitHubToken) ? "GitHub CLI / sin token" : "Token protegido guardado";
        Closing += (_, e) => { if (working) { lifetime.Cancel(); e.Cancel = true; Status.Text = "Cancelando la descarga…"; } };
        Closed += (_, _) => { lifetime.Cancel(); service.Dispose(); };
    }
    private void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        if (working || string.IsNullOrWhiteSpace(TokenBox.Password)) return;
        try { storage.Settings.ProtectedGitHubToken = SecretStore.Protect(TokenBox.Password); storage.SaveSettings(); TokenBox.Clear(); TokenState.Text = "Token protegido guardado"; available = null; ActionButton.Content = "Buscar actualizaciones"; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        if (working) return;
        try { storage.Settings.ProtectedGitHubToken = ""; storage.SaveSettings(); TokenBox.Clear(); key = ""; available = null; ActionButton.Content = "Buscar actualizaciones"; TokenState.Text = "Se usará GitHub CLI"; }
        catch (Exception ex) { Status.Text = ex.Message; }
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
                key = await UpdateService.GetTokenAsync(storage.Settings.ProtectedGitHubToken, lifetime.Token);
                available = await service.CheckAsync(key, lifetime.Token);
                Status.Text = available == null ? "Estás al día. No hay una versión estable más reciente publicada." : $"La versión {available.Version} está lista. Pulsa Actualizar y reiniciar para instalarla.";
                ActionButton.Content = available == null ? "Volver a comprobar" : "Actualizar y reiniciar";
            }
            else
            {
                Status.Text = "Descargando la actualización…"; Progress.Visibility = Visibility.Visible;
                var directory = Path.Combine(storage.Root, "updates", Guid.NewGuid().ToString("N"));
                var path = await service.DownloadAsync(available, key, directory, new Progress<double>(value => { Progress.Value = value; Status.Text = $"Descargando · {value:0}%"; }), lifetime.Token);
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
