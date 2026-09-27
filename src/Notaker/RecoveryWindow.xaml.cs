using System.IO;
using System.Windows;

namespace Notaker;

public partial class RecoveryWindow : Window
{
    private readonly RecoveryStore store;
    public string? SelectedId { get; private set; }
    public RecoveryWindow(RecoveryStore store) { this.store = store; InitializeComponent(); Refresh(); }
    private void Refresh()
    {
        EntriesList.ItemsSource = store.Entries(); EntriesList.SelectedIndex = 0;
        Hint.Text = EntriesList.Items.Count == 0 ? "No hay dictados pendientes." : "Al reintentar, el texto se copiará al portapapeles. La exportación WAV queda sin cifrar en la carpeta que elijas.";
    }
    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not RecoveryEntry entry) return;
        SelectedId = entry.Id; DialogResult = true;
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not RecoveryEntry entry) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Notaker-" + entry.CreatedAt.ToString("yyyyMMdd-HHmmss") + ".wav", Filter = "Audio WAV|*.wav" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, store.ReadAudio(entry.Id)); Hint.Text = "Audio exportado. La copia de recuperación se conserva."; }
        catch (Exception ex) { AppLog.Write("recovery.export.failed", ex); Hint.Text = "No se pudo exportar: " + ex.Message; }
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not RecoveryEntry entry) return;
        if (MessageBox.Show(this, "¿Eliminar definitivamente esta copia de audio y texto?", "Eliminar dictado pendiente", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { store.Delete(entry.Id); Refresh(); }
        catch (Exception ex) { AppLog.Write("recovery.delete.failed", ex); Hint.Text = "No se pudo eliminar la copia."; }
    }
}
