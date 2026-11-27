using System.Text;
using System.Text.RegularExpressions;

namespace Notaker;

/// <summary>A word or short phrase of the dictation that sounds like a vocabulary term. Only ever a hint.</summary>
public sealed record SoundMatch(int Index, int Length, string Heard, string Term, int Distance);

/// <summary>
/// Sound hints for the AI editor (vocabulario fonético). Nothing here rewrites text: words that sound like a term
/// travel to the editor as extra context_only forms of that term, and the editor decides by meaning. Local text
/// only changes through "=" forms and spelling variants (<see cref="Apply(string, IEnumerable{string})"/>).
/// </summary>
public static partial class PersonalVocabulary
{
    // A spoken word, with inner hyphens or apostrophes. Never part of a number, path, handle, file name or code.
    [GeneratedRegex(@"(?<![\p{L}\p{M}\p{N}_@#/\\.\-'’])[\p{L}\p{M}]+(?:[\-'’][\p{L}\p{M}]+)*(?![\p{L}\p{M}\p{N}_@#/\\\-'’])(?!\.[\p{L}\p{M}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex SpokenWords();

    private const int MaxSpanWords = 3;
    private const int MinSoundKey = 5;
    /// <summary>Hints per dictation and per term: the prompt never grows by more than a few words.</summary>
    public const int MaxSoundHints = 8;
    public const int MaxSoundHintsPerTerm = 3;

    /// <summary>Lowercase ASCII letters only: accents and every other character dropped ("Baúl" -> "baul").</summary>
    internal static string Plain(string text)
    {
        var plain = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z') plain.Append(lower);
        }
        return plain.ToString();
    }

    /// <summary>
    /// Rough sound key shared by Spanish and English spellings: silent h and final e dropped, b/v/w, p/b, t/d, k/g/c/q
    /// and s/z/soft c merged, English digraphs (th, ph, sh, ck, ee, oo, ea) read the way Whisper writes them in Spanish.
    /// "DeepSeek" and "Dipsick" share "tipsik"; "homelab" and "homelapse" differ in one sound.
    /// </summary>
    public static string SoundKey(string text)
    {
        var s = Plain(text).Replace("x", "ks").Replace("sch", "sk").Replace("ph", "f").Replace("th", "t").Replace("sh", "s")
            .Replace("ch", "C").Replace("ck", "k").Replace("qu", "k").Replace("q", "k");
        var b = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == 'c') b.Append(next is 'e' or 'i' or 'y' ? 's' : 'k');
            else if (c == 'g' && next == 'u' && i + 2 < s.Length && s[i + 2] is 'e' or 'i') { b.Append('g'); i++; }
            else b.Append(c);
        }
        s = b.ToString().Replace("ee", "i").Replace("oo", "u").Replace("ea", "e");
        b.Clear();
        for (var i = 0; i < s.Length; i++)
        {
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            switch (s[i])
            {
                case 'y': b.Append(next is 'a' or 'e' or 'i' or 'o' or 'u' ? 'y' : 'i'); break;
                case 'h': break;
                case 'z': b.Append('s'); break;
                case 'v' or 'w' or 'b': b.Append('p'); break;
                case 'd': b.Append('t'); break;
                case 'g': b.Append('k'); break;
                default: b.Append(s[i]); break;
            }
        }
        if (b.Length >= 2 && b[^1] == 'e' && b[^2] is not ('a' or 'e' or 'i' or 'o' or 'u')) b.Length--;
        var key = new StringBuilder(b.Length);
        foreach (var c in b.ToString()) if (key.Length == 0 || key[^1] != c) key.Append(c);
        return key.ToString();
    }

    /// <summary>Optimal string alignment distance (substitution, insertion, deletion, adjacent transposition).</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    private sealed record SoundTarget(string Term, string Sound, string Letters);
    private sealed record SoundTable(SoundTarget[] Targets, HashSet<string> VocabularyKeys);
    private static (string Signature, SoundTable Table) soundCache = ("\0", new([], []));

    // Only written words of five sounds or more: no digits, spaces, symbols or all-capital acronyms ("eli5", "UNA",
    // "Mercado Libre", ".NET" and "Orca" are never targets). Every term, "=" form and "~" form is already known.
    private static SoundTable Sounds(IList<string> lines)
    {
        var signature = string.Join("\n", lines);
        lock (CacheGate) if (soundCache.Signature == signature) return soundCache.Table;
        var entries = Entries(lines);
        var targets = entries
            .Where(e => e.Term.All(c => char.IsLetter(c) || c == '-') && char.IsLetter(e.Term[0]) && char.IsLetter(e.Term[^1]) && e.Term.Any(char.IsLower))
            .Select(e => new SoundTarget(e.Term, SoundKey(e.Term), Key(e.Term)))
            .Where(t => t.Sound.Length >= MinSoundKey).ToArray();
        var keys = entries.SelectMany(e => e.HeardAs.Prepend(e.Term)).Select(Key).Where(k => k.Length > 0).ToHashSet();
        var table = new SoundTable(targets, keys);
        lock (CacheGate) soundCache = (signature, table);
        return table;
    }

    /// <summary>
    /// Words and short phrases (up to three words of three letters or more on one line, single spaces between them,
    /// no function words) that sound like a vocabulary term without being written like one: at most one sound edit
    /// for terms of five or six sounds and two from seven, with the same first sound. Never matched: vocabulary
    /// spellings and heard forms, inflections or derivations that start with the term ("backlogs", "commitear"),
    /// acronyms, ties between two terms, and anything glued to digits or symbols.
    /// </summary>
    public static List<SoundMatch> SoundMatches(string text, IEnumerable<string> lines)
    {
        var found = new List<SoundMatch>();
        if (string.IsNullOrWhiteSpace(text)) return found;
        var table = Sounds(lines as IList<string> ?? lines.ToList());
        if (table.Targets.Length == 0) return found;
        var words = SpokenWords().Matches(text).ToList();
        for (var start = 0; start < words.Count; start++)
        {
            for (var count = 1; count <= MaxSpanWords && start + count <= words.Count; count++)
            {
                var last = words[start + count - 1];
                if (count > 1 && words[start + count - 2] is var before && text.Substring(before.Index + before.Length, last.Index - before.Index - before.Length) != " ") break;
                var span = words.GetRange(start, count);
                if (span.Any(w => table.VocabularyKeys.Contains(Key(w.Value)))) break;
                // A phrase never absorbs short or function words ("baúl de" is not "vault").
                if (count > 1 && span.Any(w => w.Value.Length < 3 || Common.Contains(w.Value))) break;
                var heard = text.Substring(words[start].Index, last.Index + last.Length - words[start].Index);
                var letters = Key(heard);
                if (table.VocabularyKeys.Contains(letters) || Common.Contains(heard) || span.Any(w => w.Value.Length > 1 && w.Value.All(char.IsUpper))) continue;
                var sound = SoundKey(heard);
                if (sound.Length < MinSoundKey - 1) continue;
                SoundTarget? best = null; int bestDistance = int.MaxValue; var tie = false;
                foreach (var target in table.Targets)
                {
                    if (sound[0] != target.Sound[0] || letters.StartsWith(target.Letters, StringComparison.Ordinal)) continue;
                    var allowed = target.Sound.Length >= 7 ? 2 : 1;
                    if (Math.Abs(sound.Length - target.Sound.Length) > allowed || letters.Length * 2 < target.Letters.Length || letters.Length > target.Letters.Length * 2) continue;
                    var distance = Distance(sound, target.Sound);
                    if (distance > allowed) continue;
                    if (distance < bestDistance) { best = target; bestDistance = distance; tie = false; }
                    else if (distance == bestDistance && Key(best!.Term) != Key(target.Term)) tie = true;
                }
                if (best == null || tie) continue;
                found.Add(new SoundMatch(words[start].Index, heard.Length, heard, best.Term, bestDistance));
            }
        }
        // Closest first, then the longer phrase, then reading order; overlapping matches are dropped.
        var chosen = new List<SoundMatch>();
        foreach (var match in found.OrderBy(m => m.Distance).ThenByDescending(m => m.Length).ThenBy(m => m.Index))
            if (!chosen.Any(c => match.Index < c.Index + c.Length && c.Index < match.Index + match.Length)) chosen.Add(match);
        return [.. chosen.OrderBy(m => m.Index)];
    }

    /// <summary>
    /// Forms of this dictation that sound like a vocabulary term, for the AI editor as context-only forms of that
    /// term: deduplicated, lowercase, closest first, at most <see cref="MaxSoundHintsPerTerm"/> per term and
    /// <see cref="MaxSoundHints"/> in total, never a form the vocabulary already lists. Every hint is a word or short
    /// phrase already in the dictated text, so the editor receives no new text.
    /// </summary>
    public static Dictionary<string, List<string>> SoundHints(string text, IEnumerable<string> lines)
    {
        var hints = new Dictionary<string, List<string>>();
        var total = 0;
        foreach (var match in SoundMatches(text, lines).OrderBy(m => m.Distance).ThenBy(m => m.Index))
        {
            if (total >= MaxSoundHints) break;
            var forms = hints.TryGetValue(match.Term, out var list) ? list : hints[match.Term] = [];
            if (forms.Count >= MaxSoundHintsPerTerm || forms.Any(f => Key(f) == Key(match.Heard))) continue;
            forms.Add(match.Heard.ToLowerInvariant());
            total++;
        }
        foreach (var empty in hints.Where(h => h.Value.Count == 0).Select(h => h.Key).ToList()) hints.Remove(empty);
        return hints;
    }
}
