using System.IO;
using Notaker;

static class VocabularyChecks
{
    public static void Run(Action<bool, string> check)
    {
        string[] vocabulary = ["Nova-Hub-Alerts = novahubalerts, novahuballers", "Nova-Hub = novahup, nova up, novaub", "Claude ~ cloud", "DeepSeek", "worktree"];

        // Real failure from daily use: "novahup" instead of "Nova-Hub".
        check(PersonalVocabulary.Apply("Abre el repo de novahup y luego Novahup Alerts.", vocabulary) == "Abre el repo de Nova-Hub y luego Nova-Hub Alerts.", "Always-replaced heard forms become the exact term, any case");
        check(PersonalVocabulary.Apply("nova hub, novahub y NOVA-HUB", vocabulary) == "Nova-Hub, Nova-Hub y Nova-Hub", "Spacing, hyphen and case variants of a distinctive term are normalized");
        check(PersonalVocabulary.Apply("revisa novahuballers hoy", vocabulary) == "revisa Nova-Hub-Alerts hoy", "Longest form wins over a shorter term inside it");
        check(PersonalVocabulary.Apply("Nova-Hub-Alerts ya está", vocabulary) == "Nova-Hub-Alerts ya está", "Already correct longer term is not cut down");
        check(PersonalVocabulary.Apply("Lo subimos al cloud de Claude", vocabulary) == "Lo subimos al cloud de Claude", "Context-only forms are never replaced locally (cloud stays cloud)");
        check(PersonalVocabulary.Apply("Mi novahupgrade y supernovahup", vocabulary) == "Mi novahupgrade y supernovahup", "Replacement is whole-word only");
        check(PersonalVocabulary.Apply("deepseek y Deepseek", vocabulary) == "DeepSeek y DeepSeek", "Inner-capital terms recover their spelling");
        check(PersonalVocabulary.Apply("Worktree nuevo", vocabulary) == "Worktree nuevo", "Plain lowercase terms do not rewrite sentence-case words");
        PersonalVocabulary.Apply("novahup y nova up", vocabulary, out var count);
        check(count == 2, "Replacement count is reported for structural logs");

        var cleaned = PersonalVocabulary.Normalize(["novaub", "novahub", "Nova-Hub = novahup", "NovaHub = novaub", "app", "App", "  "]);
        check(cleaned.SequenceEqual(["Nova-Hub = novahup, novaub", "app"]), "Normalization merges spelling variants, prefers the written form and drops bare mis-hearings: " + string.Join(" | ", cleaned));
        check(PersonalVocabulary.Normalize(["DeepSeek", "Deepseek", "deepseek"]).SequenceEqual(["DeepSeek"]), "First written spelling of a term wins");
        check(PersonalVocabulary.Normalize(["Claude ~ cloud", "Claude = cloud"]).SequenceEqual(["Claude = cloud"]), "An explicit always form overrides a context-only one");
        check(PersonalVocabulary.Normalize(["Claude = clod", "Claude ~ clod, cloud"]).SequenceEqual(["Claude = clod ~ cloud"]), "Learned context forms never upgrade to always-replaced");
        check(PersonalVocabulary.Parse("Nova-Hub = novahup ~ nova of").ToString() == "Nova-Hub = novahup ~ nova of", "Mixed always and context forms round-trip");

        check(PersonalVocabulary.Learn("Abre novahup ahora", "Abre Nova-Hub ahora").SequenceEqual(["Nova-Hub ~ novahup"]), "Corrections teach the heard form with the corrected spelling");
        check(PersonalVocabulary.Learn("Pregúntale a cloud", "Pregúntale a Claude").SequenceEqual(["Claude ~ cloud"]), "A real-word mis-hearing is learned as context-only");
        check(!PersonalVocabulary.Learn("Abre el panel", "Abre el panel del usuario y digamos la app").Any(), "Added text without a replacement does not pollute the vocabulary");

        var prompt = PersonalVocabulary.Prompt(vocabulary);
        check(prompt.StartsWith("Glosario: ") && prompt.Contains("Nova-Hub") && prompt.Contains("Claude") && !prompt.Contains("novahup") && !prompt.Contains("cloud"), "Whisper receives only correctly spelled terms: " + prompt);
    }
}

/// <summary>Counts term forms in a local history before and after Apply. Prints counts only, never dictated text.</summary>
static class VocabularyEval
{
    public static void Run(string historyPath, string vocabularyPath)
    {
        var history = System.Text.Json.JsonSerializer.Deserialize<List<Dictation>>(File.ReadAllText(historyPath)) ?? [];
        var vocabulary = File.ReadAllLines(vocabularyPath);
        // Probes come from the vocabulary itself: every term (exact case) and every heard form (any case).
        var probes = PersonalVocabulary.Entries(vocabulary).SelectMany(e => e.HeardAs.Prepend(e.Term)).Distinct().ToArray();
        var before = new Dictionary<string, int>(); var after = new Dictionary<string, int>();
        var replaced = 0;
        foreach (var item in history)
        {
            var raw = item.OriginalText ?? item.Text;
            var fixedText = PersonalVocabulary.Apply(raw, vocabulary, out var count); replaced += count;
            foreach (var probe in probes)
            {
                var pattern = @"(?<![\p{L}\p{N}])" + System.Text.RegularExpressions.Regex.Escape(probe) + @"(?![\p{L}\p{N}])";
                var cs = probe.Any(char.IsUpper) ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                before[probe] = before.GetValueOrDefault(probe) + System.Text.RegularExpressions.Regex.Matches(raw, pattern, cs).Count;
                after[probe] = after.GetValueOrDefault(probe) + System.Text.RegularExpressions.Regex.Matches(fixedText, pattern, cs).Count;
            }
        }
        Console.WriteLine($"dictations={history.Count} replacements={replaced}");
        foreach (var probe in probes) Console.WriteLine($"{probe}: {before[probe]} -> {after[probe]}");
    }
}
