using System.IO;
using System.Text.Json;
using Notaker;

internal static class StatisticsChecks
{
    internal static void Run(string root)
    {
        void Check(bool value, string message) { if (!value) throw new Exception("Statistics: " + message); Console.WriteLine("PASS: " + message); }
        Check(TextMetrics.WordCount("Mañana reviso el pull request, versión 2.") == 7, "Word counting handles accents, English and numbers");
        Check(TextMetrics.WordEdits("Hola, TEAM!", "hola team.") == 0, "Punctuation and case are not word corrections");
        Check(TextMetrics.WordEdits("reunio\u0301n don't", "reunión don’t") == 0, "Unicode normalization and apostrophes avoid false corrections");
        Check(TextMetrics.WordEdits("eh enviar a Luisa", "enviar a Luzia mañana") == 3, "Word edits distinguish removal, replacement and insertion");
        Check(TextMetrics.WordEdits("", "uno dos") == 2 && TextMetrics.WordEdits("uno dos", "") == 2, "Word edits handle empty input on either side");
        var store = new Storage(Path.Combine(root, "stats"));
        store.Settings.KeepHistory = false;
        var first = new Dictation("Hola team.", DateTime.Now, 30) { OriginalText = "eh hola team", AiWordEdits = 1 };
        store.Add(first);
        store.Add(new Dictation("one two three four", DateTime.Now, 90));
        var data = store.Statistics.Data;
        Check(data.Dictations == 2 && data.Words == 7 && data.SpeakingSeconds == 120 && data.WordsPerMinute == 3.5, "Usage and weighted WPM use raw words and recording duration");
        Check(data.AiEditedDictations == 1 && data.AiWordEdits == 1, "Only successful AI edits contribute to AI counters");
        Check(!File.Exists(Path.Combine(store.Root, "history.json")) && new Storage(store.Root).Statistics.Data.Words == 7, "Statistics persist independently when history is disabled");
        var json = File.ReadAllText(Path.Combine(store.Root, "statistics.json"));
        Check(!json.Contains("Hola") && !json.Contains("team") && !json.Contains("OriginalText"), "Statistics file contains counters rather than dictated text");
        store.ClearHistory();
        Check(store.Statistics.Data.Words == 7, "Clearing history preserves accumulated usage");
        store.SaveVocabulary(["Notaker", "notaker", "Acme Labs"]);
        store.SaveVocabulary(["Notaker", "Acme Labs"]);
        Check(store.Statistics.Data.AddedTerms == 2, "Manual dictionary additions count unique new terms only");
        var learned = store.Correct(first, "Hola equipo.");
        Check(learned == 1 && store.Statistics.Data.LearnedTerms == 1 && store.Statistics.Data.ManualWordEdits == 1 && store.Statistics.Data.ManualCorrections == 1, "Manual corrections and learned terms are separate counters");
        store.Correct(first with { Text = "Hola equipo." }, "Hola equipo.");
        Check(store.Statistics.Data.ManualCorrections == 1, "Saving unchanged correction does not inflate counters");
        store.Correct(first with { Text = "Hola equipo." }, "Hola equipo!");
        Check(store.Statistics.Data.ManualCorrections == 2 && store.Statistics.Data.ManualWordEdits == 1, "Punctuation-only manual save counts an event but no word edits");
        store.Add(new Dictation("Hola.", DateTime.Now, 2) { OriginalText = "Hola", AiWordEdits = 0 });
        Check(store.Statistics.Data.AiEditedDictations == 2 && store.Statistics.Data.AiWordEdits == 1, "Punctuation-only AI change counts a retouched dictation");
        var before = store.Statistics.Data;
        store.Settings.TrackStatistics = false; store.SaveSettings();
        store.Add(first); store.SaveVocabulary(["AnotherTerm"]); store.Correct(first, "Hola QwertyCorporation.");
        Check(store.Statistics.Data == before && !new Storage(store.Root).Settings.TrackStatistics, "Statistics opt-out stops all counters and persists");
        store.Settings.TrackStatistics = true;
        store.SaveVocabulary(Enumerable.Range(0, 100).Select(i => "Term" + i));
        var learnedBefore = store.Statistics.Data.LearnedTerms;
        var learnedAtCapacity = store.Correct(first, "Hola NewUniqueName.");
        Check(learnedAtCapacity == 1 && store.Vocabulary.Count == 100 && store.Statistics.Data.LearnedTerms == learnedBefore + 1, "Learning is counted even when the 100-term dictionary replaces an old entry");
        var cappedHistory = new Storage(Path.Combine(root, "stats-beyond-history"));
        for (var i = 0; i < 205; i++) cappedHistory.Add(new Dictation("uno dos", DateTime.Now, 1));
        Check(cappedHistory.History.Count == 200 && new Storage(cappedHistory.Root).Statistics.Data.Dictations == 205, "Accumulated usage outlives the 200-dictation history limit");
        var legacy = Path.Combine(root, "legacy-stats"); Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "history.json"), JsonSerializer.Serialize(new[] { first, new Dictation("tres palabras aquí", DateTime.Now, 30) }));
        var migrated = new Storage(legacy);
        Check(migrated.Statistics.Data.ImportedDictations == 2 && migrated.Statistics.Data.Words == 6 && migrated.Statistics.Data.AiWordEdits == 0, "Legacy history imports usage without inventing correction provenance");
        Check(new Storage(legacy).Statistics.Data.Dictations == 2, "Legacy usage is imported only once");
        Check(migrated.Statistics.Reset() && new Storage(legacy).Statistics.Data.Dictations == 0 && new Storage(legacy).History.Count == 2, "Reset preserves history and prevents re-import on restart");
        File.WriteAllText(Path.Combine(legacy, "statistics.json"), "{broken");
        var recovered = new Storage(legacy);
        Check(recovered.Statistics.Warning != null && recovered.Statistics.Data.Dictations == 0 && Directory.GetFiles(legacy, "statistics.json.corrupt-*").Length == 1, "Corrupt counters are backed up and not confused with first-time migration");
        Directory.CreateDirectory(Path.Combine(legacy, "statistics.json.tmp"));
        recovered.Add(first);
        Check(recovered.Statistics.Warning != null && recovered.History.Count == 3, "Statistics write failure does not discard a successful dictation");
    }
}
