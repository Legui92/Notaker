using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Notaker;

/// <summary>A word or short phrase of the dictation that sounds like a vocabulary term.</summary>
/// <param name="Local">True when the heard words are not real Spanish or English words, so the term may replace them
/// locally. Real words ("baúl", "pilot", "cloud") are only hints for the AI editor.</param>
public sealed record SoundMatch(int Index, int Length, string Heard, string Term, int Distance, bool Local);

public static partial class PersonalVocabulary
{
    // A spoken word, with inner hyphens or apostrophes. Never part of a number, path, handle, file name or code.
    [GeneratedRegex(@"(?<![\p{L}\p{M}\p{N}_@#/\\.\-'’])[\p{L}\p{M}]+(?:[\-'’][\p{L}\p{M}]+)*(?![\p{L}\p{M}\p{N}_@#/\\\-'’])(?!\.[\p{L}\p{M}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex SpokenWords();

    private const int MaxSpanWords = 3;
    private const int MinSoundKey = 5;

    // Frequent Spanish and English words (OpenSubtitles 2018 frequency lists, CC BY-SA 4.0), lowercase without accents.
    private static readonly Lazy<HashSet<string>> KnownWords = new(() =>
    {
        using var stream = typeof(PersonalVocabulary).Assembly.GetManifestResourceStream("Notaker.known-words.txt")
            ?? throw new InvalidOperationException("Missing known-words resource");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var words = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line) if (line.Length > 0) words.Add(line);
        return words;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

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

    /// <summary>True for a real Spanish or English word, with or without accents. Hyphenated words count when each part does.</summary>
    public static bool IsKnownWord(string word)
    {
        var words = KnownWords.Value;
        var plain = Plain(word);
        if (plain.Length == 0) return false;
        if (words.Contains(plain)) return true;
        var parts = word.Split(['-', '\'', '’'], StringSplitOptions.RemoveEmptyEntries);
        // English clitics such as the "t" of "couldn't" are single letters.
        return parts.Length > 1 && parts.All(p => Plain(p) is var part && (part.Length == 1 || words.Contains(part)));
    }

    private static readonly (Regex Pattern, string With)[] SoundRules = [.. new[]
    {
        ("x", "ks"), ("sch", "sk"), ("ph", "f"), ("th", "t"), ("sh", "s"), ("ch", "C"), ("ck", "k"), ("qu", "k"), ("q", "k"),
        ("c(?=[eiy])", "s"), ("c", "k"), ("gu(?=[ei])", "g"), ("ee", "i"), ("oo", "u"), ("ea", "e"), ("y(?![aeiou])", "i"),
        ("h", ""), ("z", "s"), ("[vw]", "b"), ("b", "p"), ("d", "t"), ("g", "k"), ("(?<=[^aeiou])e$", ""), (@"(.)\1+", "$1"),
    }.Select(r => (new Regex(r.Item1, RegexOptions.CultureInvariant), r.Item2))];

    /// <summary>
    /// Rough sound key shared by Spanish and English spellings: silent h and final e dropped, b/v/w, p/b, t/d, k/g/c/q
    /// and s/z/soft c merged, English digraphs (th, ph, sh, ck, ee, oo, ea) read the way Whisper writes them in Spanish.
    /// "DeepSeek" and "Dipsick" share "tipsik"; "homelab" and "homelapse" differ in one sound.
    /// </summary>
    public static string SoundKey(string text)
    {
        var key = Plain(text);
        foreach (var (pattern, with) in SoundRules) key = pattern.Replace(key, with);
        return key;
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
    private sealed record SoundTable(SoundTarget[] Targets, HashSet<string> VocabularyKeys, string[] ContextSounds);
    private static (string Signature, SoundTable Table) soundCache = ("\0", new([], [], []));

    // Only written words of five sounds or more: no digits, spaces, symbols or all-capital acronyms ("eli5", "UNA",
    // "Mercado Libre", ".NET" and "Orca" are never targets). Every term, "=" form and "~" form is a protected spelling.
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
        // "~" forms are real words the user marked as ambiguous ("cloud" for Claude): what sounds like them is never local.
        var contextSounds = entries.SelectMany(e => e.Context).Select(SoundKey).Where(k => k.Length > 0).Distinct().ToArray();
        var table = new SoundTable(targets, keys, contextSounds);
        lock (CacheGate) soundCache = (signature, table);
        return table;
    }

    /// <summary>
    /// Words and short phrases (up to three words of three letters or more on one line, single spaces between them,
    /// no function words) that sound like a vocabulary term without being written like one. One sound edit is allowed for terms of five or six sounds,
    /// two from seven, and the first sound must agree. Never matched: vocabulary spellings and heard forms
    /// (including "~" forms such as cloud), inflections or derivations that start with the term ("backlogs",
    /// "commitear"), ties between two terms, and anything glued to digits or symbols. Matches made of real words
    /// or that sound at least as close to a "~" form ("Cloude" is as near cloud as Claude) are returned with Local = false.
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
                if (count > 1 && text.Substring(words[start + count - 2].Index + words[start + count - 2].Length, last.Index - words[start + count - 2].Index - words[start + count - 2].Length) != " ") break;
                var span = words.GetRange(start, count);
                if (span.Any(w => table.VocabularyKeys.Contains(Key(w.Value)))) break;
                // A phrase never absorbs short or function words ("baúl de" is not "vault").
                if (count > 1 && span.Any(w => w.Value.Length < 3 || Common.Contains(w.Value))) break;
                var heard = text.Substring(words[start].Index, last.Index + last.Length - words[start].Index);
                var letters = Key(heard);
                if (table.VocabularyKeys.Contains(letters) || span.Any(w => w.Value.Length > 1 && w.Value.All(char.IsUpper))) continue;
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
                var local = sound.Length >= MinSoundKey && span.All(w => !IsKnownWord(w.Value) && !Common.Contains(w.Value))
                    && !table.ContextSounds.Any(c => Distance(sound, c) <= bestDistance);
                found.Add(new SoundMatch(words[start].Index, heard.Length, heard, best.Term, bestDistance, local));
            }
        }
        // Closest first, then the longer phrase, then reading order; overlapping matches are dropped.
        var chosen = new List<SoundMatch>();
        foreach (var match in found.OrderBy(m => m.Distance).ThenByDescending(m => m.Length).ThenBy(m => m.Index))
            if (!chosen.Any(c => match.Index < c.Index + c.Length && c.Index < match.Index + match.Length)) chosen.Add(match);
        return [.. chosen.OrderBy(m => m.Index)];
    }

