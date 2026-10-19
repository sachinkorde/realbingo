using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealBingo.Localization
{
    /// <summary>
    /// One language the game can run in. `Code` is the CSV column header and
    /// the value saved to PlayerPrefs, so it must never change once a build is
    /// out — a renamed code silently resets every player to English.
    ///
    /// `NativeName` is deliberately written in the language itself: someone who
    /// only reads Marathi cannot find their language in a list that says
    /// "Marathi".
    /// </summary>
    [Serializable]
    public class LanguageOption
    {
        public string code;
        public string englishName;
        public string nativeName;
        public bool needsIndicFont;

        public LanguageOption(string code, string englishName, string nativeName, bool needsIndicFont)
        {
            this.code = code;
            this.englishName = englishName;
            this.nativeName = nativeName;
            this.needsIndicFont = needsIndicFont;
        }
    }

    /// <summary>
    /// CSV-backed localization. No Unity Localization package.
    ///
    /// THE SHEET
    ///   Assets/Resources/Localization/strings.csv
    ///   Column 1 is the key; every column after it is one language, named by
    ///   its code in the header row:
    ///
    ///       key,en,hi,mr
    ///       common.ok,OK,ठीक है,ठीक आहे
    ///
    ///   Adding a language is a new COLUMN plus one line in SupportedLanguages
    ///   below — no code changes anywhere else. That is why all 22 scheduled
    ///   Indian languages are already declared: the sheet decides which of them
    ///   actually ship, and a language with no column simply never appears.
    ///
    /// USING IT
    ///       L.T("wallet.insufficient")                        // plain lookup
    ///       L.T("register.password_short", minPasswordLength) // with {0}, {1}…
    ///
    ///   Scene text does not call this directly — put a LocalizedText on the
    ///   TMP_Text instead and give it a key.
    ///
    /// MISSING KEYS never throw and never show blank. The order is:
    ///   current language -> English -> the key itself in [brackets].
    /// A visible [some.key] on screen is the intended failure: it is obvious in
    /// a screenshot, unlike an empty label, and it tells you exactly which row
    /// to add.
    /// </summary>
    public class LocalizationManager : MonoBehaviour
    {
        public const string PREF_KEY = "localization_language";
        public const string DefaultLanguage = "en";
        private const string CsvResourcePath = "Localization/strings";

        public static LocalizationManager Instance { get; private set; }

        /// <summary>Fires after the language changes. LocalizedText listens to this.</summary>
        public static event Action OnLanguageChanged;

        /// <summary>
        /// Every language the game is prepared to run in. Shipping one only
        /// requires a matching column in the CSV — see AvailableLanguages.
        /// </summary>
        public static readonly List<LanguageOption> SupportedLanguages = new List<LanguageOption>
        {
            new LanguageOption("en", "English",   "English",    false),
            new LanguageOption("hi", "Hindi",     "हिन्दी",       true),
            new LanguageOption("mr", "Marathi",   "मराठी",       true),
            // Declared and ready — each ships the moment the CSV gains its column.
            new LanguageOption("bn", "Bengali",   "বাংলা",        true),
            new LanguageOption("gu", "Gujarati",  "ગુજરાતી",      true),
            new LanguageOption("kn", "Kannada",   "ಕನ್ನಡ",        true),
            new LanguageOption("ml", "Malayalam", "മലയാളം",      true),
            new LanguageOption("or", "Odia",      "ଓଡ଼ିଆ",        true),
            new LanguageOption("pa", "Punjabi",   "ਪੰਜਾਬੀ",       true),
            new LanguageOption("ta", "Tamil",     "தமிழ்",        true),
            new LanguageOption("te", "Telugu",    "తెలుగు",       true),
            new LanguageOption("ur", "Urdu",      "اردو",         true),
            new LanguageOption("as", "Assamese",  "অসমীয়া",      true),
            new LanguageOption("ks", "Kashmiri",  "کٲشُر",        true),
            new LanguageOption("kok","Konkani",   "कोंकणी",       true),
            new LanguageOption("mai","Maithili",  "मैथिली",       true),
            new LanguageOption("mni","Manipuri",  "ꯃꯤꯇꯩꯂꯣꯟ",     true),
            new LanguageOption("ne", "Nepali",    "नेपाली",       true),
            new LanguageOption("sa", "Sanskrit",  "संस्कृतम्",     true),
            new LanguageOption("sat","Santali",   "ᱥᱟᱱᱛᱟᱲᱤ",      true),
            new LanguageOption("sd", "Sindhi",    "سنڌي",         true),
            new LanguageOption("doi","Dogri",     "डोगरी",        true),
            new LanguageOption("brx","Bodo",      "बड़ो",         true),
        };

        /// <summary>The language code in use right now (e.g. "hi").</summary>
        public static string CurrentLanguage { get; private set; } = DefaultLanguage;

        /// <summary>True once a language has been chosen — the popup checks this.</summary>
        public static bool HasChosenLanguage => PlayerPrefs.HasKey(PREF_KEY);

        /// <summary>
        /// True when the current language is written in an Indic script rather
        /// than Latin. Labels use it to thicken the weight: Devanagari packs far
        /// more strokes into the same em than Latin does, so at identical size
        /// and weight it reads thin and smudged on a phone screen.
        /// </summary>
        public static bool CurrentLanguageUsesIndicScript
        {
            get
            {
                LanguageOption o = Find(CurrentLanguage);
                return o != null && o.needsIndicFont;
            }
        }

        // key -> (languageCode -> text)
        private readonly Dictionary<string, Dictionary<string, string>> table =
            new Dictionary<string, Dictionary<string, string>>();

        // Language codes that actually have a column in the sheet.
        private readonly List<string> columnsInSheet = new List<string>();

        private bool loaded;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Before anything can be drawn: without a Devanagari-capable font in
            // TMP's fallback chain, every Hindi and Marathi string renders as
            // □□□□ no matter how correct the sheet is.
            DevanagariFallback.Ensure();

            // The shaper has to reach every label, not just the ones showing a
            // sheet row, so it goes in through TMP's own preprocessor hook.
            // Re-run on each scene load, since labels in the new scene have no
            // preprocessor of their own yet.
            InstallPreprocessor();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => InstallPreprocessor();

            Load();
            CurrentLanguage = PlayerPrefs.GetString(PREF_KEY, DefaultLanguage);

            // A code saved by an older build whose column has since been removed
            // would otherwise leave every string falling back to English with no
            // explanation. Snap back to the default and say so once.
            if (!columnsInSheet.Contains(CurrentLanguage))
            {
                if (HasChosenLanguage)
                    Debug.LogWarning($"[Localization] saved language '{CurrentLanguage}' has no column in the sheet — using '{DefaultLanguage}'.");
                CurrentLanguage = DefaultLanguage;
            }
        }

        /// <summary>
        /// Guarantees a manager exists even if no scene has one — a script that
        /// calls L.T() before the first scene is wired up still gets strings
        /// rather than a null reference.
        /// </summary>
        private static void InstallPreprocessor()
        {
            int n = DevanagariTextPreprocessor.InstallOnAllLabels();
            if (n > 0) Debug.Log($"[Localization] Devanagari shaper attached to {n} labels.");
        }

        public static LocalizationManager Ensure()
        {
            if (Instance != null) return Instance;

            var go = new GameObject("[LocalizationManager]");
            return go.AddComponent<LocalizationManager>();
        }

        // ── Loading ───────────────────────────────────────────────────────────

        private void Load()
        {
            if (loaded) return;
            loaded = true;

            TextAsset csv = Resources.Load<TextAsset>(CsvResourcePath);
            if (csv == null)
            {
                Debug.LogError($"[Localization] no sheet at Resources/{CsvResourcePath}.csv — every string will show as [key].");
                return;
            }

            List<string[]> rows = CsvTable.Parse(csv.text);
            if (rows.Count < 2)
            {
                Debug.LogError("[Localization] sheet has no data rows.");
                return;
            }

            // Header: key,en,hi,mr…
            //
            // A column whose name starts with "_" is a tool column, not a
            // language — "_match" holds the exact text a scene label currently
            // shows, which lets the editor tooling find the right row even when
            // the English has since been corrected. Skipping them here keeps
            // them out of the language picker.
            string[] header = rows[0];
            for (int col = 1; col < header.Length; col++)
            {
                string code = header[col].Trim();
                if (string.IsNullOrEmpty(code) || code.StartsWith("_")) continue;
                columnsInSheet.Add(code);
            }

            for (int r = 1; r < rows.Count; r++)
            {
                string[] row = rows[r];
                if (row.Length == 0) continue;

                string key = row[0].Trim();
                if (string.IsNullOrEmpty(key) || key.StartsWith("#")) continue;   // # = comment row

                var byLanguage = new Dictionary<string, string>();
                for (int col = 1; col < header.Length && col < row.Length; col++)
                {
                    string code = header[col].Trim();
                    if (string.IsNullOrEmpty(code) || code.StartsWith("_")) continue;

                    // An empty cell is "not translated yet", NOT "translate to
                    // empty" — storing it would defeat the English fallback and
                    // put a blank label on screen.
                    string value = row[col];
                    if (!string.IsNullOrEmpty(value)) byLanguage[code] = value;
                }

                table[key] = byLanguage;
            }

            Debug.Log($"[Localization] loaded {table.Count} keys, languages: {string.Join(", ", columnsInSheet)}");
        }

        // ── Lookup ────────────────────────────────────────────────────────────

        /// <summary>Translate a key into the current language.</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";

            if (table.TryGetValue(key, out var byLanguage))
            {
                // DevanagariShaper runs on the way OUT, never on the way in:
                // the sheet keeps proper logical order so it stays readable and
                // editable, and only what TMP is about to draw gets rearranged.
                if (byLanguage.TryGetValue(CurrentLanguage, out string text))
                    return DevanagariShaper.Fix(text);

                if (byLanguage.TryGetValue(DefaultLanguage, out string english))
                    return english;   // English needs no reordering
            }

            return $"[{key}]";
        }

        /// <summary>
        /// Translate, then fill {0}, {1}… Falls back to the unformatted string
        /// if the arguments don't match the placeholders, because a translator
        /// dropping a {0} must not crash a live build.
        /// </summary>
        public string Get(string key, params object[] args)
        {
            string text = Get(key);
            if (args == null || args.Length == 0) return text;

            try { return string.Format(text, args); }
            catch (FormatException)
            {
                Debug.LogWarning($"[Localization] '{key}' has placeholders that don't match the {args.Length} argument(s) given ({CurrentLanguage}).");
                return text;
            }
        }

        public bool HasKey(string key) => table.ContainsKey(key);

        /// <summary>Only the languages that actually have a column in the sheet.</summary>
        public static List<LanguageOption> AvailableLanguages()
        {
            var result = new List<LanguageOption>();
            LocalizationManager m = Instance;

            foreach (LanguageOption option in SupportedLanguages)
            {
                // Before the manager exists, offer only the default — the popup
                // will be rebuilt once it loads.
                if (m == null) { if (option.code == DefaultLanguage) result.Add(option); continue; }
                if (m.columnsInSheet.Contains(option.code)) result.Add(option);
            }

            return result;
        }

        public static LanguageOption Find(string code)
        {
            foreach (LanguageOption o in SupportedLanguages)
                if (o.code == code) return o;
            return null;
        }

        // ── Switching ─────────────────────────────────────────────────────────

        /// <summary>
        /// Switch language and refresh every LocalizedText in every loaded
        /// scene. Persists the choice, so the popup never asks again.
        /// </summary>
        public void SetLanguage(string code, bool persist = true)
        {
            if (string.IsNullOrEmpty(code)) return;

            if (!columnsInSheet.Contains(code))
            {
                Debug.LogWarning($"[Localization] '{code}' has no column in the sheet — ignoring.");
                return;
            }

            CurrentLanguage = code;

            if (persist)
            {
                PlayerPrefs.SetString(PREF_KEY, code);
                PlayerPrefs.Save();
            }

            OnLanguageChanged?.Invoke();
        }
    }

    /// <summary>
    /// Short alias so a call site reads as text, not as plumbing:
    ///     ShowPopup(L.T("login.enter_password"));
    /// </summary>
    public static class L
    {
        public static string T(string key)
        {
            LocalizationManager m = LocalizationManager.Instance;
            return m != null ? m.Get(key) : LocalizationManager.Ensure().Get(key);
        }

        public static string T(string key, params object[] args)
        {
            LocalizationManager m = LocalizationManager.Instance;
            return m != null ? m.Get(key, args) : LocalizationManager.Ensure().Get(key, args);
        }
    }
}
