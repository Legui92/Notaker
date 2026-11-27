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
    [GeneratedRegex(@"[\p{L}][\p{L}\p{M}\p{N}'’_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();

    /// <summary>Lowercase letters and digits only: "Nova-Hub", "nova hub" and "NovaHub" share one key.</summary>
    public static string Key(string text)
    {
        var key = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormC)) if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
        return key.ToString();
    }

    public static VocabularyEntry Parse(string line)
    {
        line = line.Trim();
        var first = line.IndexOfAny(['=', '~']);
        if (first <= 0) return new VocabularyEntry(line, [], []);
        var term = line[..first].Trim();
        if (Key(term).Length == 0) return new VocabularyEntry(line, [], []);
        List<string> always = [], context = [];
        var position = first;
        while (position < line.Length)
        {
            var next = line.IndexOfAny(['=', '~'], position + 1);
            var segment = line[(position + 1)..(next < 0 ? line.Length : next)];
            (line[position] == '=' ? always : context).AddRange(segment.Split(',').Select(h => h.Trim()).Where(h => h.Length >= 2 && Key(h).Length > 0 && Key(h) != Key(term)));
            if (next < 0) break;
            position = next;
        }
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

    /// <summary>Merges spelling variants of the same term and drops bare entries that are known mis-hearings of another term.</summary>
    public static List<VocabularyEntry> Entries(IEnumerable<string> lines)
    {
        var entries = new List<VocabularyEntry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.Trim();
            if (trimmed.Length is < 2 or > MaxLineLength || trimmed.Any(char.IsControl)) continue;
            var entry = Parse(trimmed);
            if (entry.Term.Length < 2 || Key(entry.Term).Length == 0) continue;
            var index = entries.FindIndex(e => Key(e.Term) == Key(entry.Term));
            if (index < 0) entries.Add(entry);
            else
            {
                // First spelling wins, except that a written form ("Nova-Hub") beats a bare lowercase one ("novahub").
                var merged = Merge(entries[index], entry.Always, entry.Context);
                entries[index] = !IsWritten(merged.Term) && IsWritten(entry.Term) ? merged with { Term = entry.Term } : merged;
            }
        }
        var heardKeys = entries.SelectMany(e => e.HeardAs).Select(Key).ToHashSet();
        return entries.Where(e => e.HeardAs.Any() || !heardKeys.Contains(Key(e.Term))).Take(MaxEntries).ToList();
    }

    // Inner capitals or separators carry spelling information; a capitalized first letter alone does not.
    private static bool IsWritten(string term) => term.Skip(1).Any(char.IsUpper) || term.Any(c => !char.IsLetterOrDigit(c));

    public static List<string> Normalize(IEnumerable<string> lines) => Entries(lines).Select(e => e.ToString()).ToList();

    /// <summary>
    /// Learns from a correction made inside Notaker. Only words that replaced recognized words count, so text
    /// the user merely adds does not pollute the vocabulary. Short replacements become "Term ~ heard" pairs:
    /// context-only, because the heard form may be a real word. The user can change "~" to "=" to always replace.
    /// </summary>
    public static List<string> Learn(string original, string corrected)
    {
        var before = Tokens().Matches(original).Select(m => m.Value).ToList();
        var after = Tokens().Matches(corrected).Select(m => m.Value).ToList();
        var learned = new List<string>();
        foreach (var (oldSpan, newSpan) in ReplacedSpans(before, after))
        {
            var heard = string.Join(" ", oldSpan);
            var term = PhraseIn(corrected, newSpan);
            if (newSpan.Count <= 3 && oldSpan.Count <= 3 && Key(term).Length >= 3 && Key(term) != Key(heard) && newSpan.Any(w => !Common.Contains(w)))
                learned.Add($"{term} ~ {heard}");
            else
                learned.AddRange(Words().Matches(string.Join(" ", newSpan)).Select(m => m.Value).Where(w => w.Length >= 3 && !Common.Contains(w)));
        }
        return learned.DistinctBy(l => Key(Parse(l).Term)).Take(10).ToList();
    }

    // Keeps the corrected spelling, including hyphens between the words ("Nova-Hub").
    private static string PhraseIn(string corrected, List<string> span)
    {
        var pattern = string.Join(@"[^\p{L}\p{M}\p{N}\r\n]{1,3}", span.Select(Regex.Escape));
        var match = Regex.Match(corrected, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match.Value : string.Join(" ", span);
    }

    // Word-level LCS alignment, case-insensitive. Pure insertions and deletions are not replacements.
    private static IEnumerable<(List<string> Old, List<string> New)> ReplacedSpans(List<string> a, List<string> b)
    {
        if ((long)a.Count * b.Count > 4_000_000) yield break;
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = string.Equals(a[i], b[j], StringComparison.OrdinalIgnoreCase) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        int x = 0, y = 0;
        List<string> oldSpan = [], newSpan = [];
        while (x < a.Count || y < b.Count)
        {
            if (x < a.Count && y < b.Count && string.Equals(a[x], b[y], StringComparison.OrdinalIgnoreCase))
            {
                if (oldSpan.Count > 0 && newSpan.Count > 0) yield return (oldSpan, newSpan);
                oldSpan = []; newSpan = []; x++; y++;
            }
            else if (y < b.Count && (x == a.Count || lcs[x, y + 1] >= lcs[x + 1, y])) newSpan.Add(b[y++]);
            else oldSpan.Add(a[x++]);
        }
        if (oldSpan.Count > 0 && newSpan.Count > 0) yield return (oldSpan, newSpan);
    }

    /// <summary>Whisper prompt: correctly spelled terms only, never the mis-heard forms.</summary>
    public static string Prompt(IEnumerable<string> lines)
    {
        var result = new List<string>();
        var length = "Glosario: .".Length;
        foreach (var term in Entries(lines).Select(e => e.Term))
        {
            if (length + term.Length + 2 > 600) break;
            result.Add(term); length += term.Length + 2;
        }
        return result.Count == 0 ? "" : "Glosario: " + string.Join(", ", result) + ".";
    }

    /// <summary>
    /// Deterministic local replacement of "=" forms, plus spacing, hyphen and case variants of terms written
    /// with inner capitals, digits or several words ("nova hub" -> "Nova-Hub"). Whole words only, one pass,
    /// longest form first. "~" forms and plain lowercase terms are never touched here.
    /// </summary>
    public static string Apply(string text, IEnumerable<string> lines) => Apply(text, lines, out _);
    public static string Apply(string text, IEnumerable<string> lines, out int replacements)
    {
        replacements = 0;
        if (string.IsNullOrWhiteSpace(text)) return text;
        var forms = new List<(string Pattern, string Term)>();
        foreach (var entry in Entries(lines))
        {
            var termTokens = Tokens().Matches(entry.Term).Select(m => m.Value).ToList();
            var hasCapital = entry.Term.Any(char.IsUpper);
            var distinctive = (termTokens.Count > 1 && hasCapital) || entry.Term.Skip(1).Any(c => char.IsUpper(c) || char.IsDigit(c));
            if (distinctive) forms.Add((FormPattern(entry.Term), entry.Term));
            foreach (var heard in entry.Always) forms.Add((FormPattern(heard), entry.Term));
        }
        if (forms.Count == 0) return text;
        var ordered = forms.DistinctBy(f => f.Pattern).OrderByDescending(f => f.Pattern.Length).ToList();
        var regex = new Regex(@"(?<![\p{L}\p{M}\p{N}])(?:" + string.Join("|", ordered.Select((f, i) => $"(?<g{i}>{f.Pattern})")) + @")(?![\p{L}\p{M}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var count = 0;
        string result;
        try
        {
            result = regex.Replace(text, match =>
            {
                for (var i = 0; i < ordered.Count; i++)
                    if (match.Groups[$"g{i}"].Success)
                    {
                        if (match.Value != ordered[i].Term) count++;
                        return ordered[i].Term;
                    }
                return match.Value;
            });
        }
        catch (RegexMatchTimeoutException) { return text; }
        replacements = count;
        return result;
    }

    // Same letters with optional spaces, hyphens, dots or apostrophes between them: "novahup" also matches
    // "nova up" and "nova-up". Accents are matched literally.
    private static string FormPattern(string form) => string.Join(@"[\s\-.'’]?", Key(form).Select(c => Regex.Escape(c.ToString())));
}
