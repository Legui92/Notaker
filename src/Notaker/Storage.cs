using System.IO;
using System.Text.Json;

namespace Notaker;

public sealed record Dictation(string Text, DateTime CreatedAt, double Seconds)
{
    public string Caption => $"{CreatedAt:dd MMM · HH:mm}   /   {Seconds:0} s" + (RecognitionSeconds is double voice ? $" · Voz {voice:0.0} s" : "") + (PolishSeconds is double ai ? $" · IA {ai:0.0} s" : "");
    public string? OriginalText { get; init; }
    public double? RecognitionSeconds { get; init; }
    public double? PolishSeconds { get; init; }
    public int? AiWordEdits { get; init; }
}

public sealed class Preferences
{
    public string Language { get; set; } = "auto";
    public string Model { get; set; } = "large-v3-turbo-q8_0";
    public int Hotkey { get; set; }
    public DictationShortcut? CustomHotkey { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DictationShortcut Shortcut => CustomHotkey is { IsValid: true } ? CustomHotkey : DictationShortcut.FromLegacy(Hotkey);
    public int Microphone { get; set; } = -1;
    public bool KeepHistory { get; set; } = true;
    public bool TrackStatistics { get; set; } = true;
    public bool AutoPaste { get; set; } = true;
    public bool UseGpu { get; set; } = true;
    public bool CleanWithAi { get; set; }
    public bool LearnVocabulary { get; set; } = true;
    public string ApiModel { get; set; } = "deepseek-flash";
    public string ProtectedApiKey { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ProtectedGitHubToken { get; set; } // Legacy migration only; never used for authentication.
}

public sealed class Storage
{
    public string Root { get; }
    public string ModelPath => Path.Combine(Root, "models", $"ggml-{Settings.Model}.bin");
    public Preferences Settings { get; }
    public List<Dictation> History { get; }
    public StatisticsStore Statistics { get; }
    public List<string> Vocabulary { get; private set; }
    public string? LoadWarning { get; private set; }

    public Storage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notaker");
        Directory.CreateDirectory(Path.Combine(Root, "models"));
        Settings = Read<Preferences>("settings.json") ?? new();
        if (Settings.Language is not ("auto" or "es" or "en" or "mixed")) Settings.Language = "auto";
        if (!VoiceModels.IsSupported(Settings.Model)) Settings.Model = "base";
        Settings.Hotkey = Math.Clamp(Settings.Hotkey, 0, 2);
        if (Settings.ProtectedGitHubToken != null)
        {
            Settings.ProtectedGitHubToken = null;
            try { SaveSettings(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LoadWarning = "No se pudo eliminar el antiguo acceso de GitHub de los ajustes. Ya no se usa para actualizar."; }
        }
        History = Read<List<Dictation>>("history.json") ?? [];
        Vocabulary = PersonalVocabulary.Normalize(Read<List<string>>("vocabulary.json") ?? []);
        Statistics = new StatisticsStore(Root, History, Settings.TrackStatistics);
    }

    private T? Read<T>(string name)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
        catch (JsonException)
        {
            File.Move(path, path + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}", true);
            LoadWarning = "Se recuperó una configuración dañada. Conservamos una copia del archivo original.";
            return default;
        }
    }

    private void Write<T>(string name, T value)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
    public void SaveSettings() => Write("settings.json", Settings);
    public void Add(Dictation item)
    {
        if (Settings.TrackStatistics) Statistics.RecordDictation(item);
        if (!Settings.KeepHistory) return;
        History.Insert(0, item);
        if (History.Count > 200) History.RemoveRange(200, History.Count - 200);
        Write("history.json", History);
    }
    public void ClearHistory() { History.Clear(); Write("history.json", History); }
    public void SaveVocabulary(IEnumerable<string> terms, bool learned = false)
    {
        var normalized = PersonalVocabulary.Normalize(terms);
        var added = normalized.Except(Vocabulary, StringComparer.OrdinalIgnoreCase).Count();
        Write("vocabulary.json", normalized);
        Vocabulary = normalized;
        if (Settings.TrackStatistics) Statistics.RecordTerms(added, learned);
    }
    public int Correct(Dictation entry, string corrected)
    {
        var before = Vocabulary.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Settings.LearnVocabulary)
            SaveVocabulary(PersonalVocabulary.Learn(entry.Text, corrected).Concat(Vocabulary), learned: true);
        var index = History.IndexOf(entry);
        if (index >= 0)
        {
            History[index] = entry with { Text = corrected, OriginalText = entry.OriginalText ?? entry.Text };
            Write("history.json", History);
        }
        if (Settings.TrackStatistics) Statistics.RecordCorrection(entry.Text, corrected);
        return Vocabulary.Count(term => !before.Contains(term));
    }
}
