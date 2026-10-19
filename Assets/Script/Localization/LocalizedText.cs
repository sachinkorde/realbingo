using TMPro;
using UnityEngine;

namespace RealBingo.Localization
{
    /// <summary>
    /// Put this on any TMP_Text whose content is a fixed UI label, give it a
    /// key, and it keeps itself translated — on enable, and again every time
    /// the language changes.
    ///
    /// WHAT NOT TO PUT IT ON
    ///   Anything whose text is written by code every frame or every round:
    ///   the wallet balance, the countdown, the winning number, a player's
    ///   name. This component would overwrite that value on the next language
    ///   change. Those call L.T() at the point they build the string instead.
    ///
    /// FORMAT ARGUMENTS
    ///   A label like "You won {0}" needs a runtime value, so it cannot be set
    ///   from the Inspector alone. Call SetArgs(…) from code: the args are kept
    ///   and re-applied after a language switch, so the label survives it.
    /// </summary>
    [AddComponentMenu("Real Bingo/Localized Text")]
    [RequireComponent(typeof(TMP_Text))]
    [DisallowMultipleComponent]
    public class LocalizedText : MonoBehaviour
    {
        [Tooltip("Row in Resources/Localization/strings.csv, e.g. login.password")]
        [SerializeField] private string key;

        [Tooltip("Applies the language's font override from the font table, if one is assigned there. " +
                 "Leave on unless this label must keep its own typeface.")]
        [SerializeField] private bool applyLanguageFont = true;

        [Tooltip("Optional per-language fonts. Usually unnecessary: adding a Devanagari font to " +
                 "TMP Settings' global fallback list covers every label at once. See the setup note " +
                 "in LocalizationFontTable.")]
        [SerializeField] private LocalizationFontTable fontTable;

        [Tooltip("Draws Hindi / Marathi in bold. Devanagari has far more strokes per character " +
                 "than Latin, so at the same size and weight it reads thinner and muddier on a " +
                 "phone; bold restores the contrast. English is left at the label's own weight.")]
        [SerializeField] private bool boldForIndicScripts = true;

        // The weight the label was designed with, so switching back to English
        // restores it instead of leaving every label permanently bold.
        private FontStyles baseStyle;
        private bool baseStyleCaptured;

        private TMP_Text label;
        private object[] args;

        /// <summary>The CSV row this label shows. Setting it refreshes immediately.</summary>
        public string Key
        {
            get => key;
            set { key = value; Refresh(); }
        }

        private void Awake()
        {
            label = GetComponent<TMP_Text>();

            // Make sure a manager exists even when this label is the first
            // thing to wake up in the scene.
            LocalizationManager.Ensure();
        }

        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= Refresh;
        }

        /// <summary>
        /// Supply the runtime values for a key containing {0}, {1}… They are
        /// remembered, so the label re-formats correctly after a language
        /// change rather than reverting to the raw "{0}" form.
        /// </summary>
        public void SetArgs(params object[] formatArgs)
        {
            args = formatArgs;
            Refresh();
        }

        public void Refresh()
        {
            if (label == null) label = GetComponent<TMP_Text>();
            if (label == null || string.IsNullOrEmpty(key)) return;

            label.text = (args != null && args.Length > 0) ? L.T(key, args) : L.T(key);

            if (applyLanguageFont && fontTable != null)
            {
                TMP_FontAsset font = fontTable.FontFor(LocalizationManager.CurrentLanguage);
                if (font != null) label.font = font;
            }

            if (boldForIndicScripts)
            {
                if (!baseStyleCaptured) { baseStyle = label.fontStyle; baseStyleCaptured = true; }

                label.fontStyle = LocalizationManager.CurrentLanguageUsesIndicScript
                    ? baseStyle | FontStyles.Bold
                    : baseStyle;
            }
        }

#if UNITY_EDITOR
        /// <summary>Lets the editor tooling stamp a key without going through the property.</summary>
        public void EditorSetKey(string newKey) => key = newKey;

        /// <summary>Used by the scanner to seed a CSV row from what the label already says.</summary>
        public string EditorCurrentText()
        {
            TMP_Text t = GetComponent<TMP_Text>();
            return t != null ? t.text : "";
        }
#endif
    }
}
