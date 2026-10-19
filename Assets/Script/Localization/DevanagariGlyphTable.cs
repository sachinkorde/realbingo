using System.Collections.Generic;
using UnityEngine;

namespace RealBingo.Localization
{
    /// <summary>
    /// Maps Devanagari character sequences to the Private Use Area codepoints
    /// that draw them as a single correct glyph.
    ///
    /// BACKGROUND
    /// Noto Sans Devanagari contains a precomposed glyph for क्ष, for the half
    /// form of स, for reph, and for ~300 more. None of them has a Unicode
    /// codepoint — a shaping engine normally reaches them through GSUB, which
    /// TextMeshPro does not run. So the font was patched (see the editor tool)
    /// to give each one a PUA codepoint, which makes it an ordinary character
    /// TMP can look up, rasterize and cache like any other.
    ///
    /// This class is the other half: the lookup the shaper uses to decide which
    /// PUA character to emit for a given sequence.
    ///
    /// Data: Resources/Localization/devanagari_pua.txt
    ///     &lt;sequence hex&gt;\t&lt;PUA hex&gt;\t&lt;advance width&gt;
    /// </summary>
    public static class DevanagariGlyphTable
    {
        private const string ResourcePath = "Localization/devanagari_pua";

        /// <summary>र + virama. Zero advance, so it overlays the glyph before it.</summary>
        public const string RephSequence = "र्";

        private static Dictionary<string, char> bySequence;
        private static HashSet<char> halfForms;
        private static int longestSequence;
        private static bool loaded;

        /// <summary>PUA character for reph, or '\0' when the table has none.</summary>
        public static char Reph { get; private set; }

        /// <summary>Length of the longest sequence, so the matcher knows how far to look ahead.</summary>
        public static int LongestSequence { get { Load(); return longestSequence; } }

        public static bool IsAvailable { get { Load(); return bySequence != null && bySequence.Count > 0; } }

        /// <summary>True for a codepoint this table handed out — the shaper treats those as consonants.</summary>
        public static bool IsSubstituted(char c) => c >= '' && c <= '';

        /// <summary>
        /// True when this PUA glyph is a HALF FORM — a consonant whose sequence
        /// ended in a virama, such as स्. A half form does not stand alone: it
        /// leans on the consonant after it to make one written unit, so the
        /// cluster continues through it. Getting this wrong puts a following ि
        /// inside the cluster (स्[ि]थ) instead of in front of it ([ि]स्थ).
        /// </summary>
        public static bool IsHalfForm(char c)
        {
            Load();
            return halfForms != null && halfForms.Contains(c);
        }

        public static bool TryGet(string text, int start, int length, out char pua)
        {
            Load();
            pua = '\0';

            if (bySequence == null) return false;

            // Substring allocates; callers try longest-first and stop on the
            // first hit, so this runs a handful of times per label, not per frame.
            return bySequence.TryGetValue(text.Substring(start, length), out pua);
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[Localization] no glyph table at Resources/{ResourcePath}.txt — " +
                                 "conjuncts and reph will not be substituted (matras still are).");
                return;
            }

            bySequence = new Dictionary<string, char>(512);
            halfForms = new HashSet<char>();

            foreach (string line in asset.text.Split('\n'))
            {
                string row = line.Trim();
                if (row.Length == 0 || row[0] == '#') continue;

                string[] parts = row.Split('\t');
                if (parts.Length < 2) continue;

                string sequence = FromHex(parts[0]);
                if (string.IsNullOrEmpty(sequence)) continue;

                if (!int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber,
                                  System.Globalization.CultureInfo.InvariantCulture, out int cp))
                    continue;

                char pua = (char)cp;
                bySequence[sequence] = pua;

                // Ends in a virama = a half form, which joins the next consonant.
                // Reph also ends in one but is a mark, not a joining form, so it
                // is excluded or it would swallow the letter it sits above.
                if (sequence[sequence.Length - 1] == '्' && sequence != RephSequence)
                    halfForms.Add(pua);

                if (sequence.Length > longestSequence) longestSequence = sequence.Length;
                if (sequence == RephSequence) Reph = pua;
            }

            Debug.Log($"[Localization] glyph table: {bySequence.Count} sequences, " +
                      $"longest {longestSequence} chars, reph {(Reph != '\0' ? "found" : "MISSING")}.");
        }

        /// <summary>"0915094D0930" -> the three characters it names.</summary>
        private static string FromHex(string hex)
        {
            if (hex.Length < 4 || hex.Length % 4 != 0) return null;

            var chars = new char[hex.Length / 4];
            for (int i = 0; i < chars.Length; i++)
            {
                if (!int.TryParse(hex.Substring(i * 4, 4), System.Globalization.NumberStyles.HexNumber,
                                  System.Globalization.CultureInfo.InvariantCulture, out int cp))
                    return null;

                chars[i] = (char)cp;
            }

            return new string(chars);
        }
    }
}
