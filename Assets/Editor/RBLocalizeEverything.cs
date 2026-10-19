using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RealBingo.Localization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu:  Real Bingo ▸ Localization ▸ ★ LOCALIZE EVERYTHING (both scenes)
///
/// The whole job in one command. It opens both scenes in turn and, in each:
///
///   1. makes sure a LocalizationManager exists (reuses the GameObject you
///      already added rather than making a second one)
///   2. finds Lang_Select_Popup, puts LanguageSelectPopup on it, and works out
///      its panel root / button template / container / confirm button
///   3. hands the popup to UIManagerMenu so it runs before the splash fades
///   4. puts a LocalizedText, with the right key, on every fixed UI label
///   5. puts a LocalizedDropdown on every TMP_Dropdown and keys its options
///   6. saves the scene
///
/// Keys are not invented. Each label's current text is matched against the
/// sheet (the _match column first, then en), so a label gets the key whose
/// Hindi and Marathi are already written. Anything with no row is reported by
/// name at the end rather than being given a made-up key.
///
/// WHAT IT DELIBERATELY LEAVES ALONE
///   Numbers, balances, the countdown, the winning number, a player's name,
///   whatever is typed into an input field — all written by code at runtime.
///   A LocalizedText there would overwrite a live value on the next language
///   change, so these are skipped and listed as "skipped" in the report.
///
/// Safe to run twice: existing components are re-keyed, not duplicated.
/// Everything is one undo step per scene, and the report prints to the Console.
/// </summary>
public static class RBLocalizeEverything
{
    private const string CsvPath = "Assets/Resources/Localization/strings.csv";
    private const string MainScene = "Assets/Scenes/Main.unity";
    private const string GameScene = "Assets/Scenes/GameScene.unity";
    // Matched loosely, not by exact name: the object in Main is actually called
    // "Lang_SelecrPopup". Requiring an exact spelling would silently skip the
    // whole popup step over a typo, so any name containing both "lang" and
    // "popup" (ignoring case, spaces and underscores) counts.
    private const string PopupObjectName = "Lang_Select_Popup";

    // The gender dropdown's options, in order. Keys already translated in the sheet.
    private static readonly string[] GenderKeys =
        { "common.select", "common.male", "common.female", "common.other" };

    [MenuItem("Real Bingo/Localization/★ LOCALIZE EVERYTHING (both scenes)", priority = 0)]
    public static void Run()
    {
        if (!File.Exists(CsvPath))
        {
            EditorUtility.DisplayDialog("No sheet", $"Expected the CSV at {CsvPath}.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[Localization] cancelled — nothing was changed.");
            return;
        }

        Dictionary<string, string> byText = BuildTextLookup();
        var report = new StringBuilder();
        report.AppendLine($"Sheet: {byText.Count} text→key entries loaded.\n");

        // Font first: it is project-wide, not per scene.
        RBDevanagariFontSetup.SetUp(showDialog: false);
        report.AppendLine("Devanagari font: built and added to the TMP fallback list.\n");

        foreach (string scenePath in new[] { MainScene, GameScene })
        {
            if (!File.Exists(scenePath)) { report.AppendLine($"── {scenePath}: NOT FOUND, skipped\n"); continue; }

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            report.AppendLine($"── {scene.name} ─────────────────────────────");

            EnsureManager(report);
            WirePopup(report);
            int[] counts = StampLabels(scene, byText, report);
            int dropdowns = StampDropdowns(scene, report);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            report.AppendLine($"   labels keyed : {counts[0]}");
            report.AppendLine($"   no CSV row   : {counts[1]}");
            report.AppendLine($"   skipped      : {counts[2]} (live values / input text / numbers)");
            report.AppendLine($"   dropdowns    : {dropdowns}");
            report.AppendLine("   scene saved.\n");
        }

        // Leave them where they started.
        EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);

        string text = report.ToString();
        Debug.Log("[Localization] LOCALIZE EVERYTHING:\n" + text);

