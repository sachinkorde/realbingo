using System.Collections.Generic;
using System.Text;

namespace RealBingo.Localization
{
    /// <summary>
    /// A minimal RFC-4180 CSV reader, written for the localization sheet.
    ///
    /// Unity's JsonUtility has no CSV equivalent and String.Split(',') is wrong
    /// the moment a translation contains a comma — which, in Hindi and Marathi,
    /// is most sentences of any length. This handles the three things that
    /// actually break a naive split:
    ///
    ///   "a,b"      a comma INSIDE a quoted field is text, not a separator
    ///   "say ""hi"""   a doubled quote inside a quoted field is one quote
    ///   "line1\nline2" a newline INSIDE a quoted field does not end the row
    ///
    /// Everything is parsed in one pass over the raw string, so a 1,000-row
    /// sheet costs one allocation per cell and nothing else.
    /// </summary>
    public static class CsvTable
    {
        /// <summary>
        /// Splits raw CSV text into rows of fields. Blank lines are dropped.
        /// Handles \n, \r\n and a leading UTF-8 BOM (Excel writes one, and it
        /// would otherwise become part of the very first key — a bug that only
        /// shows up as "the first string never translates").
        /// </summary>
        public static List<string[]> Parse(string raw)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(raw)) return rows;

            if (raw[0] == '﻿') raw = raw.Substring(1);

            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            bool rowHasContent = false;

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // "" inside a quoted field = one literal quote.
                        if (i + 1 < raw.Length && raw[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;

                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        rowHasContent = true;
                        break;

                    case '\r':
                        break; // \r\n — the \n does the work

                    case '\n':
                        fields.Add(field.ToString());
                        field.Clear();
                        if (rowHasContent || HasAnyContent(fields)) rows.Add(fields.ToArray());
                        fields.Clear();
                        rowHasContent = false;
                        break;

                    default:
                        field.Append(c);
                        break;
                }
            }

            // Last row, when the file does not end in a newline.
            fields.Add(field.ToString());
            if (HasAnyContent(fields)) rows.Add(fields.ToArray());

            return rows;
        }

        private static bool HasAnyContent(List<string> fields)
        {
            foreach (string f in fields)
                if (!string.IsNullOrWhiteSpace(f)) return true;
            return false;
        }

        /// <summary>
        /// Quotes one value for writing back out. Only wraps when it has to,
        /// so the sheet stays readable in a text diff.
        /// </summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            bool needsQuotes = value.IndexOf(',') >= 0 ||
                               value.IndexOf('"') >= 0 ||
                               value.IndexOf('\n') >= 0 ||
                               value.IndexOf('\r') >= 0;

            if (!needsQuotes) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
