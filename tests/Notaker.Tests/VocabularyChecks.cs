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

        // Independent review, round 1: ordinary entries must never rewrite correct text.
        check(PersonalVocabulary.Apply("Usa .NET y ASP.NET Core, el net income", [".NET"]) == "Usa .NET y ASP.NET Core, el net income", "Symbol-led terms are hints only, never duplicated");
        check(PersonalVocabulary.Apply("ping @Mention and mention", ["@Mention"]) == "ping @Mention and mention", "Symbol-led handles are not rewritten");
        check(PersonalVocabulary.Apply("del 20-26 de junio, cuesta 20.26", ["2026"]) == "del 20-26 de junio, cuesta 20.26", "Numbers are never rewritten by numeric terms");
        check(PersonalVocabulary.Apply("de 3 a 4 días y 5 g de sal", ["A4", "5G"]) == "de 3 a 4 días y 5 g de sal", "Alphanumeric terms do not absorb numbers and units");
        check(PersonalVocabulary.Apply("Hay una reunión. Is it ready? el mercado libre", ["UNA", "IT", "Mercado Libre"]) == "Hay una reunión. Is it ready? el mercado libre", "Acronyms and spaced phrases that are real words stay untouched");
        check(PersonalVocabulary.Apply("abre nova\nhub y novahup\nya", ["Nova-Hub = novahup"]) == "abre nova\nhub y Nova-Hub\nya", "Replacements never cross line breaks");
        var twice = PersonalVocabulary.Apply(PersonalVocabulary.Apply("novahup y Nova-Hub-Alerts", vocabulary), vocabulary, out var again);
        check(twice == "Nova-Hub y Nova-Hub-Alerts" && again == 0, "Applying twice (before and after AI) is idempotent");
        check(!PersonalVocabulary.Learn("pagamos 1500 euros en 2025", "pagamos 1600 euros en 2026").Any(), "Corrected numbers are not learned");
        var grammar = PersonalVocabulary.Learn("fue a la casa y el perro ladra", "fui a la casa y la perra ladra");
        check(!grammar.Any(), "Grammar fixes between ordinary words are not learned: " + string.Join(" | ", grammar));
        check(!PersonalVocabulary.Learn("vamos a hacer esto mañana temprano por favor", "necesitamos preparar el informe trimestral del equipo comercial").Any(), "Long rewrites are not learned word by word");
        check(!PersonalVocabulary.Learn("fue a la casa", "Fui a la casa").Any(), "A capital at the start of a sentence is not a name");
        check(PersonalVocabulary.Learn("abre novahub ahora", "abre Nova-Hub ahora").SequenceEqual(["Nova-Hub"]), "A spelling-only correction learns the whole written term");
        check(PersonalVocabulary.Learn("abre el work tree", "abre el worktree").SequenceEqual(["worktree"]), "Joined words are learned as the term");
        var kept = PersonalVocabulary.Normalize(["E=mc2", "Pi = 3.14", "Claude ~ cloud", "Cloud"]);
        check(kept.SequenceEqual(["E=mc2", "Pi = 3.14", "Claude ~ cloud", "Cloud"]), "Old entries with symbols and real words survive normalization: " + string.Join(" | ", kept));
        check(PersonalVocabulary.Apply("pi vale 3.14", kept) == "pi vale 3.14", "Legacy entries with '=' and no spoken form are not rules");

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
