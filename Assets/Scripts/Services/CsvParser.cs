using System.Collections.Generic;
using System.Text;

/// <summary>
/// RFC 4180 CSV reader.
///
/// Naive splitting on commas destroys this data: building descriptions contain
/// commas, quotation marks and occasionally line breaks, all of which are legal
/// inside a quoted field. This handles those properly.
///
/// Rows come back keyed by column header rather than by index, so columns can be
/// added, removed or reordered in the sheet without breaking the app.
/// </summary>
public static class CsvParser
{
    public static List<Dictionary<string, string>> Parse(string text)
    {
        var rows = new List<Dictionary<string, string>>();

        if (string.IsNullOrWhiteSpace(text))
            return rows;

        List<List<string>> raw = Tokenize(text);
        if (raw.Count < 2)
            return rows;

        List<string> headers = raw[0];
        for (int i = 0; i < headers.Count; i++)
            headers[i] = headers[i].Trim().ToLowerInvariant();

        for (int r = 1; r < raw.Count; r++)
        {
            List<string> cells = raw[r];

            // A trailing newline produces one empty row; skip it rather than
            // creating a building with no fields.
            if (cells.Count == 1 && string.IsNullOrWhiteSpace(cells[0]))
                continue;

            var row = new Dictionary<string, string>();
            for (int c = 0; c < headers.Count; c++)
                row[headers[c]] = c < cells.Count ? cells[c].Trim() : string.Empty;

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Splits the text into rows of fields, honouring quoted sections.</summary>
    static List<List<string>> Tokenize(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();

        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c != '"')
                {
                    field.Append(c);
                    continue;
                }

                // A doubled quote inside a quoted field is a literal quote.
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }

                inQuotes = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;

                case ',':
                    row.Add(field.ToString());
                    field.Length = 0;
                    break;

                case '\r':
                    // Swallow it; the \n that follows ends the row.
                    break;

                case '\n':
                    row.Add(field.ToString());
                    field.Length = 0;
                    rows.Add(row);
                    row = new List<string>();
                    break;

                default:
                    field.Append(c);
                    break;
            }
        }

        // Whatever is left when the text runs out is the last field of the last row.
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
