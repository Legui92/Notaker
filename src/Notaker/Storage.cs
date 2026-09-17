using System.IO;
using System.Text.Json;

namespace Notaker;

public sealed record Dictation(string Text, DateTime CreatedAt, double Seconds)
{
    public string Caption => $"{CreatedAt:dd MMM · HH:mm}   /   {Seconds:0} s";
    public string? OriginalText { get; init; }
}

public sealed class Preferences
{
    public string Language { get; set; } = "auto";
    public string Model { get; set; } = "base";
    public int Hotkey { get; set; }
    public DictationShortcut? CustomHotkey { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DictationShortcut Shortcut => CustomHotkey is { IsValid: true } ? CustomHotkey : DictationShortcut.FromLegacy(Hotkey);
    public int Microphone { get; set; } = -1;
    public bool KeepHistory { get; set; } = true;
    public bool AutoPaste { get; set; } = true;
    public bool CleanWithAi { get; set; }
    public bool LearnVocabulary { get; set; } = true;
    public string ApiModel { get; set; } = "deepseek-flash";
    public string ProtectedApiKey { get; set; } = "";
}

public sealed class Storage
{
    public string Root { get; }
    public string ModelPath => Path.Combine(Root, "models", $"ggml-{Settings.Model}.bin");
    public Preferences Settings { get; }
    public List<Dictation> History { get; }
    public List<string> Vocabulary { get; private set; }
    public string? LoadWarning { get; private set; }

    public Storage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notaker");
        Directory.CreateDirectory(Path.Combine(Root, "models"));
        Settings = Read<Preferences>("settings.json") ?? new();
        if (Settings.Language is not ("auto" or "es" or "en")) Settings.Language = "auto";
        if (Settings.Model is not ("base" or "small")) Settings.Model = "base";
        Settings.Hotkey = Math.Clamp(Settings.Hotkey, 0, 2);
        History = Read<List<Dictation>>("history.json") ?? [];
        Vocabulary = PersonalVocabulary.Normalize(Read<List<string>>("vocabulary.json") ?? []);
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
        if (!Settings.KeepHistory) return;
        History.Insert(0, item);
        if (History.Count > 200) History.RemoveRange(200, History.Count - 200);
        Write("history.json", History);
    }
    public void ClearHistory() { History.Clear(); Write("history.json", History); }
    public void SaveVocabulary(IEnumerable<string> terms)
    {
        var normalized = PersonalVocabulary.Normalize(terms);
        Write("vocabulary.json", normalized);
        Vocabulary = normalized;
    }
    public int Correct(Dictation entry, string corrected)
    {
        var before = Vocabulary.Count;
        if (Settings.LearnVocabulary)
            SaveVocabulary(PersonalVocabulary.Learn(entry.Text, corrected).Concat(Vocabulary));
        var index = History.IndexOf(entry);
        if (index >= 0)
        {
            History[index] = entry with { Text = corrected, OriginalText = entry.OriginalText ?? entry.Text };
            Write("history.json", History);
        }
        return Math.Max(0, Vocabulary.Count - before);
    }
}
