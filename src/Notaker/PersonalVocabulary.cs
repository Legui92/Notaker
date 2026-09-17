using System.Text.RegularExpressions;

namespace Notaker;

public static partial class PersonalVocabulary
{
    private static readonly HashSet<string> Common = new(("para por pero como que porque cuando donde desde hasta sobre entre esta este esto eso esa ese " +
        "las los una unos unas del con sin son ser soy era fue hay muy más mas menos mañana hoy ayer tengo tiene " +
        "and the this that these those with from have has had was were will would could should not for you your are " +
        "can please gracias favor también tambien entonces necesito quiero hola enviar documento reunión reunion").Split(' '), StringComparer.OrdinalIgnoreCase);
    [GeneratedRegex(@"[\p{L}][\p{L}\p{M}\p{N}'’_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
    public static List<string> Normalize(IEnumerable<string> terms) => terms
        .Where(t => !string.IsNullOrWhiteSpace(t))
        .Select(t => t.Trim()).Where(t => t.Length is >= 2 and <= 80 && !t.Any(char.IsControl))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
    public static IEnumerable<string> Learn(string original, string corrected)
    {
        var oldWords = Words().Matches(original).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Words().Matches(corrected).Select(m => m.Value)
            .Where(w => w.Length >= 3 && !oldWords.Contains(w) && !Common.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10);
    }
    public static string Prompt(IEnumerable<string> terms)
    {
        var result = new List<string>();
        var length = 0;
        foreach (var term in Normalize(terms))
        {
            if (length + term.Length + 2 > 600) break;
            result.Add(term); length += term.Length + 2;
        }
        return string.Join(", ", result);
    }
}
