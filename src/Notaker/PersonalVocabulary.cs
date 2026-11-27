using System.Text;
using System.Text.RegularExpressions;

namespace Notaker;

/// <summary>
/// One vocabulary line: "Term", "Term = heard, heard" or "Term ~ heard" (both may appear: "Term = a ~ b").
/// "=" forms are always replaced locally. "~" forms are also real words (cloud / Claude): only the AI editor
/// replaces them, and only when the context refers to the term.
/// </summary>
public sealed record VocabularyEntry(string Term, IReadOnlyList<string> Always, IReadOnlyList<string> Context)
{
    public IEnumerable<string> HeardAs => Always.Concat(Context);
    public override string ToString() => Term + (Always.Count > 0 ? " = " + string.Join(", ", Always) : "") + (Context.Count > 0 ? " ~ " + string.Join(", ", Context) : "");
}

public static partial class PersonalVocabulary
{
    public const int MaxEntries = 100;
    public const int MaxLineLength = 200;
    private static readonly HashSet<string> Common = new(("para por pero como que porque cuando donde desde hasta sobre entre esta este esto eso esa ese " +
        "las los una unos unas del con sin son ser soy era fue hay muy más mas menos mañana hoy ayer tengo tiene " +
        "and the this that these those with from have has had was were will would could should not for you your are " +
        "can please gracias favor también tambien entonces necesito quiero hola enviar documento reunión reunion").Split(' '), StringComparer.OrdinalIgnoreCase);
    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();

