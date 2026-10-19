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

/// <summary>
/// Window:  Real Bingo ▸ Localization ▸ Localization Tools
///
/// The CSV and the C# side were written by hand; the ~100 TMP_Text labels sitting
/// in Main.unity and GameScene.unity were not, and adding a LocalizedText to each
/// one by hand is where a localization pass normally dies. This does that part.
///
///   SCAN      lists every TMP_Text in the open scene, what it says, whether it
///             already has a LocalizedText, and the key it would be given.
///   STAMP     adds LocalizedText to the selected labels and fills in the key.
///   EXPORT    appends any key that is missing from strings.csv, with the label's
///             current English text in the en column and the other columns blank.
///   AUDIT     reports keys used in code but absent from the sheet, keys in the
///             sheet nothing references, and any untranslated cell.
///
/// NOTHING IS AUTOMATIC. Stamping edits your scene, so it only ever touches rows
/// you have ticked, and it skips anything that looks like live data (a number, a
/// balance, a placeholder name) because those are written by code at runtime and
/// a LocalizedText would fight with it.
/// </summary>
public class RBLocalizationTools : EditorWindow
{
    private const string CsvPath = "Assets/Resources/Localization/strings.csv";

    private class Row
    {
        public TMP_Text label;
        public string currentText;
        public string key;
        public bool hasBinder;
        public bool selected;
        public string skipReason;
        public bool fromSheet;      // key came from the CSV, not invented here
    }

    private readonly List<Row> rows = new List<Row>();
    private Vector2 scroll;
    private string keyPrefix = "ui";
    private bool hideAlreadyBound = true;
    private bool hideSkipped = true;
    private string auditReport = "";

    [MenuItem("Real Bingo/Localization/Localization Tools")]
    private static void Open()
    {
        GetWindow<RBLocalizationTools>(false, "Localization").minSize = new Vector2(760, 520);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Open the scene you want to localize (Main or GameScene), then Scan.\n\n" +
            "Scan matches each label's text against the sheet, so most rows come back " +
            "green '✓ translated' — that key already has Hindi and Marathi written. " +
            "Orange 'NEW' means no row exists yet; Export adds one with the English filled " +
            "in and the translations blank.\n\n" +
            "Stamping edits the open scene. It is undoable, but save deliberately.",
            MessageType.Info);

        if (rows.Count > 0)
        {
            int matched = rows.Count(r => r.fromSheet && r.skipReason == null);
            int fresh = rows.Count(r => !r.fromSheet && r.skipReason == null && !r.hasBinder);
            EditorGUILayout.LabelField($"{matched} already translated   •   {fresh} new   •   " +
                                       $"{rows.Count(r => r.skipReason != null)} skipped",
                                       EditorStyles.boldLabel);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            keyPrefix = EditorGUILayout.TextField("Key prefix", keyPrefix);
            if (GUILayout.Button("Scan open scene", GUILayout.Width(140))) Scan();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            hideAlreadyBound = EditorGUILayout.ToggleLeft("Hide already bound", hideAlreadyBound, GUILayout.Width(160));
            hideSkipped = EditorGUILayout.ToggleLeft("Hide skipped (numbers / live data)", hideSkipped, GUILayout.Width(250));
            if (GUILayout.Button("All", GUILayout.Width(40))) SetAll(true);
            if (GUILayout.Button("None", GUILayout.Width(50))) SetAll(false);
        }

