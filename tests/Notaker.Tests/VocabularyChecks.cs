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

        // Debt from the independent reviews of PR 15 (N1-N6) and PR 18 (J18-3).
        string[] hyphens = ["to-do", "A-Team", "Log-In", "Nova-Hub"];
        check(PersonalVocabulary.Apply("what to do now, the a team, log in and login; TO-DO, a-team, LOG-IN", hyphens)
            == "what to do now, the a team, log in and login; to-do, A-Team, Log-In", "N1: hyphenated terms with a short or common part only fix the case of the hyphenated form");
        check(PersonalVocabulary.Apply("nova hub y novahub", hyphens) == "Nova-Hub y Nova-Hub", "N1: distinctive hyphenated terms keep their spaced and joined variants");
        var tight = PersonalVocabulary.Parse("X-Hub=xhup");
        check(tight.Term == "X-Hub" && tight.Always.SequenceEqual(["xhup"]), "N2: '=' without spaces between a written term and a spoken word is a rule: " + tight);
        check(PersonalVocabulary.Parse("Nova-Hub=novahup~nova of").ToString() == "Nova-Hub = novahup ~ nova of", "N2: tight '=' and '~' round-trip");
        var tightPrompt = PersonalVocabulary.Prompt(["X-Hub=xhup", "E=mc2"]);
        check(tightPrompt == "Glosario: X-Hub, E=mc2." && PersonalVocabulary.Apply("abre xhup", ["X-Hub=xhup"]) == "abre X-Hub", "N2: the heard form never reaches the Whisper prompt: " + tightPrompt);
        check(PersonalVocabulary.Normalize(["E=mc2", "a=b", "Pi=3.14", "C++=c plus"]).SequenceEqual(["E=mc2", "a=b", "Pi=3.14", "C++=c plus"]), "N2: formulas and numbers without spaces stay plain terms");
        check(PersonalVocabulary.Normalize(["E = mc2", "F = ma"]).SequenceEqual(["E = mc2", "F = ma"]) && PersonalVocabulary.Apply("la mc2 y el ma", ["E = mc2", "F = ma"]) == "la mc2 y el ma",
            "N4: a single letter is never a replacement rule");
        check(PersonalVocabulary.Apply("abre nova hub pro y novahub", ["Foo = novahub", "Nova-Hub-Pro"]) == "abre Nova-Hub-Pro y Foo", "N5: the longest form by letters wins across heard forms and spelling variants");
        check(PersonalVocabulary.Learn("pregunta a claude y a deepseek", "pregunta a Claude y a DeepSeek").SequenceEqual(["Claude"]), "N6: a change of case inside a sentence learns the term, never one with spelling variants");
        var latex = PersonalVocabulary.Learn("guantes de latex y escribo en latex", "guantes de latex y escribo en LaTeX");
        check(!latex.Any() && PersonalVocabulary.Apply("guantes de latex", latex) == "guantes de latex", "J19-3: a case-only change never creates a rewrite of the lowercase word: " + string.Join(" | ", latex));
        check(PersonalVocabulary.Learn("le escribo a mercado libre", "le escribo a Mercado Libre").SequenceEqual(["Mercado Libre"]), "N6: a multi-word change of case learns the whole phrase");
        check(!PersonalVocabulary.Learn("claude dice que sí. luego vamos", "Claude dice que sí. Luego vamos").Any() && !PersonalVocabulary.Learn("pregunta a Claude", "pregunta a claude").Any()
            && !PersonalVocabulary.Learn("abre el panel", "abre el panel de DeepSeek").Any(), "N6: sentence starts, lowercasing and added names are not learned");
        check(PersonalVocabulary.Learn("pregunta a cloud y a mercado libre", "pregunta a Claude y a Mercado Libre").SequenceEqual(["Claude ~ cloud", "Mercado Libre"]), "N6: replacements and case changes in one correction");
        foreach (var (raw, fixedList) in new[]
        {
            ("llamar a juan\ncomprar pan", "Llamar a juan\nComprar pan"),
            ("- llamar a juan\n- comprar pan", "- Llamar a juan\n- Comprar pan"),
            ("1. llamar a juan\n2) comprar pan\n• enviar fotos", "1. Llamar a juan\n2) Comprar pan\n• Enviar fotos"),
            ("notas:\r\ncomprar pan", "notas:\r\nComprar pan"),
        })
            check(!PersonalVocabulary.Learn(raw, fixedList).Any(), "J19-2: capitals at the start of a line or list item are not learned: " + fixedList.Replace("\n", " / "));
        check(PersonalVocabulary.Learn("hola\nescribe a claude", "hola\nescribe a Claude").SequenceEqual(["Claude"]), "J19-2: a case change inside a line after a line break is still learned");
        check(PersonalVocabulary.Learn("tarea - pregunta a cloud", "tarea - pregunta a Claude").SequenceEqual(["Claude ~ cloud"]), "J19-2: a dash inside a sentence is not a list marker");
        // J19-1: symbolic terms of more than one character keep their rules.
        string[] symbols = ["C# = c sharp", "F#=fsharp"];
        check(PersonalVocabulary.Apply("uso c sharp y fsharp", symbols) == "uso C# y F#" && PersonalVocabulary.Apply("uso c plus plus", ["C++ = c plus plus"]) == "uso C++", "J19-1: C#, F# and C++ keep their heard forms");
        var symbolPrompt = PersonalVocabulary.Prompt(symbols);
        check(symbolPrompt == "Glosario: C#, F#." && PersonalVocabulary.Prompt(["C++ = c plus plus"]) == "Glosario: C++.", "J19-1: heard forms of symbolic terms stay out of the Whisper prompt: " + symbolPrompt);
        // Debt from the independent review of PR 19 (J19-4, J19-5, J19-7, J19-8, J19-9).
        var both = PersonalVocabulary.Normalize(["C# = c sharp", "C++ = c plus plus", "c#"]);
        check(both.SequenceEqual(["C# = c sharp", "C++ = c plus plus"]) && PersonalVocabulary.Apply("uso c sharp y c plus plus", both) == "uso C# y C++",
            "J19-8: C# and C++ share the letter key but stay two entries: " + string.Join(" | ", both));
        var vocabularyRoot = Path.Combine(Path.GetTempPath(), "notaker-vocabulary-" + Guid.NewGuid().ToString("N"));
        try
        {
            new Storage(vocabularyRoot).SaveVocabulary(["C# = c sharp", "C++ = c plus plus"]);
            var reloaded = new Storage(vocabularyRoot).Vocabulary;
            check(reloaded.SequenceEqual(["C# = c sharp", "C++ = c plus plus"]), "J19-8: the C++ line survives saving and loading: " + string.Join(" | ", reloaded));
        }
        finally { try { Directory.Delete(vocabularyRoot, true); } catch (IOException) { } }
        check(PersonalVocabulary.Normalize(["Nova-Hub", "nova hub", "NovaHub = novaub"]).SequenceEqual(["Nova-Hub = novaub"]), "J19-8: terms with letters still merge by their letters");
        string[] compounds = ["Read-Only", "Know-How", "Check-Out", "Nova-Hub"];
        check(PersonalVocabulary.Apply("the file is read only, I know how to do it, git checkout main; READ-ONLY and know-HOW", compounds)
            == "the file is read only, I know how to do it, git checkout main; Read-Only and Know-How", "J19-4: everyday compounds only fix the case of the hyphenated form");
        check(PersonalVocabulary.Apply("abre nova hub y novahub", compounds) == "abre Nova-Hub y Nova-Hub", "J19-4: distinctive hyphenated names keep their spaced and joined variants");
        var formulas = PersonalVocabulary.Normalize(["H2O=agua", "NaCl=sal", "CO2=gas"]);
        check(formulas.SequenceEqual(["H2O=agua", "NaCl=sal", "CO2=gas"]) && PersonalVocabulary.Apply("bebe agua con sal y gas", formulas) == "bebe agua con sal y gas",
            "J19-5: chemical formulas written without spaces are plain terms: " + string.Join(" | ", formulas));
        check(PersonalVocabulary.Parse("GitHub=gitjab").Always.SequenceEqual(["gitjab"]) && PersonalVocabulary.Parse("PR=pe").Always.SequenceEqual(["pe"])
            && PersonalVocabulary.Apply("bebe agua", ["H2O = agua"]) == "bebe H2O", "J19-5: names, acronyms and formulas with spaces keep their rules");
        string[] digitAcronyms = ["B2B=bitubi", "P2P=pitupi", "V8=veocho", "W3C=wtresc"];
        check(PersonalVocabulary.Apply("modelo bitubi y pitupi, motor veocho, norma wtresc", digitAcronyms) == "modelo B2B y P2P, motor V8, norma W3C",
            "J20-1: acronyms with digits made of element letters keep tight rules spoken with their first letter");
        check(PersonalVocabulary.Normalize(["a+=bb", "x#=yy"]).SequenceEqual(["a+=bb", "x#=yy"]) && PersonalVocabulary.Apply("bb y yy", ["a+=bb", "x#=yy"]) == "bb y yy",
            "J19-9: compound assignments are code, not rules");
        check(PersonalVocabulary.Parse("C#=csharp").Always.SequenceEqual(["csharp"]) && PersonalVocabulary.Parse("C++=cplusplus").Always.SequenceEqual(["cplusplus"])
            && PersonalVocabulary.Parse("Notepad++=notepadplusplus").Always.SequenceEqual(["notepadplusplus"]), "J19-9: symbol terms keep tight rules spoken with their first letter");
        foreach (var (raw, fixedText) in new[]
        {
            ("dijo: «luego vamos»", "dijo: «Luego vamos»"),
            ("«… luego vamos»", "«… Luego vamos»"),
            ("lo pensé… luego vamos", "lo pensé… Luego vamos"),
            ("—luego vamos —dijo", "—Luego vamos —dijo"),
            ("\"luego vamos\"", "\"Luego vamos\""),
            ("a) luego vamos\nb) comprar pan", "a) Luego vamos\nb) Comprar pan"),
        })
            check(!PersonalVocabulary.Learn(raw, fixedText).Any(), "J19-7: capitals after opening signs, ellipses or letter list markers are not learned: " + fixedText.Replace("\n", " / "));
        check(PersonalVocabulary.Learn("fui —con claude— al centro", "fui —con Claude— al centro").SequenceEqual(["Claude"])
            && PersonalVocabulary.Learn("dijo «claude» y se fue", "dijo «Claude» y se fue").SequenceEqual(["Claude"])
            && PersonalVocabulary.Learn("vitamina a y claude", "vitamina a y Claude").SequenceEqual(["Claude"]), "J19-7: a dash, quotes or a single letter inside a sentence are not a start");
        string[] github = ["GitHub", "Nova-Hub = novahup"];
        foreach (var address in new[] { "https://github.com/ana/x", "www.github.com", "ana@github.com", "C:\\novahup\\x", "github.com", "novahup.txt", "repo/novahup" })
            check(PersonalVocabulary.Apply("abre " + address + " ya", github, out var touched) == "abre " + address + " ya" && touched == 0, "J18-3: addresses, paths and files are not rewritten: " + address);
        check(PersonalVocabulary.Apply("sube a github y a novahup.", github) == "sube a GitHub y a Nova-Hub.", "J18-3: words at the end of a sentence are still replaced");

        // Sound hints (vocabulario fonético). Words that sound like a term are only hints for the AI editor: the local
        // text never changes by sound, only through "=" forms and spelling variants, exactly as in v0.11.1.
        string[] jargon = ["Nova-Hub = novahup", "Claude ~ cloud", "DeepSeek", "Obsidian", "homelab", "worktree", "worktrees", "vault", "backlog", "commit", "pipeline", "Codex", "PowerShell", "passkey", "Orca", "eli5", "UNA", ".NET", "Mercado Libre"];
        check(PersonalVocabulary.SoundKey("DeepSeek") == PersonalVocabulary.SoundKey("Dipsick") && PersonalVocabulary.SoundKey("Nova-Hub") == PersonalVocabulary.SoundKey("novahoop"), "Spanish and English spellings of one sound share a key");
        foreach (var (heard, term, form) in new[]
        {
            ("Reinicia el homelap esta tarde", "homelab", "homelap"),
            ("le pido a Dipsick que resuma", "DeepSeek", "dipsick"),
            ("revisa Novahoop mañana", "Nova-Hub", "novahoop"),
            ("haz un komit y abre la paskey", "passkey", "paskey"),
            ("guardo notas en Obsidyan", "Obsidian", "obsidyan"),
            ("abre Powerchel y el Pipelaine", "PowerShell", "powerchel"),
            ("Lo guardé en el baúl", "vault", "baúl"),
            ("Dip sick lo limpia", "DeepSeek", "dip sick"),
        })
        {
            var hinted = PersonalVocabulary.SoundHints(heard, jargon);
            check(hinted.TryGetValue(term, out var forms) && forms.Contains(form), $"Sound-alike is an AI hint: {form} -> {term}");
            check(PersonalVocabulary.Apply(heard, jargon, out var n) == heard && n == 0, "Sound-alikes are never replaced locally: " + heard);
        }
        // The fourteen real words the independent review saw rewritten (J18-1) and more: never changed locally.
        foreach (var correct in new[]
        {
            "coders, optician, gomita, glande, backlot, ponérsela, portly, basked, cowhide, codearse, postcode, besugo, bebérselo, obstina.",
            "Lo guardé en el baúl, el baul y la bóveda. The pilot hit a frag, a flack and a bubble; pass the piloto.",
            "Subí fotos al cloud y a la Cloud pública. Un homenaje a la obsidiana con un vaso de horchata.",
            "Reinicia el homelap, pregunta a Dipsick y a Cloude, abre Powerchel, Obsidyan, Novahoop y el Workctree, haz un komit.",
            "Revisa los backlogs, commitear y commiteado, los worktrees, el homelab y Obsidian.",
            "Usa .NET, abre homelap.txt, C:\\homelap\\x, @homelap, #Opsidian, homelap_2 y homelap2. El 20-26 de junio, 3,5 %, HOMELAP y UNA reunión.",
            "Mercado Libre y el mercado libre. home\nlap, Dip\nsick.",
            "Mañana a las 9:30 revisamos el presupuesto con Juan, después enviamos el documento. The build broke twice and the chart looks wrong since Friday.",
        })
        {
            var once = PersonalVocabulary.Apply(correct, jargon, out var none);
            check(once == correct && none == 0, "Text is never rewritten by sound: " + once);
            check(PersonalVocabulary.Apply(once, jargon) == once, "Apply stays idempotent");
        }
        check(PersonalVocabulary.Apply("Reinicia el homelap y novahup", jargon) == "Reinicia el homelap y Nova-Hub", "'=' forms still replace, sound-alikes do not");
        // What is never a hint.
        check(!PersonalVocabulary.SoundMatches("Bipsick y Tomelab", jargon).Any(), "The first sound must agree");
        check(!PersonalVocabulary.SoundMatches("el orka y el eli", jargon).Any(), "Short terms and terms with digits are never sound targets");
        check(!PersonalVocabulary.SoundMatches("abre Dipsik", ["DeepSeek", "Deepsick"]).Any(), "Ties between two terms are not guessed");
        check(!PersonalVocabulary.SoundMatches("Revisa los backlogs, commitear, el homelab, Obsidian, cloud y Cloud", jargon).Any(), "Terms, their listed forms and words that start with a term are not hints");
        check(!PersonalVocabulary.SoundMatches("homelap.txt C:\\homelap\\x @homelap #Opsidian homelap_2 homelap2 HOMELAP home\nlap", jargon).Any(), "Files, paths, handles, digits, acronyms and line breaks are not hints");
        check(PersonalVocabulary.SoundMatches("Obsidien", jargon) is [{ Term: "Obsidian", Distance: 1 }] && !PersonalVocabulary.SoundMatches("Obsidiano", jargon).Any() && PersonalVocabulary.SoundMatches("Obsetien", jargon) is [{ Distance: 2 }] && !PersonalVocabulary.SoundMatches("Opsetiem", jargon).Any(), "Long terms allow up to two sound edits; words that start with the term are not hints");
        check(PersonalVocabulary.SoundMatches("homelop", jargon) is [{ Distance: 1 }] && !PersonalVocabulary.SoundMatches("homelopes", jargon).Any(), "Short terms allow one sound edit");
        // Bounded and deduplicated: the prompt never grows by more than the limits.
        var flood = string.Join(" y ", Enumerable.Range(0, 6).Select(_ => "homelap Homelop homelep homelip homelup Dipsick Dipsik Obsidyan Obsidien Obsetien Powerchel Pipelaine paskey komitt"));
        var capped = PersonalVocabulary.SoundHints(flood, jargon);
        check(capped.Sum(h => h.Value.Count) == PersonalVocabulary.MaxSoundHints && capped.All(h => h.Value.Count <= PersonalVocabulary.MaxSoundHintsPerTerm)
            && capped.All(h => h.Value.Select(PersonalVocabulary.Key).Distinct().Count() == h.Value.Count) && capped.Values.All(f => f.All(flood.ToLowerInvariant().Contains)),
            "Hints are capped per term and per dictation, deduplicated and taken from the dictated text: " + string.Join(" | ", capped.Select(h => h.Key + "=" + string.Join(",", h.Value))));
        check(PersonalVocabulary.SoundHints("Homelap, homelap y HOMElap", jargon) is { Count: 1 } one && one["homelab"].SequenceEqual(["homelap"]), "Repeated forms are one hint");
        check(PersonalVocabulary.SoundHints("Reinicia el homelap", []).Count == 0 && PersonalVocabulary.SoundHints("", jargon).Count == 0 && PersonalVocabulary.Apply("Reinicia el homelap", []) == "Reinicia el homelap", "Empty vocabulary or text: no hints, no changes");

        var prompt = PersonalVocabulary.Prompt(vocabulary);
        check(prompt.StartsWith("Glosario: ") && prompt.Contains("Nova-Hub") && prompt.Contains("Claude") && !prompt.Contains("novahup") && !prompt.Contains("cloud"), "Whisper receives only correctly spelled terms: " + prompt);
    }
}

