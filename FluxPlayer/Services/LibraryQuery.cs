using FluxPlayer.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FluxPlayer.Services;

public static partial class LibraryQuery
{
    public static Func<LibraryEntry, bool> Compile(string query)
    {
        var tests = new List<Func<LibraryEntry, bool>>();
        foreach (Match match in Tokens().Matches(query))
        {
            string term = match.Value; bool negate = term.StartsWith('-'); if (negate) term = term[1..];
            var colon = term.IndexOf(':'); string field = colon >= 0 ? term[..colon].ToLowerInvariant() : "";
            string value = (colon >= 0 ? term[(colon + 1)..] : term).Trim('"');
            bool Has(string? text) => text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
            // Legacy field aliases keep existing saved smart playlists compatible.
            Func<LibraryEntry, bool> test = field switch
            {
                "artist" or "artista" => e => Has(e.Artist),
                "album" or "álbum" => e => Has(e.Album),
                "genre" or "genero" or "género" => e => Has(e.Genre),
                "format" or "formato" => e => e.Format.Equals(value.TrimStart('.'), StringComparison.OrdinalIgnoreCase),
                "title" or "titulo" or "título" => e => Has(e.Title),
                "folder" or "carpeta" => e => Has(e.Folder),
                "duration" or "duracion" => e => Compare(e.DurationMs / 1000.0, value),
                "plays" or "veces" => e => Compare(e.PlayCount, value),
                "size" or "tamano" => e => Compare(e.Size / (1024.0 * 1024), value),
                "added" or "fecha" => e => DateMatches(e.AddedUtc, value),
                "favorite" or "favorito" => e => e.Favorite == (value is "true" or "1" or "sí" or "si"),
                _ => e => Has(e.Title) || Has(e.Artist) || Has(e.Album) || Has(e.Genre) || Has(e.Path)
            };
            tests.Add(negate ? e => !test(e) : test);
        }
        return entry => tests.All(test => test(entry));
    }

    private static bool Compare(double actual, string expression)
    {
        int length = expression.TakeWhile(c => c is '>' or '<' or '=').Count();
        string op = expression[..length], number = expression[length..];
        double expected;
        if (number.Contains(':'))
        {
            var parts = number.Split(':'); if (parts.Length != 2 || !int.TryParse(parts[0], out int minutes)
                || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out double seconds)) return false;
            expected = minutes * 60 + seconds;
        }
        else if (!double.TryParse(number, CultureInfo.InvariantCulture, out expected)) return false;
        return op switch { ">" => actual > expected, ">=" => actual >= expected, "<" => actual < expected, "<=" => actual <= expected, _ => actual == expected };
    }
    private static bool DateMatches(DateTime actual, string expression)
    {
        int length = expression.TakeWhile(c => c is '>' or '<' or '=').Count();
        if (!DateTime.TryParseExact(expression[length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return false;
        return expression[..length] switch { ">" => actual.Date > date.Date, ">=" => actual.Date >= date.Date, "<" => actual.Date < date.Date, "<=" => actual.Date <= date.Date, _ => actual.Date == date.Date };
    }
    [GeneratedRegex("-?(?:[^\\s\"]*:\"[^\"]*\"|\"[^\"]*\"|[^\\s]+)")]
    private static partial Regex Tokens();
}