        EditorGUILayout.Space();
        DrawRows();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            int n = rows.Count(r => r.selected);
            using (new EditorGUI.DisabledScope(n == 0))
            {
                if (GUILayout.Button($"Stamp LocalizedText on {n} selected"))
                    Stamp();
                if (GUILayout.Button($"Export {n} selected to CSV"))
                    ExportToCsv();
            }
            if (GUILayout.Button("Audit keys", GUILayout.Width(100))) Audit();
        }

        if (!string.IsNullOrEmpty(auditReport))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Audit", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(auditReport, GUILayout.Height(160));
        }
    }

    private void DrawRows()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));

        foreach (Row r in rows)
        {
            if (hideAlreadyBound && r.hasBinder) continue;
            if (hideSkipped && r.skipReason != null) continue;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(r.skipReason != null || r.hasBinder))
                    r.selected = EditorGUILayout.Toggle(r.selected, GUILayout.Width(18));

                if (GUILayout.Button("→", GUILayout.Width(24)))
                {
                    Selection.activeGameObject = r.label.gameObject;
                    EditorGUIUtility.PingObject(r.label.gameObject);
                }

                EditorGUILayout.LabelField(Short(r.currentText, 34), GUILayout.Width(230));
                r.key = EditorGUILayout.TextField(r.key, GUILayout.Width(220));

                // "translated" is the one worth seeing at a glance: that key
                // already has Hindi and Marathi waiting in the sheet. "NEW" has
                // no row yet, so Export will add one with blank translations.
                string note = r.hasBinder ? "already bound"
                            : r.skipReason != null ? r.skipReason
                            : r.fromSheet ? "✓ translated" : "NEW — needs translating";

                Color was = GUI.color;
                if (r.skipReason == null && !r.hasBinder)
                    GUI.color = r.fromSheet ? new Color(0.45f, 0.85f, 0.5f) : new Color(1f, 0.78f, 0.35f);
                EditorGUILayout.LabelField(note, EditorStyles.miniLabel);
                GUI.color = was;
            }
        }

        EditorGUILayout.EndScrollView();
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    private void Scan()
    {
        rows.Clear();
        auditReport = "";

        // Text -> key, built from the sheet. The sheet already contains a row
        // for every label that was in these scenes when it was written, with
        // the Hindi and Marathi already filled in, so matching on text gives
        // each label its REAL translated key instead of an invented slug.
        Dictionary<string, string> byText = BuildTextLookup();

        Scene scene = EditorSceneManager.GetActiveScene();
        var seenKeys = new HashSet<string>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string text = label.text ?? "";

                var row = new Row
                {
                    label = label,
                    currentText = text,
                    hasBinder = label.GetComponent<LocalizedText>() != null,
                    skipReason = SkipReason(label, text),
                };

                if (byText.TryGetValue(Normalize(text), out string sheetKey))
                {
                    row.key = sheetKey;
                    row.fromSheet = true;
                    seenKeys.Add(sheetKey);
                }
                else
                {
                    row.key = MakeKey(keyPrefix, text, label.gameObject.name, seenKeys);
                }

                row.selected = row.skipReason == null && !row.hasBinder;
                rows.Add(row);
            }
        }

        rows.Sort((a, b) => string.CompareOrdinal(a.key, b.key));

        int ready = rows.Count(r => r.skipReason == null && !r.hasBinder);
        int matched = rows.Count(r => r.fromSheet && r.skipReason == null);
        Debug.Log($"[Localization] scanned {scene.name}: {rows.Count} TMP_Text, {ready} ready to stamp, " +
                  $"{matched} matched to an existing translated row in the sheet.");
    }

    /// <summary>
    /// Builds "what the label says" -> key from the sheet. A row's _match column
    /// wins when it has one: that is the exact text the scene shows today, which
    /// may differ from en where the English has been corrected (the scenes say
    /// "Chage Password"; en says "Change Password").
    /// </summary>
    private static Dictionary<string, string> BuildTextLookup()
    {
        var map = new Dictionary<string, string>();
        if (!File.Exists(CsvPath)) return map;

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

            // _match first, then en. Both are registered where they differ, so a
            // label still resolves after someone fixes the typo in the scene.
            if (matchCol > 0 && matchCol < row.Length && !string.IsNullOrWhiteSpace(row[matchCol]))
                Register(map, row[matchCol], key);

            Register(map, row[1], key);
        }

        return map;
    }

    /// <summary>
    /// First row to claim a piece of text wins. Later duplicates are skipped
    /// rather than overwriting — otherwise "Cancel" appearing in both halves of
    /// the sheet would make which key you get depend on row order.
    /// </summary>
    private static void Register(Dictionary<string, string> map, string text, string key)
    {
        string norm = Normalize(text);
        if (string.IsNullOrEmpty(norm)) return;
        if (!map.ContainsKey(norm)) map[norm] = key;
    }

    /// <summary>
    /// Compares label text forgivingly: case, surrounding whitespace, doubled
    /// inner spaces ("Enter  Password") and the literal "\n" some scene strings
    /// contain are all differences that should still match.
    /// </summary>
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

    /// <summary>
    /// Why a label should NOT get a binder. Everything here is text that code
    /// owns at runtime — stamping it would mean the next language change
    /// overwrites a live balance or the countdown with a static string.
    /// </summary>
    private static string SkipReason(TMP_Text label, string text)
    {
        string t = (text ?? "").Trim();

        if (string.IsNullOrEmpty(t)) return "empty";
        if (t == "​") return "zero-width spacer";
        if (Regex.IsMatch(t, @"^[\d\s.,:₹%+\-/]+$")) return "numeric / live value";
        if (t.Length > 120) return "long body text — key it by hand";

        // A TMP_InputField's own text object is driven by what the player types.
        if (label.GetComponentInParent<TMP_InputField>() != null)
        {
            TMP_InputField field = label.GetComponentInParent<TMP_InputField>();
            if (field.textComponent == label) return "input field's typed text";
            // The placeholder IS localizable — fall through.
        }

        return null;
    }

    /// <summary>
    /// Build "prefix.some_label" from the English text, falling back to the
    /// GameObject name when the text is unusable. Deduplicates with _2, _3…
    /// </summary>
    private static string MakeKey(string prefix, string text, string objectName, HashSet<string> seen)
    {
        string source = string.IsNullOrWhiteSpace(text) ? objectName : text;

        string slug = Regex.Replace(source.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        if (slug.Length > 40) slug = slug.Substring(0, 40).TrimEnd('_');
        if (string.IsNullOrEmpty(slug)) slug = "label";

        string key = $"{prefix}.{slug}";
        string candidate = key;
        int n = 2;
        while (!seen.Add(candidate)) candidate = $"{key}_{n++}";

        return candidate;
    }

    private void SetAll(bool value)
    {
        foreach (Row r in rows)
            if (r.skipReason == null && !r.hasBinder) r.selected = value;
    }

    // ── Stamp ─────────────────────────────────────────────────────────────────

    private void Stamp()
    {
        List<Row> picked = rows.Where(r => r.selected && !r.hasBinder && r.skipReason == null).ToList();
        if (picked.Count == 0) return;

        Undo.SetCurrentGroupName("Add LocalizedText");
        int group = Undo.GetCurrentGroup();

        foreach (Row r in picked)
        {
            var binder = Undo.AddComponent<LocalizedText>(r.label.gameObject);
            binder.EditorSetKey(r.key);
            EditorUtility.SetDirty(r.label.gameObject);
            r.hasBinder = true;
            r.selected = false;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"[Localization] stamped {picked.Count} labels. Scene is dirty — save when happy.");
    }

    // ── CSV ───────────────────────────────────────────────────────────────────

    private void ExportToCsv()
    {
        if (!File.Exists(CsvPath))
        {
            Debug.LogError($"[Localization] no sheet at {CsvPath}");
            return;
        }

        string[] lines = File.ReadAllLines(CsvPath, Encoding.UTF8);
        int columns = CsvTable.Parse(lines[0]).FirstOrDefault()?.Length ?? 4;

        var existing = new HashSet<string>(
            lines.Where(l => !l.StartsWith("#"))
                 .Select(l => CsvTable.Parse(l).FirstOrDefault()?.FirstOrDefault()?.Trim())
                 .Where(k => !string.IsNullOrEmpty(k)));

        var added = new List<string>();

        foreach (Row r in rows.Where(r => r.selected || r.hasBinder))
        {
            if (string.IsNullOrEmpty(r.key) || existing.Contains(r.key)) continue;

            // key, en = what the label says today, other languages blank.
            var cells = new List<string> { r.key, CsvTable.Escape(r.currentText.Trim()) };
            for (int i = 2; i < columns; i++) cells.Add("");

            added.Add(string.Join(",", cells));
            existing.Add(r.key);
        }

        if (added.Count == 0)
        {
            Debug.Log("[Localization] nothing new to export — every selected key is already in the sheet.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"# ── added by Localization Tools from {EditorSceneManager.GetActiveScene().name} ──");
        foreach (string line in added) sb.AppendLine(line);

        File.AppendAllText(CsvPath, sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.Refresh();

        Debug.Log($"[Localization] appended {added.Count} rows to {CsvPath}. " +
                  "The hi/mr columns are blank — fill them in, or they fall back to English.");
    }

    // ── Audit ─────────────────────────────────────────────────────────────────

    private void Audit()
    {
        if (!File.Exists(CsvPath)) { auditReport = $"No sheet at {CsvPath}"; return; }

        List<string[]> table = CsvTable.Parse(File.ReadAllText(CsvPath, Encoding.UTF8));
        if (table.Count == 0) { auditReport = "Sheet is empty."; return; }

        string[] header = table[0];
        var sheetKeys = new HashSet<string>();
        var untranslated = new List<string>();

        for (int r = 1; r < table.Count; r++)
        {
            string[] row = table[r];
            if (row.Length == 0) continue;
            string key = row[0].Trim();
            if (string.IsNullOrEmpty(key) || key.StartsWith("#")) continue;

            sheetKeys.Add(key);

            for (int c = 1; c < header.Length; c++)
                if (c >= row.Length || string.IsNullOrWhiteSpace(row[c]))
                    untranslated.Add($"{key}  ({header[c].Trim()})");
        }

        // Keys referenced from code as L.T("…")
        var usedInCode = new HashSet<string>();
        foreach (string file in Directory.GetFiles("Assets/Script", "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"L\.T\(\s*""([^""]+)"""))
                usedInCode.Add(m.Groups[1].Value);

        // Keys referenced from LocalizedText components in the open scene
        var usedInScene = new HashSet<string>();
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (LocalizedText b in root.GetComponentsInChildren<LocalizedText>(true))
                if (!string.IsNullOrEmpty(b.Key)) usedInScene.Add(b.Key);

        var missing = usedInCode.Concat(usedInScene).Where(k => !sheetKeys.Contains(k)).Distinct().OrderBy(k => k).ToList();
        var unused = sheetKeys.Where(k => !usedInCode.Contains(k) && !usedInScene.Contains(k)).OrderBy(k => k).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"sheet: {sheetKeys.Count} keys, languages: {string.Join(", ", header.Skip(1).Select(h => h.Trim()))}");
        sb.AppendLine($"referenced: {usedInCode.Count} in code, {usedInScene.Count} in the open scene");
        sb.AppendLine();

        sb.AppendLine($"MISSING FROM SHEET ({missing.Count}) — these show on screen as [key]:");
        foreach (string k in missing) sb.AppendLine("   " + k);

        sb.AppendLine();
        sb.AppendLine($"UNTRANSLATED CELLS ({untranslated.Count}) — these fall back to English:");
        foreach (string k in untranslated.Take(60)) sb.AppendLine("   " + k);
        if (untranslated.Count > 60) sb.AppendLine($"   … and {untranslated.Count - 60} more");

        sb.AppendLine();
        sb.AppendLine($"UNREFERENCED IN CODE + OPEN SCENE ({unused.Count}) — expected for keys used by the other scene:");
        foreach (string k in unused.Take(40)) sb.AppendLine("   " + k);
        if (unused.Count > 40) sb.AppendLine($"   … and {unused.Count - 40} more");

        auditReport = sb.ToString();
        Debug.Log("[Localization] audit:\n" + auditReport);
    }

    private static string Short(string s, int max)
    {
        s = (s ?? "").Replace("\n", "⏎").Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
