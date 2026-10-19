using System.Text;

namespace RealBingo.Localization
{
    /// <summary>
    /// A minimal Devanagari shaper for TextMeshPro.
    ///
    /// TMP reads only the liga, kern, mark and mkmk OpenType features. Devanagari
    /// needs rphf, half, cjct, pres, psts and haln, plus syllable reordering —
    /// so TMP draws the stored codepoints as-is and the text comes out wrong in
    /// three different ways. This fixes all three, in the order they have to be
    /// applied:
    ///
    ///   1. REPH          र् at the head of a cluster is written as a hook ABOVE
    ///                    the end of that cluster. Emitted as a zero-advance PUA
    ///                    glyph after the consonant, so it overlays it.
    ///                    पासवर्ड — the one you reported.
    ///
    ///   2. CONJUNCTS     क् + ष is written as the single ligature क्ष, and स् is
    ///                    written as a narrow half-form. Both exist as real glyphs
    ///                    in the font; each is swapped for the PUA codepoint that
    ///                    reaches it. Longest match wins, so क्ष्र beats क्ष.
    ///
    ///   3. PRE-BASE ि    stored after its consonant, written before it — and
    ///                    before the WHOLE cluster, which is why this runs last,
    ///                    once clusters have collapsed into single characters.
    ///
    /// Order matters. Substituting first means step 3 sees one character where
    /// there were four, so the matra lands in front of the ligature rather than
    /// inside it.
    ///
    /// Display-only: the CSV keeps proper logical order, so it stays readable,
    /// searchable and editable by a translator. Only the string handed to TMP
    /// is rearranged.
    ///
    /// Set Enabled = false if you ever add a real shaper (HarfBuzz, IndicFlow,
    /// UniText). Running both would reorder twice and undo the result.
    /// </summary>
    public static class DevanagariShaper
    {
        public static bool Enabled = true;

        private const char PreBaseMatra = 'ि';   // ि
        private const char Halant = '्';         // ्
        private const char Nukta = '़';          // ़
        private const char Ra = 'र';             // र

        public static string Fix(string text)
        {
            if (!Enabled || string.IsNullOrEmpty(text)) return text;
            if (!ContainsDevanagari(text)) return text;

            string s = text;

            if (DevanagariGlyphTable.IsAvailable)
            {
                s = ApplyReph(s);
                s = ApplySubstitutions(s);
            }

            return ReorderPreBaseMatras(s);
        }

        // ── 1. Reph ───────────────────────────────────────────────────────────

        /// <summary>
        /// र् directly before a consonant is reph: it belongs on top of the
        /// cluster that follows. Rewritten as "cluster + rephGlyph" — the glyph
        /// has zero advance width, so it draws over what precedes it.
        ///
        /// Only when र् STARTS the cluster. Elsewhere (क् + र) it is rakar, a
        /// stroke below the letter, and a different glyph entirely — handled by
        /// the substitution pass, not here.
        /// </summary>
        private static string ApplyReph(string text)
        {
            char reph = DevanagariGlyphTable.Reph;
            if (reph == '\0' || text.IndexOf(Ra) < 0) return text;

            var sb = new StringBuilder(text.Length);
            int i = 0;

            while (i < text.Length)
            {
                bool isRephStart = text[i] == Ra &&
                                   i + 2 < text.Length &&
                                   text[i + 1] == Halant &&
                                   IsConsonant(text[i + 2]) &&
                                   !StartsCluster(text, i);

                if (!isRephStart) { sb.Append(text[i]); i++; continue; }

                i += 2;                       // step over र and ्
                int clusterStart = i;
                i = EndOfCluster(text, i);

                sb.Append(text, clusterStart, i - clusterStart);
                sb.Append(reph);
            }

            return sb.ToString();
        }

        /// <summary>
        /// True when the र at this index is itself the tail of a preceding
        /// cluster (as in क् + र), where it is rakar rather than reph.
        /// </summary>
        private static bool StartsCluster(string text, int i)
        {
            return i >= 1 && text[i - 1] == Halant;
        }

        // ── 2. Conjuncts and half forms ───────────────────────────────────────

        private static string ApplySubstitutions(string text)
        {
            int longest = DevanagariGlyphTable.LongestSequence;
            if (longest < 2) return text;

            var sb = new StringBuilder(text.Length);
            int i = 0;

            while (i < text.Length)
            {
                int max = System.Math.Min(longest, text.Length - i);
                bool matched = false;

                // Longest first: क्ष्र must win over क्ष, or the trailing र is
                // left stranded and renders as a separate letter.
                for (int len = max; len >= 2; len--)
                {
                    if (DevanagariGlyphTable.TryGet(text, i, len, out char pua))
                    {
                        sb.Append(pua);
                        i += len;
                        matched = true;
                        break;
                    }
                }

                if (!matched) { sb.Append(text[i]); i++; }
            }

            return sb.ToString();
        }

        // ── 3. Pre-base matra ─────────────────────────────────────────────────

        private static string ReorderPreBaseMatras(string text)
        {
            if (text.IndexOf(PreBaseMatra) < 0) return text;

            var sb = new StringBuilder(text.Length);
            int i = 0;

            while (i < text.Length)
            {
                if (!IsClusterStart(text[i])) { sb.Append(text[i]); i++; continue; }

                int start = i;
                i = EndOfCluster(text, i);

                if (i < text.Length && text[i] == PreBaseMatra)
                {
                    sb.Append(PreBaseMatra);
                    sb.Append(text, start, i - start);
                    i++;
                }
                else
                {
                    sb.Append(text, start, i - start);
                }
            }

            return sb.ToString();
        }

        // ── Shared ────────────────────────────────────────────────────────────

        /// <summary>End index of the cluster beginning at <paramref name="i"/>.</summary>
        private static int EndOfCluster(string text, int i)
        {
            if (i >= text.Length || !IsClusterStart(text[i])) return i;

            // Run through any half forms first. स्थ becomes [half-स][थ] after
            // substitution, and that is ONE written unit — stopping at the half
            // form would leave a following ि sitting inside it.
            while (i < text.Length && DevanagariGlyphTable.IsHalfForm(text[i])) i++;

            if (i >= text.Length) return i;
            if (!IsClusterStart(text[i])) return i;

            i++;
            if (i < text.Length && text[i] == Nukta) i++;

            while (i + 1 < text.Length && text[i] == Halant && IsConsonant(text[i + 1]))
            {
                i += 2;
                if (i < text.Length && text[i] == Nukta) i++;
            }

            return i;
        }

        /// <summary>
        /// A cluster can begin with a real consonant or with a PUA character the
        /// substitution pass produced — after that pass क्ष is one character, and
        /// a following ि still belongs in front of it.
        /// </summary>
        private static bool IsClusterStart(char c)
        {
            return IsConsonant(c) || DevanagariGlyphTable.IsSubstituted(c);
        }

        private static bool IsConsonant(char c)
        {
            return (c >= 'क' && c <= 'ह') ||
                   (c >= 'क़' && c <= 'य़') ||
                    c == 'ॹ' || c == 'ॺ';
        }

        /// <summary>Cheap guard so Latin strings skip every pass above.</summary>
        private static bool ContainsDevanagari(string text)
        {
            foreach (char c in text)
                if (c >= 'ऀ' && c <= 'ॿ') return true;

            return false;
        }
    }
}
