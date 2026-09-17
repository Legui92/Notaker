using System.Windows;

namespace Notaker;

public partial class WritingSettingsWindow : Window
{
    private readonly Storage storage;
    private bool removeKey;
    private readonly CancellationTokenSource lifetime = new();
    public WritingSettingsWindow(Storage storage)
    {
        this.storage = storage; InitializeComponent();
        CleanBox.IsChecked = storage.Settings.CleanWithAi;
        LearnBox.IsChecked = storage.Settings.LearnVocabulary;
        ApiModelBox.Text = storage.Settings.ApiModel;
        VocabularyBox.Text = string.Join(Environment.NewLine, storage.Vocabulary);
        KeyStatus.Text = storage.Settings.ProtectedApiKey.Length > 0 ? "Clave guardada. Déjalo vacío para conservarla." : "La clave se protege con tu cuenta de Windows.";
        Closed += (_, _) => lifetime.Cancel();
    }
    private string CurrentKey() => !string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? ApiKeyBox.Password.Trim() : removeKey ? "" : SecretStore.Unprotect(storage.Settings.ProtectedApiKey);
    private void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        removeKey = true; ApiKeyBox.Clear(); CleanBox.IsChecked = false;
        KeyStatus.Text = "La clave se eliminará cuando guardes los cambios.";
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = CurrentKey();
            if (CleanBox.IsChecked == true && key.Length == 0) { Feedback.Text = "Introduce una clave para activar la limpieza con IA."; return; }
            var model = ApiModelBox.Text.Trim();
            if (model.Length is < 1 or > 100 || model.Any(char.IsWhiteSpace)) { Feedback.Text = "Indica un identificador de modelo válido."; return; }
            storage.SaveVocabulary(VocabularyBox.Text.Split('\n'));
            storage.Settings.ProtectedApiKey = SecretStore.Protect(key);
            storage.Settings.CleanWithAi = CleanBox.IsChecked == true;
            storage.Settings.LearnVocabulary = LearnBox.IsChecked == true;
            storage.Settings.ApiModel = model;
            storage.SaveSettings(); DialogResult = true;
        }
        catch (Exception ex) { Feedback.Text = "No se pudo guardar: " + ex.Message; }
    }
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        Feedback.Text = "Comprobando con una frase de ejemplo; no se envía tu historial.";
        try
        {
            using var polisher = new TextPolisher();
            var result = await polisher.PolishAsync("Eh, necesito enviar el informe mañana, eh, antes de las nueve.", CurrentKey(), ApiModelBox.Text.Trim(), [], lifetime.Token);
            Feedback.Text = "Conexión correcta. Ejemplo: " + result;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Feedback.Text = "No se pudo completar la prueba: " + ex.Message; }
        finally { TestButton.IsEnabled = true; }
    }
}
