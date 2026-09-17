using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notaker;

public sealed record StatisticsSnapshot
{
    public int Version { get; init; } = 1;
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public long ImportedDictations { get; init; }
    public long Dictations { get; init; }
    public long Words { get; init; }
    public double SpeakingSeconds { get; init; }
    public long AiEditedDictations { get; init; }
    public long AiWordEdits { get; init; }
    public long ManualCorrections { get; init; }
    public long ManualWordEdits { get; init; }
    public long LearnedTerms { get; init; }
    public long AddedTerms { get; init; }
    [JsonIgnore] public double WordsPerMinute => SpeakingSeconds > 0 ? Words * 60.0 / SpeakingSeconds : 0;
}

// Aggregate counters only: never stores transcripts, audio or dictionary terms.
public sealed class StatisticsStore
{
    private readonly string path;
    public StatisticsSnapshot Data { get; private set; } = new();
    public string? Warning { get; private set; }
    public StatisticsStore(string root, IEnumerable<Dictation> history, bool importHistory)
    {
        path = Path.Combine(root, "statistics.json");
        var existed = File.Exists(path);
        try
        {
            if (existed)
            {
                var data = JsonSerializer.Deserialize<StatisticsSnapshot>(File.ReadAllText(path));
                if (data == null || data.Version != 1 || !double.IsFinite(data.SpeakingSeconds) || data.SpeakingSeconds < 0 ||
                    new[] { data.ImportedDictations, data.Dictations, data.Words, data.AiEditedDictations, data.AiWordEdits, data.ManualCorrections, data.ManualWordEdits, data.LearnedTerms, data.AddedTerms }.Any(n => n < 0))
                    throw new JsonException("Invalid statistics.");
                Data = data;
                return;
            }
            if (importHistory)
            {
                foreach (var item in history.Where(d => !string.IsNullOrWhiteSpace(d.Text) && double.IsFinite(d.Seconds) && d.Seconds > 0))
                    Data = Data with { Dictations = Data.Dictations + 1, Words = Data.Words + TextMetrics.WordCount(item.OriginalText ?? item.Text), SpeakingSeconds = Data.SpeakingSeconds + item.Seconds, ImportedDictations = Data.ImportedDictations + 1 };
            }
            Save(Data);
        }
        catch (JsonException)
        {
            try { File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N")); Save(new()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Warning = "Las estadísticas estaban dañadas. Se inicia un contador nuevo; el historial permanece intacto.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Warning = "No se pudieron leer o guardar las estadísticas. Los totales podrían estar incompletos."; }
    }
    private bool Save(StatisticsSnapshot next)
    {
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
            Data = next;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warning = "No se pudieron guardar algunas estadísticas. Los totales podrían estar incompletos.";
            return false;
        }
    }
    public void RecordDictation(Dictation item)
    {
        if (string.IsNullOrWhiteSpace(item.Text) || !double.IsFinite(item.Seconds) || item.Seconds <= 0) return;
        Save(Data with
        {
            Dictations = Data.Dictations + 1,
            Words = Data.Words + TextMetrics.WordCount(item.OriginalText ?? item.Text),
            SpeakingSeconds = Data.SpeakingSeconds + item.Seconds,
            AiEditedDictations = Data.AiEditedDictations + (item.AiWordEdits.HasValue && item.Text != item.OriginalText ? 1 : 0),
            AiWordEdits = Data.AiWordEdits + Math.Max(0, item.AiWordEdits ?? 0)
        });
    }
    public void RecordCorrection(string before, string after)
    {
        if (before == after) return;
        Save(Data with { ManualCorrections = Data.ManualCorrections + 1, ManualWordEdits = Data.ManualWordEdits + TextMetrics.WordEdits(before, after) });
    }
    public void RecordTerms(int added, bool learned)
    {
        if (added <= 0) return;
        Save(learned ? Data with { LearnedTerms = Data.LearnedTerms + added } : Data with { AddedTerms = Data.AddedTerms + added });
    }
    public bool Reset()
    {
        if (!Save(new())) return false;
        Warning = null;
        return true;
    }
}
