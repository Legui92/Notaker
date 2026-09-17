using System.Text;
using System.Text.RegularExpressions;

namespace Notaker;

public static partial class TextMetrics
{
    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{M}\p{N}]*(?:['’_-][\p{L}\p{N}][\p{L}\p{M}\p{N}]*)*", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
    private static string[] Tokens(string text) => Words().Matches(text.Normalize(NormalizationForm.FormC))
        .Select(m => m.Value.Replace('’', '\'').ToUpperInvariant()).ToArray();
    public static int WordCount(string text) => Words().Count(text);

    // Word-level edit distance: insertion, removal or replacement costs one.
    // Case and punctuation changes do not count as word corrections.
    public static int WordEdits(string before, string after)
    {
        var a = Tokens(before); var b = Tokens(after);
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
        var endA = a.Length; var endB = b.Length;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1]) { endA--; endB--; }
        var n = endA - start; var m = endB - start;
        if (n == 0 || m == 0) return Math.Max(n, m);
        var previous = Enumerable.Range(0, m + 1).ToArray(); var current = new int[m + 1];
        for (var i = 1; i <= n; i++)
        {
            current[0] = i;
            for (var j = 1; j <= m; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[start + i - 1] == b[start + j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[m];
    }
}
