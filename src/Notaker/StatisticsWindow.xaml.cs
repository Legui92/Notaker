using System.Globalization;
using System.Windows;

namespace Notaker;

public partial class StatisticsWindow : Window
{
    private readonly Storage storage;
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("es-CO");
    public StatisticsWindow(Storage storage)
    {
        this.storage = storage;
        InitializeComponent(); Refresh();
    }
    private static string Number(long value) => value.ToString("N0", DisplayCulture);
    private void Refresh()
    {
        var data = storage.Statistics.Data;
        WordsValue.Text = Number(data.Words);
        PaceValue.Text = data.SpeakingSeconds > 0 ? data.WordsPerMinute.ToString("N1", DisplayCulture) : "—";
        var time = TimeSpan.FromSeconds(data.SpeakingSeconds);
        TimeValue.Text = time.TotalHours >= 1 ? $"{(long)time.TotalHours} h {time.Minutes} min" : time.TotalMinutes >= 1 ? $"{(long)time.TotalMinutes} min {time.Seconds} s" : $"{time.Seconds} s";
        DictationsValue.Text = Number(data.Dictations);
        AiValue.Text = Number(data.AiWordEdits);
        AiDetail.Text = $"En {Number(data.AiEditedDictations)} dictados retocados";
        ManualValue.Text = Number(data.ManualWordEdits);
        ManualDetail.Text = $"En {Number(data.ManualCorrections)} correcciones guardadas";
        LearnedValue.Text = Number(data.LearnedTerms);
        AddedValue.Text = Number(data.AddedTerms);
        VocabularyValue.Text = Number(storage.Vocabulary.Count);
        CoverageText.Text = $"Registro de correcciones desde {data.StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}. " +
            (data.ImportedDictations > 0 ? $"Uso importado de {Number(data.ImportedDictations)} dictados del historial disponible (máximo 200). Sus correcciones previas no se pueden reconstruir." : "Los contadores incluyen solo la actividad registrada desde su inicio.");
        TrackBox.IsChecked = storage.Settings.TrackStatistics;
        WarningText.Text = storage.Statistics.Warning ?? (storage.Settings.TrackStatistics ? "" : "Registro pausado. La actividad nueva no se sumará mientras esté desactivado.");
    }
    private void Track_Click(object sender, RoutedEventArgs e)
    {
        var before = storage.Settings.TrackStatistics;
        try { storage.Settings.TrackStatistics = TrackBox.IsChecked == true; storage.SaveSettings(); Refresh(); }
        catch (Exception ex) { storage.Settings.TrackStatistics = before; TrackBox.IsChecked = before; WarningText.Text = "No se pudo guardar: " + ex.Message; }
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Se pondrán a cero los contadores. Tus dictados, vocabulario y ajustes se conservan. Esta acción no se puede deshacer.", "Reiniciar estadísticas", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        storage.Statistics.Reset(); Refresh();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
