using TMPro;
using UnityEngine;

namespace RealBingo.Localization
{
    /// <summary>
    /// Hooks the shaper into TextMeshPro's own pipeline.
    ///
    /// TMP calls ITextPreprocessor.PreprocessText on whatever a label is about
    /// to draw, before parsing and layout. That is the right place for this:
    /// the shaper runs on EVERY string a label shows, not just the ones that
    /// came from the localization sheet.
    ///
    /// That distinction matters. Doing the reordering inside
    /// LocalizationManager.Get covered CSV rows and nothing else — a wallet
    /// balance built with string interpolation, a server error message, a
    /// player's name, the language picker's own "हिन्दी", an input field's
    /// placeholder set from code: all of those bypassed it and rendered wrong.
    /// Here, there is no path around it.
    ///
    /// TMP's own author pointed at manual reordering as the workaround for
    /// Devanagari, since TMP applies no Indic shaping; this is that workaround
    /// put where TMP expects it rather than bolted onto one call site.
    ///
    /// Installed by InstallOnAllLabels, which LocalizationManager calls at
    /// startup and after every scene load.
    /// </summary>
    public class DevanagariTextPreprocessor : ITextPreprocessor
    {
        /// <summary>One instance is enough — it holds no per-label state.</summary>
        public static readonly DevanagariTextPreprocessor Instance = new DevanagariTextPreprocessor();

        public string PreprocessText(string text) => DevanagariShaper.Fix(text);

        /// <summary>
        /// Attaches the preprocessor to every TMP_Text currently loaded.
        ///
        /// Labels created later need it too, which is why this runs again after
        /// each scene load rather than only once. Re-running is cheap and
        /// idempotent: a label that already has it is skipped.
        ///
        /// A label with its own preprocessor is left alone — something else is
        /// deliberately transforming its text and quietly replacing that would
        /// be worse than leaving one label unshaped.
        /// </summary>
        public static int InstallOnAllLabels()
        {
            TMP_Text[] all = Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int installed = 0;

            foreach (TMP_Text label in all)
            {
                if (label == null) continue;
                if (label.textPreprocessor != null) continue;

                label.textPreprocessor = Instance;

                // The label may already be showing unshaped text. TMP caches the
                // parsed result, so forceTextReparsing is what makes it run the
                // preprocessor again rather than redrawing the old glyphs.
                label.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);

                installed++;
            }

            return installed;
        }
    }
}