/// <summary>
/// Counts term forms in a local history before and after Apply, and the sound hints the AI editor would receive.
/// Prints counts only, never dictated text; --show-forms also prints each hint as "heard -> term" (single words or
/// short phrases, for a manual review on the user's own machine).
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
        int replaced = 0, hints = 0, hintedDictations = 0;
        foreach (var item in history)
        {
            var raw = item.OriginalText ?? item.Text;
            var fixedText = PersonalVocabulary.Apply(raw, vocabulary, out var count); replaced += count;
            // Hints are computed on the text the editor receives: after the "=" forms and spelling variants.
            var sent = PersonalVocabulary.SoundHints(fixedText, vocabulary);
            hints += sent.Sum(h => h.Value.Count);
            if (sent.Count > 0) hintedDictations++;
            foreach (var (term, heard) in sent) foreach (var h in heard) { var k = $"{h} -> {term}"; forms[k] = forms.GetValueOrDefault(k) + 1; }
            foreach (var probe in probes)
            {
                before[probe] = before.GetValueOrDefault(probe) + VoiceEval.Occurrences(raw, probe);
                after[probe] = after.GetValueOrDefault(probe) + VoiceEval.Occurrences(fixedText, probe);
            }
        }
        Console.WriteLine($"dictations={history.Count} replacements={replaced} soundHints={hints} dictationsWithSoundHints={hintedDictations}");
        foreach (var probe in probes) Console.WriteLine($"{probe}: {before[probe]} -> {after[probe]}");
        if (showForms) foreach (var (form, n) in forms) Console.WriteLine($"hint {form} x{n}");
    }
}
