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

        // Sound matches (vocabulario fonético). Local replacement only for unknown words that sound exactly like a term
        // of five or six sounds, or one sound apart from a term of seven or more with the same first and last sound.
        string[] jargon = ["Nova-Hub = novahup", "Claude ~ cloud", "DeepSeek", "Obsidian", "homelab", "worktree", "worktrees", "vault", "backlog", "commit", "pipeline", "Codex", "PowerShell", "passkey", "Orca", "eli5", "UNA", ".NET", "Mercado Libre"];
        check(PersonalVocabulary.IsKnownWord("baúl") && PersonalVocabulary.IsKnownWord("Baul") && PersonalVocabulary.IsKnownWord("couldn't") && PersonalVocabulary.IsKnownWord("pilot")
            && !PersonalVocabulary.IsKnownWord("homelapse") && !PersonalVocabulary.IsKnownWord("Dipsick"), "Known words are recognized with or without accents and English clitics");
        check(PersonalVocabulary.IsKnownWord("cantárselas") && PersonalVocabulary.IsKnownWord("cafecitos"), "Enclitics and diminutives of listed words are real words");
        check(PersonalVocabulary.SoundKey("DeepSeek") == PersonalVocabulary.SoundKey("Dipsick") && PersonalVocabulary.SoundKey("Nova-Hub") == PersonalVocabulary.SoundKey("novahoop"), "Spanish and English spellings of one sound share a key");
        foreach (var (heard, fixedText) in new[]
        {
            ("Reinicia el homelap esta tarde", "Reinicia el homelab esta tarde"),
            ("Homelap apagado desde ayer", "Homelab apagado desde ayer"),
            ("le pido a Dipsick que resuma", "le pido a DeepSeek que resuma"),
            ("revisa Novahoop mañana", "revisa Nova-Hub mañana"),
            ("haz un komit y abre la paskey", "haz un commit y abre la passkey"),
            ("guardo notas en Obsidyan", "guardo notas en Obsidian"),
            ("abre Powerchel y el Pipelaine", "abre PowerShell y el pipeline"),
        })
        {
            var result = PersonalVocabulary.Apply(heard, jargon, out var n);
            check(result == fixedText && n >= 1, $"Unknown sound-alike becomes the term: {heard} -> {result}");
            check(PersonalVocabulary.Apply(result, jargon, out var again2) == result && again2 == 0, "Sound replacement is idempotent: " + result);
        }
        foreach (var correct in new[]
        {
            // Real Spanish and English words that sound like a term stay as dictated (independent review, round 1).
            "Lo guardé en el baúl, el baul y la bóveda. The pilot hit a frag, a flack and a bubble; pass the piloto.",
            "Subí fotos al cloud y a la Cloud pública. Un homenaje a la obsidiana con un vaso de horchata.",
            "coders, codearse, postcode, optician, gomita, glande, besugo, ponérsela, bebérselo, obstina, portly, backlot, basked, cowhide.",
            // Inflections and Spanglish verbs that start with a term, other terms and protected spellings.
            "Revisa los backlogs, commitear y commiteado, los worktrees, el homelab y Obsidian.",
            // Symbols, paths, handles, files, numbers, acronyms, phrases and line breaks.
            "Usa .NET, abre homelap.txt, C:\\homelap\\x, @homelap, #Opsidian, homelap_2 y homelap2. El 20-26 de junio, 3,5 %, HOMELAP y UNA reunión.",
            "Mercado Libre y el mercado libre. home\nlap, Dip\nsick.",
            // Ordinary dictation in both languages.
            "Mañana a las 9:30 revisamos el presupuesto con Juan, después enviamos el documento. The build broke twice and the chart looks wrong since Friday.",
        })
            check(PersonalVocabulary.Apply(correct, jargon, out var none) == correct && none == 0, "Correct text and real words are never rewritten by sound: " + PersonalVocabulary.Apply(correct, jargon));
        // Threshold rules, one probe each: every unknown word below is only a hint, never a replacement.
        foreach (var (heard, term, distance, rule) in new[]
        {
            ("homelop", "homelab", 1, "terms under seven sounds need the same sounds"),
            ("Obsetien", "Obsidian", 2, "longer terms allow one sound edit locally and two only as a hint"),
            ("Homelapse", "homelab", 1, "the last sound must agree"),
            ("Obsidiam", "Obsidian", 1, "the last sound must agree"),
            ("Oxidian", "Obsidian", 1, "a listed word (occitan) sounds as close"),
            ("Wordtree", "worktree", 1, "a listed word (poetry) sounds as close"),
        })
            check(PersonalVocabulary.SoundMatches(heard, jargon) is [{ Local: false } m] && m.Term == term && m.Distance == distance && PersonalVocabulary.Apply(heard, jargon) == heard, $"Sound threshold: {heard} stays a hint because {rule}");
        check(!PersonalVocabulary.SoundMatches("Bipsick y Tomelab", jargon).Any(), "The first sound must agree, even for hints");
        var hints = PersonalVocabulary.SoundHints("puse la llave en el baúl y la nota en Obsidian, Cloud lo sabe", jargon);
        check(hints.TryGetValue("vault", out var vaultHints) && vaultHints.SequenceEqual(["baúl"]) && !hints.ContainsKey("Claude"), "Real-word sound-alikes are only AI hints, never for listed '~' forms: " + string.Join(" | ", hints.Select(h => h.Key + "=" + string.Join(",", h.Value))));
        check(PersonalVocabulary.Apply("pregunta a Cloude", jargon) == "pregunta a Cloude" && PersonalVocabulary.SoundHints("pregunta a Cloude", jargon)["Claude"].SequenceEqual(["cloude"]), "A word that sounds like a '~' form stays a hint, never a replacement");
        check(PersonalVocabulary.Apply("abre el Workctree", jargon) == "abre el worktree" && PersonalVocabulary.Apply("abre el Workctree", ["worktree ~ work three"]) == "abre el Workctree", "A long term whose '~' form sounds the same is never replaced by sound");
        check(PersonalVocabulary.SoundMatches("Dip sick lo limpia", jargon) is [{ Term: "DeepSeek", Local: false }], "A phrase of real words is a hint, not a replacement");
        check(!PersonalVocabulary.SoundMatches("el orka y el eli", jargon).Any(), "Short terms and terms with digits are never sound targets");
        check(!PersonalVocabulary.SoundMatches("abre Dipsik", ["DeepSeek", "Deepsick"]).Any(), "Ties between two terms are not guessed");
        // Without the word list (missing or damaged resource) nothing is replaced or hinted by sound, and nothing throws.
        var source = PersonalVocabulary.KnownWordsSource;
        try
        {
            foreach (var broken in new Func<Stream?>[] { () => null, () => new MemoryStream([1, 2, 3, 4]) })
            {
                PersonalVocabulary.KnownWordsSource = broken;
                PersonalVocabulary.ResetKnownWords();
                check(PersonalVocabulary.Apply("Reinicia el homelap y novahup", jargon) == "Reinicia el homelap y Nova-Hub" && PersonalVocabulary.SoundHints("en el baúl", jargon).Count == 0,
                    "Without the known-words list sound matching is off and '=' forms still apply");
            }
        }
        finally
        {
            PersonalVocabulary.KnownWordsSource = source;
            PersonalVocabulary.ResetKnownWords();
        }
        check(PersonalVocabulary.Apply("Reinicia el homelap", jargon) == "Reinicia el homelab", "Sound matching comes back with the list");

        var prompt = PersonalVocabulary.Prompt(vocabulary);
        check(prompt.StartsWith("Glosario: ") && prompt.Contains("Nova-Hub") && prompt.Contains("Claude") && !prompt.Contains("novahup") && !prompt.Contains("cloud"), "Whisper receives only correctly spelled terms: " + prompt);
    }
}

