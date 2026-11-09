using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// The trail search, letter for letter the website's fold/matches
/// (web/src/lib/work.ts), so a name found on the site is found in the app.
///
/// Folding makes text comparable: lower case, Cyrillic written in Latin
/// letters (Bulgarian official transliteration), no accents, single spaces.
/// "Ямбол" and "yambol" fold to the same thing.
/// </summary>
public static class TrailFilter
{
    static readonly Dictionary<char, string> Cyrillic = new Dictionary<char, string>
    {
        { 'а', "a" }, { 'б', "b" }, { 'в', "v" }, { 'г', "g" }, { 'д', "d" }, { 'е', "e" },
        { 'ж', "zh" }, { 'з', "z" }, { 'и', "i" }, { 'й', "y" }, { 'к', "k" }, { 'л', "l" },
        { 'м', "m" }, { 'н', "n" }, { 'о', "o" }, { 'п', "p" }, { 'р', "r" }, { 'с', "s" },
        { 'т', "t" }, { 'у', "u" }, { 'ф', "f" }, { 'х', "h" }, { 'ц', "ts" }, { 'ч', "ch" },
        { 'ш', "sh" }, { 'щ', "sht" }, { 'ъ', "a" }, { 'ь', "" }, { 'ю', "yu" }, { 'я', "ya" },
    };

    /// <summary>The categories in the order the pickers and the website show them. Category
    /// icons are kept in this order too.</summary>
    public static readonly string[] Categories = { "education", "tourism", "estate", "other" };

    /// <summary>Where a category sits in <see cref="Categories"/>. Blank or
    /// unknown counts as "other", as the database treats it.</summary>
    public static int CategoryIndex(string category)
    {
        int i = System.Array.IndexOf(Categories, category);
        return i >= 0 ? i : Categories.Length - 1;
    }

    static readonly Regex Spaces = new Regex(@"\s+");

    public static string Fold(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Cyrillic first: stripping accents first would turn й into и.
        string lower = text.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        StringBuilder latin = new StringBuilder(lower.Length + 8);

        foreach (char ch in lower)
        {
            string mapped;
            if (Cyrillic.TryGetValue(ch, out mapped))
                latin.Append(mapped);
            else
                latin.Append(ch);
        }

        string decomposed = latin.ToString().Normalize(NormalizationForm.FormD);
        StringBuilder bare = new StringBuilder(decomposed.Length);

        foreach (char ch in decomposed)
        {
            if (ch < '\u0300' || ch > '\u036f')
                bare.Append(ch);
        }

        return Spaces.Replace(bare.ToString(), " ").Trim();
    }

    /// <summary>True when every word of the query appears somewhere in the
    /// trail's name or city. An empty query matches everything.</summary>
    public static bool Matches(string query, string name, string cityBg, string cityEn)
    {
        string hay = Fold((name ?? string.Empty) + " " + (cityBg ?? string.Empty) + " " +
                          (cityEn ?? string.Empty));

        string[] words = Fold(query).Split(' ');

        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0 && hay.IndexOf(words[i], System.StringComparison.Ordinal) < 0)
                return false;
        }

        return true;
    }
}
