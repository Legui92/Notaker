using System.Text;
using System.Text.RegularExpressions;

namespace Notaker;

public static partial class DictationText
{
    // Braille artifacts are not spoken punctuation. Replace them with a boundary,
    // rather than joining adjacent words. Preserve accents, emoji and real symbols.
    public static string CleanArtifacts(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '\u2800' and <= '\u28FF') { output.Append(' '); continue; }
            if (c is '\u200B' or '\uFEFF' or '\u2060') continue;
            if (char.IsControl(c) && c is not ('\n' or '\r' or '\t')) continue;
            output.Append(c);
        }
        return HorizontalWhitespace().Replace(output.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }
    [GeneratedRegex(@"[^\S\r\n]+")]
    private static partial Regex HorizontalWhitespace();
}