    /// <summary>Replaces the local sound matches. A lowercase term is capitalized only at the start of a sentence.</summary>
    private static string ReplaceSounds(string text, IList<string> lines, ref int replacements)
    {
        var matches = SoundMatches(text, lines).Where(m => m.Local).ToList();
        if (matches.Count == 0) return text;
        var result = new StringBuilder(text.Length);
        var position = 0;
        foreach (var match in matches)
        {
            result.Append(text, position, match.Index - position);
            var prefix = text[..match.Index].TrimEnd();
            var sentenceStart = prefix.Length == 0 || prefix[^1] is '.' or '!' or '?' or '¿' or '¡' or ':' or '\n';
            var term = match.Term.Any(char.IsUpper) || !sentenceStart || !char.IsUpper(match.Heard[0]) ? match.Term : char.ToUpperInvariant(match.Term[0]) + match.Term[1..];
            result.Append(term);
            position = match.Index + match.Length;
            replacements++;
        }
        return result.Append(text, position, text.Length - position).ToString();
    }

    /// <summary>
    /// Real words that sound like a vocabulary term, for the AI editor as context-only forms: at most three per term
    /// and ten in total, never a form the vocabulary already lists.
    /// </summary>
    public static Dictionary<string, List<string>> SoundHints(string text, IEnumerable<string> lines)
    {
        var hints = new Dictionary<string, List<string>>();
        var total = 0;
        foreach (var match in SoundMatches(text, lines).Where(m => !m.Local))
        {
            if (total >= 10) break;
            var forms = hints.TryGetValue(match.Term, out var list) ? list : hints[match.Term] = [];
            if (forms.Count >= 3 || forms.Any(f => Key(f) == Key(match.Heard))) continue;
            forms.Add(match.Heard.ToLowerInvariant());
            total++;
        }
        return hints;
    }
}