        string logPath = "Assets/Resources/Localization/last_run_report.txt";
        File.WriteAllText(logPath, text, new UTF8Encoding(false));
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Localization complete",
            text + "\nThe full report is in the Console and in\n" + logPath, "OK");
    }

    // ── 1. Manager ────────────────────────────────────────────────────────────

    private static void EnsureManager(StringBuilder report)
    {
        var existing = Object.FindFirstObjectByType<LocalizationManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            report.AppendLine($"   manager      : reused '{existing.gameObject.name}'");
            return;
        }

        // Only Main needs one in the scene — it survives into GameScene via
        // DontDestroyOnLoad. Creating a second there would be destroyed on
        // arrival by the singleton guard, which is harmless but confusing.
        GameObject go = GameObject.Find("LocalizationManager") ?? new GameObject("LocalizationManager");
        Undo.RegisterCreatedObjectUndo(go, "LocalizationManager");
        Undo.AddComponent<LocalizationManager>(go);
        report.AppendLine("   manager      : created 'LocalizationManager'");
    }

    // ── 2+3. Language popup ───────────────────────────────────────────────────

    private static void WirePopup(StringBuilder report)
    {
        GameObject popupGO = FindInScene(PopupObjectName);
        if (popupGO == null)
        {
            // Only Main is expected to have it.
            if (EditorSceneManager.GetActiveScene().path == MainScene)
                report.AppendLine($"   popup        : no object named '{PopupObjectName}' — skipped");
            return;
        }

        var popup = popupGO.GetComponent<LanguageSelectPopup>();
        if (popup == null) popup = Undo.AddComponent<LanguageSelectPopup>(popupGO);

        // Work out the pieces from whatever hierarchy was built.
        Button[] buttons = popupGO.GetComponentsInChildren<Button>(true);

        Button confirm = buttons.FirstOrDefault(b => LooksLikeConfirm(b));
        Button template = buttons.FirstOrDefault(b => b != confirm &&
                                                      b.GetComponentInChildren<TMP_Text>(true) != null);

        var so = new SerializedObject(popup);
        SetRef(so, "panelRoot", popupGO);
        if (template != null)
        {
            SetRef(so, "buttonTemplate", template);
            SetRef(so, "buttonContainer", template.transform.parent);
        }
        SetRef(so, "confirmButton", confirm);
        so.ApplyModifiedProperties();

        report.AppendLine($"   popup        : '{popupGO.name}' wired");
        report.AppendLine($"      template  : {(template != null ? template.name : "NOT FOUND — set buttonTemplate by hand")}");
        report.AppendLine($"      container : {(template != null ? template.transform.parent.name : "-")}");
        report.AppendLine($"      confirm   : {(confirm != null ? confirm.name : "none (tapping a language selects immediately)")}");

        // Hand it to the startup sequence.
        var menu = Object.FindFirstObjectByType<UIManagerMenu>(FindObjectsInactive.Include);
        if (menu != null)
        {
            var mso = new SerializedObject(menu);
            SerializedProperty prop = mso.FindProperty("languageSelectPopup");
            if (prop != null)
            {
                prop.objectReferenceValue = popup;
                mso.ApplyModifiedProperties();
                report.AppendLine("      UIManagerMenu: assigned");
            }
        }
    }

    private static bool LooksLikeConfirm(Button b)
    {
        string n = b.gameObject.name.ToLowerInvariant();
        if (n.Contains("confirm") || n.Contains("continue") || n.Contains("proceed") ||
            n.Contains("ok") || n.Contains("done") || n.Contains("start")) return true;

        TMP_Text label = b.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return false;

        string t = (label.text ?? "").Trim().ToLowerInvariant();
        return t == "continue" || t == "confirm" || t == "ok" || t == "done" ||
               t == "proceed" || t == "start";
    }

    // ── 4. Labels ─────────────────────────────────────────────────────────────

    /// <summary>Returns { keyed, noRow, skipped }.</summary>
    private static int[] StampLabels(Scene scene, Dictionary<string, string> byText, StringBuilder report)
    {
        int keyed = 0, noRow = 0, skipped = 0;
        var unmatched = new List<string>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string text = label.text ?? "";

                if (SkipReason(label, text) != null) { skipped++; continue; }

                if (!byText.TryGetValue(Normalize(text), out string key))
                {
                    noRow++;
                    unmatched.Add($"{Path(label.transform)}  =  \"{Short(text)}\"");
                    continue;
                }

                var binder = label.GetComponent<LocalizedText>();
                if (binder == null) binder = Undo.AddComponent<LocalizedText>(label.gameObject);

                binder.EditorSetKey(key);
                EditorUtility.SetDirty(label.gameObject);
                keyed++;
            }
        }

        if (unmatched.Count > 0)
        {
            report.AppendLine($"   ── {unmatched.Count} labels have no row in the sheet:");
            foreach (string u in unmatched.Take(40)) report.AppendLine("      " + u);
            if (unmatched.Count > 40) report.AppendLine($"      … and {unmatched.Count - 40} more");
        }

        return new[] { keyed, noRow, skipped };
    }

    // ── 5. Dropdowns ──────────────────────────────────────────────────────────

    private static int StampDropdowns(Scene scene, StringBuilder report)
    {
        int done = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Dropdown dd in root.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                // Only the gender dropdown is known. Anything else gets reported
                // rather than guessed at — keying options by position is only
                // safe when the options are actually the ones expected.
                List<string> options = dd.options.Select(o => (o.text ?? "").Trim().ToLowerInvariant()).ToList();
                bool isGender = options.Count == 4 &&
                                options.Contains("male") && options.Contains("female");

                if (!isGender)
                {
                    report.AppendLine($"   dropdown '{dd.name}': options {string.Join("/", options)} — " +
                                      "not recognised, add a LocalizedDropdown by hand");
                    continue;
                }

                var binder = dd.GetComponent<LocalizedDropdown>();
                if (binder == null) binder = Undo.AddComponent<LocalizedDropdown>(dd.gameObject);

                // Key them in the dropdown's own order, not the array's.
                var keys = new List<string>();
                foreach (var opt in dd.options)
                {
                    string t = (opt.text ?? "").Trim().ToLowerInvariant();
                    keys.Add(t == "male" ? "common.male"
                           : t == "female" ? "common.female"
                           : t == "other" ? "common.other"
                           : "common.select");
                }

                binder.EditorSetKeys(keys);
                EditorUtility.SetDirty(dd.gameObject);
                done++;
            }
        }

        return done;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static Dictionary<string, string> BuildTextLookup()
    {
        var map = new Dictionary<string, string>();
        List<string[]> table = CsvTable.Parse(File.ReadAllText(CsvPath, Encoding.UTF8));
        if (table.Count < 2) return map;

        string[] header = table[0];
        int matchCol = -1;
        for (int c = 1; c < header.Length; c++)
            if (header[c].Trim() == "_match") { matchCol = c; break; }

        for (int r = 1; r < table.Count; r++)
        {
            string[] row = table[r];
            if (row.Length < 2) continue;

            string key = row[0].Trim();
            if (string.IsNullOrEmpty(key) || key.StartsWith("#")) continue;

            if (matchCol > 0 && matchCol < row.Length && !string.IsNullOrWhiteSpace(row[matchCol]))
                Register(map, row[matchCol], key);

            Register(map, row[1], key);
        }

        return map;
    }

    private static void Register(Dictionary<string, string> map, string text, string key)
    {
        string n = Normalize(text);
        if (!string.IsNullOrEmpty(n) && !map.ContainsKey(n)) map[n] = key;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        string t = text.Replace("\\n", "\n")
                       .Replace("\r\n", "\n")
                       .Replace('\u00A0', ' ')
                       .Trim()
                       .ToLowerInvariant();

        t = Regex.Replace(t, "[ \t]+", " ");

        // Fold a run of line breaks (with any spaces around them) down to one.
        // A multi-line scene label comes back from YAML with a blank line between
        // its halves while the sheet writes a single break, and the two have to
        // still compare equal or those labels never match.
        t = Regex.Replace(t, " *\n[ \n]*", "\n");

        return t;
    }

    /// <summary>Why this label must NOT get a binder. Null = safe to key.</summary>
    private static string SkipReason(TMP_Text label, string text)
    {
        string t = (text ?? "").Trim();

        if (string.IsNullOrEmpty(t)) return "empty";
        if (t == "​") return "zero-width spacer";
        if (Regex.IsMatch(t, @"^[\d\s.,:₹%+\-/]+$")) return "numeric / live value";
        if (t.Length > 120) return "long body text";

        // The text the player types. The placeholder beside it IS localizable.
        TMP_InputField field = label.GetComponentInParent<TMP_InputField>();
        if (field != null && field.textComponent == label) return "input field text";

        // A dropdown's visible label is driven by LocalizedDropdown instead —
        // binding it here would fight that and lose on the next selection.
        if (label.GetComponentInParent<TMP_Dropdown>() != null) return "dropdown label";

        return null;
    }

    /// <summary>
    /// Exact name first, then a loose match: lowercase with every non-letter
    /// stripped, needing both "lang" and "popup". That finds Lang_Select_Popup,
    /// Lang_SelecrPopup, LanguagePopup and so on, so a rename or a typo does
    /// not quietly skip the popup step.
    /// </summary>
    private static GameObject FindInScene(string name)
    {
        var all = new List<Transform>();
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            all.AddRange(root.GetComponentsInChildren<Transform>(true));

        foreach (Transform t in all)
            if (t.gameObject.name == name) return t.gameObject;

        foreach (Transform t in all)
        {
            string n = Regex.Replace(t.gameObject.name.ToLowerInvariant(), "[^a-z]", "");
            if (n.Contains("lang") && n.Contains("popup")) return t.gameObject;
        }

        return null;
    }

    private static void SetRef(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) p.objectReferenceValue = value;
    }

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        while (t != null) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string Short(string s)
    {
        s = (s ?? "").Replace("\n", "⏎").Trim();
        return s.Length <= 40 ? s : s.Substring(0, 40) + "…";
    }
}