    /// <summary>Lowercase letters and digits only: "Nova-Hub", "nova hub" and "NovaHub" share one key.</summary>
    public static string Key(string text)
    {
        var key = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormC)) if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
        return key.ToString();
    }

    /// <summary>
    /// Identity of an entry: its letter key, or its spelling without spaces when it has one letter or none, so
    /// "C#" and "C++" (both "c") stay two terms while "Nova-Hub" and "nova hub" stay one.
    /// </summary>
    public static string EntryKey(string term)
    {
        var key = Key(term);
        return key.Length > 1 ? key : string.Concat(term.Normalize(NormalizationForm.FormC).Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
    }

    // "Term = heard": a separator with a space on one side, or a tight one between a written term and a
    // spoken word ("X-Hub=xhup"). "E=mc2", "Pi=3.14" or "a=b" stay plain terms. A term ending in a symbol or
    // shaped like a chemical formula is a rule only when the spoken word starts with its first letter
    // ("C#=csharp", "C++=cplusplus", "B2B=bitubi"): "a+=bb", "x#=yy", "H2O=agua" or "NaCl=sal" stay plain.
    // With spaces ("F# = efe sharp", "H2O = agua") any spoken word counts.
    private static bool IsSeparator(string line, int i) => line[i] is '=' or '~'
        && ((i > 0 && char.IsWhiteSpace(line[i - 1])) || (i + 1 < line.Length && char.IsWhiteSpace(line[i + 1])) || IsTightSeparator(line, i));

    private static bool IsTightSeparator(string line, int i)
    {
        var left = line[..i];
        left = left[(left.LastIndexOfAny([' ', '\t', ',', '=', '~']) + 1)..];
        var right = line[(i + 1)..];
        var end = right.IndexOfAny([' ', '\t', ',', '=', '~']);
        right = end < 0 ? right : right[..end];
        if (!(left.Length >= 2 && Key(left).Length > 0 && (char.IsLetterOrDigit(left[^1]) || left[^1] is '#' or '+') && left.All(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '\'' or '’' or '+' or '#')
            && right.Length >= 2 && right.All(c => char.IsLetter(c) || c is '-' or '\'' or '’'))) return false;
        return (char.IsLetterOrDigit(left[^1]) && !IsChemicalFormula(left)) || Key(right).StartsWith(Key(left)[0]);
    }

    private static readonly HashSet<string> Elements = new(("H He Li Be B C N O F Ne Na Mg Al Si P S Cl Ar K Ca Sc Ti V Cr Mn Fe Co Ni Cu Zn Ga Ge As Se Br Kr " +
        "Rb Sr Y Zr Nb Mo Tc Ru Rh Pd Ag Cd In Sn Sb Te I Xe Cs Ba La Ce Pr Nd Pm Sm Eu Gd Tb Dy Ho Er Tm Yb Lu Hf Ta W Re Os Ir Pt Au Hg Tl Pb Bi Po At Rn " +
        "Fr Ra Ac Th Pa U Np Pu Am Cm Bk Cf Es Fm Md No Lr Rf Db Sg Bh Hs Mt Ds Rg Cn Nh Fl Mc Lv Ts Og").Split(' '), StringComparer.Ordinal);

    [GeneratedRegex(@"\G([A-Z][a-z]?)(\d*)", RegexOptions.CultureInvariant)]
    private static partial Regex FormulaPart();

    // Element symbols with counts and at least one digit or two-letter symbol ("H2O", "NaCl", "CO2"). All-capital
    // acronyms without digits ("PR", "IT", "CS") are not formulas.
    private static bool IsChemicalFormula(string text)
    {
        var parts = FormulaPart().Matches(text);
        if (parts.Sum(m => m.Length) != text.Length || !parts.All(m => Elements.Contains(m.Groups[1].Value))) return false;
        var hasDigit = text.Any(char.IsDigit);
        return (hasDigit || parts.Count >= 2) && (hasDigit || text.Any(char.IsLower));
    }

    public static VocabularyEntry Parse(string line)
    {
        line = line.Trim();
        var separators = Enumerable.Range(0, line.Length).Where(i => IsSeparator(line, i)).ToList();
        if (separators.Count == 0 || separators[0] == 0) return new VocabularyEntry(line, [], []);
        var term = line[..separators[0]].Trim();
        List<string> always = [], context = [];
        for (var s = 0; s < separators.Count; s++)
        {
            var start = separators[s] + 1;
            var end = s + 1 < separators.Count ? separators[s + 1] : line.Length;
            // Heard forms are spoken words: they need letters ("Pi = 3.14" is a plain term, not a rule).
            (line[separators[s]] == '=' ? always : context).AddRange(line[start..end].Split(',').Select(h => h.Trim())
                .Where(h => h.Length >= 2 && h.Any(char.IsLetter) && Key(h) != Key(term)));
        }
        // A term of a single character is a formula or a symbol ("E = mc2", "F = ma"), never a replacement rule.
        // "C# = c sharp" or "C++ = c plus plus" are written with more characters and keep their rules.
        if (Key(term).Length == 0 || term.Length < 2 || always.Count + context.Count == 0) return new VocabularyEntry(line, [], []);
        return Merge(new VocabularyEntry(term, [], []), always, context);
    }

    private static VocabularyEntry Merge(VocabularyEntry entry, IEnumerable<string> always, IEnumerable<string> context)
    {
        var a = entry.Always.Concat(always).DistinctBy(Key).ToList();
        var keys = a.Select(Key).ToHashSet();
        // A form the user marked as always-replaced stays always-replaced.
        var c = entry.Context.Concat(context).Where(h => !keys.Contains(Key(h))).DistinctBy(Key).ToList();
        return entry with { Always = a, Context = c };
    }

    /// <summary>Merges spelling variants of the same term and drops bare entries that are always-replaced mis-hearings of another term.</summary>
    public static List<VocabularyEntry> Entries(IEnumerable<string> lines)
    {
        var entries = new List<VocabularyEntry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.Trim();
            if (trimmed.Length is < 2 or > MaxLineLength || trimmed.Any(char.IsControl)) continue;
            var entry = Parse(trimmed);
            if (Key(entry.Term).Length == 0) continue;
            var index = entries.FindIndex(e => EntryKey(e.Term) == EntryKey(entry.Term));
            if (index < 0) entries.Add(entry);
            else
            {
                // First spelling wins, except that a written form ("Nova-Hub") beats a bare lowercase one ("novahub").
                var merged = Merge(entries[index], entry.Always, entry.Context);
                entries[index] = !IsWritten(merged.Term) && IsWritten(entry.Term) ? merged with { Term = entry.Term } : merged;
            }
        }
        // A "~" form is a real word: a bare entry with that spelling is kept.
        var replacedKeys = entries.SelectMany(e => e.Always).Select(Key).ToHashSet();
        return entries.Where(e => e.HeardAs.Any() || !replacedKeys.Contains(EntryKey(e.Term))).Take(MaxEntries).ToList();
    }

    // Inner capitals or symbols such as hyphens carry spelling information; spaces or a capitalized first letter alone do not.
    private static bool IsWritten(string term) => term.Skip(1).Any(char.IsUpper) || term.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));

    public static List<string> Normalize(IEnumerable<string> lines) => Entries(lines).Select(e => e.ToString()).ToList();

    /// <summary>
    /// Learns from a correction made inside Notaker. Only short replacements (up to three words on each side)
    /// count, never added text, long rewrites or numbers. A replacement is learned as "Term ~ heard" only when
    /// the new spelling looks like a name or jargon: a capital that is not the start of a sentence, inner
    /// capitals or hyphens. Fixing only the spelling of a term, or joining its words, learns the term itself.
    /// Learned forms are context-only; the user can change "~" to "=" to always replace them. A change of case
    /// alone ("claude" -> "Claude") inside a sentence learns the term as a glossary entry; at the start of a
    /// sentence it is ordinary capitalization and is not learned. Added names are not learned.
    /// </summary>
    public static List<string> Learn(string original, string corrected)
    {
        var before = Tokens().Matches(original).Select(m => m.Value).ToList();
        var after = Tokens().Matches(corrected).Select(m => m.Value).ToList();
        var learned = new List<string>();
        foreach (var (oldSpan, newSpan, caseOnly) in ReplacedSpans(before, after))
        {
            if (oldSpan.Count > 3 || newSpan.Count > 3 || oldSpan.Concat(newSpan).Any(w => w.Any(char.IsDigit))) continue;
            var (term, index) = PhraseIn(corrected, newSpan);
            var heard = string.Join(" ", oldSpan);
            if (Key(term).Length < 3 || newSpan.All(Common.Contains)) continue;
            var sentenceStart = IsSentenceStart(corrected, index);
            // Case only: the term enters the glossary, never as a rule. A term with spelling variants (inner capitals
            // or hyphens: "DeepSeek", "LaTeX") would rewrite every lowercase use ("guantes de latex"), so it is not learned.
            if (caseOnly) { if (!sentenceStart && char.IsUpper(term[0]) && !HasSpellingVariants(term)) learned.Add(term); continue; }
            var looksLikeName = IsWritten(term) || (char.IsUpper(term[0]) && !sentenceStart);
            // Same letters: a spelling fix ("novahub" -> "Nova-Hub") or joined words ("work tree" -> "worktree").
            if (Key(term) == Key(heard)) { if (looksLikeName || newSpan.Count != oldSpan.Count) learned.Add(term); continue; }
            if (looksLikeName) learned.Add($"{term} ~ {heard}");
        }
        return learned.DistinctBy(l => EntryKey(Parse(l).Term)).Take(10).ToList();
    }

    // The start of the text or of a line, a list item ("- ", "• ", "1. ", "a) ") or a word after . ! ? ¿ ¡ : or …,
    // also behind opening quotes, brackets or a dialogue dash ("«Luego", "—Luego", "… «Luego"). A dash or a quote
    // inside a sentence ("fui —con Claude", "dijo «Claude»") is not a start.
    private static bool IsSentenceStart(string corrected, int index)
    {
        var before = corrected[..index];
        var line = before[(before.LastIndexOf('\n') + 1)..].Trim();
        if (line.Length > 0 && line[^1] is '¿' or '¡') return true;
        line = line.TrimEnd('«', '"', '“', '‘', '\'', '(', '[', '—', '–', ' ');
        if (line.Length == 0 || ListMarker().IsMatch(line)) return true;
        return line[^1] is '.' or '!' or '?' or '¿' or '¡' or ':' or '…';
    }

    [GeneratedRegex(@"^(?:[-*•]|\d{1,3}[.)]|[a-zA-Z][.)])$", RegexOptions.CultureInvariant)]
    private static partial Regex ListMarker();

    // Keeps the corrected spelling, including hyphens between the words ("Nova-Hub").
    private static (string Text, int Index) PhraseIn(string corrected, List<string> span)
    {
        var pattern = string.Join(@"[^\p{L}\p{M}\p{N}\r\n]{1,3}", span.Select(Regex.Escape));
        var match = Regex.Match(corrected, pattern, RegexOptions.CultureInvariant);
        return match.Success ? (match.Value, match.Index) : (string.Join(" ", span), 0);
    }

    // Word-level LCS alignment, case-insensitive. Pure insertions and deletions are not replacements. Runs of
    // aligned words that changed only their case come out apart, with CaseOnly set.
    private static IEnumerable<(List<string> Old, List<string> New, bool CaseOnly)> ReplacedSpans(List<string> a, List<string> b)
    {
        if ((long)a.Count * b.Count > 4_000_000) yield break;
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = string.Equals(a[i], b[j], StringComparison.OrdinalIgnoreCase) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        int x = 0, y = 0;
        List<string> oldSpan = [], newSpan = [], oldCase = [], newCase = [];
        while (x < a.Count || y < b.Count)
        {
            if (x < a.Count && y < b.Count && string.Equals(a[x], b[y], StringComparison.OrdinalIgnoreCase))
            {
                if (oldSpan.Count > 0 && newSpan.Count > 0) yield return (oldSpan, newSpan, false);
                oldSpan = []; newSpan = [];
                if (a[x] != b[y]) { oldCase.Add(a[x]); newCase.Add(b[y]); }
                else
                {
                    if (newCase.Count > 0) yield return (oldCase, newCase, true);
                    oldCase = []; newCase = [];
                }
                x++; y++;
                continue;
            }
            if (newCase.Count > 0) yield return (oldCase, newCase, true);
            oldCase = []; newCase = [];
            if (y < b.Count && (x == a.Count || lcs[x, y + 1] >= lcs[x + 1, y])) newSpan.Add(b[y++]);
            else oldSpan.Add(a[x++]);
        }
        if (oldSpan.Count > 0 && newSpan.Count > 0) yield return (oldSpan, newSpan, false);
        if (newCase.Count > 0) yield return (oldCase, newCase, true);
    }

    /// <summary>Upper bound for every Whisper prompt built here, with or without live context.</summary>
    public const int MaxPromptLength = 600;
    public const int MaxContextLength = 200;

    /// <summary>Whisper prompt: correctly spelled terms only, never the mis-heard forms.</summary>
    public static string Prompt(IEnumerable<string> lines) => Prompt(lines, MaxPromptLength);
    public static string Prompt(IEnumerable<string> lines, int maxLength)
    {
        var result = new List<string>();
        var length = "Glosario: .".Length;
        foreach (var term in Entries(lines).Select(e => e.Term))
        {
            if (length + term.Length + 2 > maxLength) break;
            result.Add(term); length += term.Length + 2;
        }
        return result.Count == 0 ? "" : "Glosario: " + string.Join(", ", result) + ".";
    }

    /// <summary>
    /// Last words of the text already recognized, for the next live fragment: at most <paramref name="maxLength"/>
    /// characters, starting at a word boundary (a sentence start when one fits), line breaks and repeated spaces
    /// collapsed. Never cuts a word in half.
    /// </summary>
    public static string ContextTail(string? previous, int maxLength = MaxContextLength)
    {
        if (string.IsNullOrWhiteSpace(previous) || maxLength <= 0) return "";
        var flat = string.Join(' ', previous.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= maxLength) return flat;
        var window = flat[^maxLength..];
        var cut = flat[^(maxLength + 1)] == ' ' ? 0 : window.IndexOf(' ') + 1;
        if (cut <= 0 && flat[^(maxLength + 1)] != ' ') return "";
        window = window[cut..];
        // Prefer a whole sentence: Whisper continues the style of the prompt, a half sentence invites repeats.
        for (var i = 0; i + 1 < window.Length / 2; i++)
            if (window[i] is '.' or '!' or '?' && window[i + 1] == ' ') return window[(i + 2)..];
        return window;
    }

    /// <summary>
    /// Live prompt: glossary plus the tail of the previous fragments, within the same bound as the glossary alone.
    /// The tail goes through <see cref="Apply"/> first, so Whisper sees the fixed spelling, not the mis-hearing.
    /// </summary>
    public static string LivePrompt(IEnumerable<string> lines, string? previous)
    {
        var list = lines as IList<string> ?? lines.ToList();
        var tail = ContextTail(string.IsNullOrWhiteSpace(previous) ? previous : Apply(previous, list));
        var glossary = Prompt(list, MaxPromptLength - (tail.Length == 0 ? 0 : tail.Length + 1));
        return tail.Length == 0 ? glossary : glossary.Length == 0 ? tail : glossary + " " + tail;
    }

    // Spelling variants are normalized only for written compound names: letters at both ends, inner capitals
    // or hyphens, no digits, spaces or all-capital acronyms. ".NET", "UNA", "2026", "A4" or "Mercado Libre"
    // stay prompt hints and are never rewritten locally unless the user declares "=" forms.
    private static bool HasSpellingVariants(string term) =>
        term.Length >= 3 && char.IsLetter(term[0]) && char.IsLetter(term[^1]) && term.All(c => char.IsLetter(c) || c == '-')
        && (term.Contains('-') || term.Skip(1).Any(char.IsUpper)) && term.Any(char.IsLower);

    // Words that join into everyday compounds ("read-only", "know-how", "check-out"). Only used for hyphenated
    // terms: Learn keeps its own list of common words.
    private static readonly HashSet<string> OrdinaryParts = new(("read write only know how check out off back down over under self well built " +
        "real time full open close end front high low long short user sign set log make take follow pop drop pick look run start stop give get free side line " +
        "cut break push pull lock all any one two non pre post").Split(' '), StringComparer.OrdinalIgnoreCase);

    // Variants of a written term: case, and a space or hyphen where the term has a hyphen ("nova hub" -> "Nova-Hub").
    // A part of two letters or less, a common word or an everyday compound part makes the spaced and joined forms
    // ordinary text ("what to do", "a team", "login", "read only", "git checkout"): then only the case of the
    // hyphenated form varies.
    private static string VariantPattern(string term)
    {
        var parts = term.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var separator = parts.Any(p => p.Length <= 2 || Common.Contains(p) || OrdinaryParts.Contains(p)) ? "-" : "[ -]?";
        return string.Join(separator, parts.Select(Regex.Escape));
    }

    // A heard form with optional spaces, hyphens, dots or apostrophes between its letters, never line breaks:
    // "novahup" also matches "nova up" and "nova-up". Accents are matched literally.
    private static string HeardPattern(string form) => string.Join(@"[ \-.'’]?", Key(form).Select(c => Regex.Escape(c.ToString())));

    private static readonly object CacheGate = new();
    private static (string Signature, Regex? Regex, string[] Terms) cache = ("", null, []);

    private static (Regex? Regex, string[] Terms) Replacer(IEnumerable<string> lines)
    {
        var list = lines as IList<string> ?? lines.ToList();
        var signature = string.Join("\n", list);
        lock (CacheGate) if (cache.Signature == signature && cache.Regex != null) return (cache.Regex, cache.Terms);
        var forms = new List<(string Pattern, string Term, int Letters)>();
        foreach (var entry in Entries(list))
        {
            if (HasSpellingVariants(entry.Term)) forms.Add((VariantPattern(entry.Term), entry.Term, Key(entry.Term).Length));
            foreach (var heard in entry.Always) forms.Add((HeardPattern(heard), entry.Term, Key(heard).Length));
        }
        // Longest form first, counted in letters: a heard pattern is a much longer string than a variant pattern.
        var ordered = forms.Where(f => f.Pattern.Length > 0).DistinctBy(f => f.Pattern).OrderByDescending(f => f.Letters).ThenByDescending(f => f.Pattern.Length).ToList();
        var regex = ordered.Count == 0 ? null : new Regex(@"(?<![\p{L}\p{M}\p{N}])(?:" + string.Join("|", ordered.Select((f, i) => $"(?<g{i}>{f.Pattern})")) + @")(?![\p{L}\p{M}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var terms = ordered.Select(f => f.Term).ToArray();
        lock (CacheGate) cache = (signature, regex, terms);
        return (regex, terms);
    }

    // Web addresses, emails, paths and file names keep their spelling: "https://github.com/x", "www.github.com",
    // "ana@github.com", "C:\novahup\x", "github.com" or "novahup.txt".
    private static bool InAddress(string text, int index, int length)
    {
        int start = index, end = index + length;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        var token = text[start..end];
        return token.Contains("://") || token.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || token.IndexOfAny(['/', '\\', '@']) >= 0
            || (index + length + 1 < text.Length && text[index + length] == '.' && char.IsLetterOrDigit(text[index + length + 1]));
    }

    /// <summary>
    /// Deterministic local replacement of "=" forms and spelling variants of written compound names. Whole words
    /// only, one pass, longest form first, idempotent. "~" forms are never touched here.
    /// </summary>
    public static string Apply(string text, IEnumerable<string> lines) => Apply(text, lines, out _);
    public static string Apply(string text, IEnumerable<string> lines, out int replacements)
    {
        replacements = 0;
        if (string.IsNullOrWhiteSpace(text)) return text;
        var (regex, terms) = Replacer(lines);
        if (regex == null) return text;
        var count = 0;
        try
        {
            var result = regex.Replace(text, match =>
            {
                if (InAddress(text, match.Index, match.Length)) return match.Value;
                for (var i = 0; i < terms.Length; i++)
                    if (match.Groups[$"g{i}"].Success)
                    {
                        if (match.Value != terms[i]) count++;
                        return terms[i];
                    }
                return match.Value;
            });
            replacements = count;
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            AppLog.Write("vocabulary.timeout.original_kept");
            return text;
        }
    }
}