/// <summary>
/// Counts term forms in a local history before and after Apply, and how many rewrites and AI hints come from sound
/// matches. Prints counts only, never dictated text; --show-forms also prints each sound match as "heard -> term"
/// (single words or short phrases, for a manual false-positive review on the user's own machine).
/// </summary>
static class VocabularyEval
{
    public static void Run(string historyPath, string vocabularyPath, bool showForms = false)
    {
        var history = System.Text.Json.JsonSerializer.Deserialize<List<Dictation>>(File.ReadAllText(historyPath)) ?? [];
        var vocabulary = VoiceEval.ReadVocabulary(vocabularyPath);
        // Probes come from the vocabulary itself: every term (exact case) and every heard form (any case).
        var probes = PersonalVocabulary.Entries(vocabulary).SelectMany(e => e.HeardAs.Prepend(e.Term)).Distinct().ToArray();
        var before = new Dictionary<string, int>(); var after = new Dictionary<string, int>();
        var forms = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int replaced = 0, sounds = 0, hints = 0, changedDictations = 0;
        foreach (var item in history)
        {
            var raw = item.OriginalText ?? item.Text;
            var fixedText = PersonalVocabulary.Apply(raw, vocabulary, out var count); replaced += count;
            // Sound matches are decided on the text after the "=" forms and spelling variants, as Apply does.
            var withoutSounds = PersonalVocabulary.Apply(raw, vocabulary, out _, sounds: false);
            var matches = PersonalVocabulary.SoundMatches(withoutSounds, vocabulary);
            sounds += matches.Count(m => m.Local); hints += matches.Count(m => !m.Local);
            if (matches.Any(m => m.Local)) changedDictations++;
            foreach (var m in matches) { var k = $"{m.Heard} -> {m.Term} ({(m.Local ? "local" : "hint")}, d={m.Distance})"; forms[k] = forms.GetValueOrDefault(k) + 1; }
            foreach (var probe in probes)
            {
                before[probe] = before.GetValueOrDefault(probe) + VoiceEval.Occurrences(raw, probe);
                after[probe] = after.GetValueOrDefault(probe) + VoiceEval.Occurrences(fixedText, probe);
            }
        }
        Console.WriteLine($"dictations={history.Count} replacements={replaced} formReplacements={replaced - sounds} soundReplacements={sounds} dictationsWithSoundReplacements={changedDictations} soundHints={hints}");
        foreach (var probe in probes) Console.WriteLine($"{probe}: {before[probe]} -> {after[probe]}");
        if (showForms) foreach (var (form, n) in forms) Console.WriteLine($"sound {form} x{n}");
    }
}
