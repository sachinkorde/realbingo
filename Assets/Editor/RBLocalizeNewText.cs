using System.Text;
using RealBingo.Localization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: Real Bingo / Fixes / Localize New Text
///
/// Attaches LocalizedText to the labels that were still plain English, and points
/// the FAQ rows at keys instead of hard-coded sentences.
///
/// It covers three things:
///
///   1. The screens added in this session - the FAQ, How to Play and Help titles,
///      the Help intro, note and three contact labels, and the How to Play body.
///
///   2. Two gaps that were already there: the Copy Code and Share buttons on
///      Refer and Earn, and the Winning / Total Bet labels. Those last two are
///      worth noting - game.winning and game.total_bet were added to the sheet
///      earlier, but the components were never attached, so the keys sat unused
///      and the labels stayed English.
///
///   3. The "Enter text…" placeholders on the bank and document fields.
///
/// The FAQ is not scene text - its questions live in FAQAccordion.entries - so
/// this rewrites those twenty-four values to faq.q1 … faq.a12. FAQAccordion
/// translates a value that matches a key and shows anything else as typed, so
/// the swap is safe either way.
///
/// The three Help rows each packed a label and a phone number into one label, and
/// a number must not be translated. Each row is split into Label (localized) and
/// Value (left alone).
///
/// fontTable is left empty on every new component, matching all 109 that are
/// already in the scene - the Devanagari fallback handles the script instead.
///
/// Safe to run twice: a label that already has a LocalizedText is skipped.
/// </summary>
public static class RBLocalizeNewText
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string AutoRunKey = "RBLocalizeNewText.Done.v1";

    /// <summary>path under the scene root, then the key to give it.</summary>
    private static readonly (string path, string key)[] SceneLabels =
    {
        ("UICanvas/FAQPanel/PopUp/Title",                                   "ui.faq"),
        ("UICanvas/HowToPlayPanel/PopUp/Title",                             "ui.how_to_play"),
        ("UICanvas/HowToPlayPanel/PopUp/Scroll View/Viewport/Content",      "howtoplay.body"),
        ("UICanvas/HelpPanel/PopUp/Title",                                  "help.title"),
        ("UICanvas/HelpPanel/PopUp/Intro",                                  "help.intro"),
        ("UICanvas/HelpPanel/PopUp/Note",                                   "help.note"),
        ("UICanvas/ReferAndEarn/PopUp/Btns_Panel/CopyBtn/Text (TMP)",       "referral.copy_code"),
        ("UICanvas/ReferAndEarn/PopUp/Btns_Panel/ShareBtn/Text (TMP)",      "referral.share"),
        ("UICanvas/WinningAndBet/Winning/WinningAmt Text (TMP)",            "game.winning"),
        ("UICanvas/WinningAndBet/Total Bet/TotalBet Amt Text (TMP)",        "game.total_bet"),
    };

    /// <summary>The Help rows: row object, the label's key, and the untranslated value.</summary>
    private static readonly (string row, string key)[] ContactRows =
    {
        ("UICanvas/HelpPanel/PopUp/PhoneRow",    "help.call_us"),
        ("UICanvas/HelpPanel/PopUp/WhatsAppRow", "help.whatsapp"),
        ("UICanvas/HelpPanel/PopUp/EmailRow",    "help.email_us"),
    };

    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        if (EditorPrefs.GetBool(AutoRunKey, false)) return;

        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(AutoRunKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (EditorSceneManager.GetActiveScene().path != GameScenePath)
            {
                Debug.Log("[RBLoc] Ready, but GameScene is not the open scene. Open it and refresh " +
                          "(Ctrl+R), or run Real Bingo / Fixes / Localize New Text.");
                return;
            }

            EditorPrefs.SetBool(AutoRunKey, true);
            LocalizeNewText();
        };
    }

    [MenuItem("Real Bingo/Fixes/Localize New Text", priority = 18)]
    public static void LocalizeNewText()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[RBLoc] Stop Play mode first.");
            return;
        }

        if (EditorSceneManager.GetActiveScene().path != GameScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        }

        var log = new StringBuilder();
        int attached = 0, skipped = 0, missing = 0;

        foreach ((string path, string key) in SceneLabels)
        {
            GameObject go = FindByPath(path);
            if (go == null) { log.AppendLine($"   missing: {path}"); missing++; continue; }

            switch (Attach(go, key))
            {
                case AttachResult.Attached: attached++; log.AppendLine($"   {key}  ->  {path}"); break;
                case AttachResult.AlreadyThere: skipped++; break;
                case AttachResult.NoText: missing++; log.AppendLine($"   no TMP_Text on {path}"); break;
            }
        }

        foreach ((string row, string key) in ContactRows)
        {
            GameObject rowGo = FindByPath(row);
            if (rowGo == null) { log.AppendLine($"   missing: {row}"); missing++; continue; }

            if (SplitContactRow(rowGo, key, log)) attached++;
            else skipped++;
        }

        // The placeholders are three separate "Enter text…" fields; find them by
        // their text rather than by path, which is long and easy to get wrong.
        int placeholders = 0;
        foreach (TMP_Text t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.text != "Enter text..." && t.text != "Enter text…") continue;
            if (t.GetComponent<LocalizedText>() != null) continue;

            Attach(t.gameObject, "common.enter_text");
            placeholders++;
        }
        if (placeholders > 0) log.AppendLine($"   common.enter_text  ->  {placeholders} placeholder fields");

        int faq = PointFaqAtKeys(log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string report =
            $"Attached LocalizedText to {attached} labels" +
            (skipped > 0 ? $", {skipped} already had one" : "") +
            (missing > 0 ? $", {missing} not found" : "") + ".\n" +
            (faq >= 0 ? $"FAQ: {faq} entries now point at faq.q1 … faq.a12.\n" : "FAQ: left alone.\n") +
            "\n" + log +
            "\nScene marked dirty - press Ctrl+S to keep it.";

        Debug.Log("[RBLoc]\n" + report);
        EditorUtility.DisplayDialog("Localization", report, "OK");
    }

    // ── Pieces ───────────────────────────────────────────────────────────────

    private enum AttachResult { Attached, AlreadyThere, NoText }

    private static AttachResult Attach(GameObject go, string key)
    {
        if (go.GetComponent<TMP_Text>() == null) return AttachResult.NoText;
        if (go.GetComponent<LocalizedText>() != null) return AttachResult.AlreadyThere;

        LocalizedText loc = Undo.AddComponent<LocalizedText>(go);

        var so = new SerializedObject(loc);
        SerializedProperty prop = so.FindProperty("key");
        if (prop == null)
        {
            Debug.LogWarning($"[RBLoc] LocalizedText has no 'key' field - cannot set {key}.");
            return AttachResult.NoText;
        }
        prop.stringValue = key;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(loc);
        return AttachResult.Attached;
    }

    /// <summary>
    /// Splits "Call us\n+91 …" into a translated Label and an untranslated Value.
    /// A phone number or an email address must survive every language unchanged,
    /// so it cannot share a string with the words around it.
    /// </summary>
    private static bool SplitContactRow(GameObject rowGo, string key, StringBuilder log)
    {
        Transform labelTr = rowGo.transform.Find("Label");
        if (labelTr == null) { log.AppendLine($"   {rowGo.name}: no Label child."); return false; }

        TMP_Text label = labelTr.GetComponent<TMP_Text>();
        if (label == null) { log.AppendLine($"   {rowGo.name}: Label has no TMP_Text."); return false; }
        if (labelTr.GetComponent<LocalizedText>() != null) return false;   // already done

        // Pull the value out of the second line, which the builder wrote as
        // "<label>\n<size=80%><color=#CFC8DC><value></color></size>".
        string value = "";
        string raw = label.text;
        int newline = raw.IndexOf('\n');
        if (newline >= 0)
        {
            value = raw.Substring(newline + 1);
            value = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", "").Trim();
        }

        var labelRect = (RectTransform)labelTr;
        Undo.RecordObject(labelRect, "Localize New Text");
        labelRect.anchorMin = new Vector2(0f, 0.46f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        label.text = "";                       // LocalizedText fills this in
        label.alignment = TextAlignmentOptions.BottomLeft;

        Attach(labelTr.gameObject, key);

        // The number / address, in its own object so no translation touches it.
        var valueGo = new GameObject("Value", typeof(RectTransform));
        valueGo.layer = rowGo.layer;
        var valueRect = (RectTransform)valueGo.transform;
        valueRect.SetParent(labelTr.parent, false);
        valueRect.anchorMin = new Vector2(0f, 0f);
        valueRect.anchorMax = new Vector2(1f, 0.46f);
        valueRect.offsetMin = new Vector2(labelRect.offsetMin.x, 0f);
        valueRect.offsetMax = new Vector2(labelRect.offsetMax.x, 0f);
        valueRect.localScale = Vector3.one;

        TextMeshProUGUI valueText = valueGo.AddComponent<TextMeshProUGUI>();
        valueText.text = value;
        valueText.fontSize = label.fontSize * 0.8f;
        valueText.color = new Color(0.81f, 0.78f, 0.86f, 1f);
        valueText.alignment = TextAlignmentOptions.TopLeft;
        valueText.raycastTarget = false;

        Undo.RegisterCreatedObjectUndo(valueGo, "Localize New Text");

        log.AppendLine($"   {key}  ->  {rowGo.name}/Label   (number kept in {rowGo.name}/Value)");
        return true;
    }

    /// <summary>
    /// Replaces the twelve written-out questions with their keys. Returns the
    /// number changed, or -1 if the entries are not the ones this session wrote.
    /// </summary>
    private static int PointFaqAtKeys(StringBuilder log)
    {
        FAQAccordion accordion = Object.FindFirstObjectByType<FAQAccordion>(FindObjectsInactive.Include);
        if (accordion == null) { log.AppendLine("   no FAQAccordion in the scene."); return -1; }

        var so = new SerializedObject(accordion);
        SerializedProperty entries = so.FindProperty("entries");
        if (entries == null || entries.arraySize == 0) return -1;

        // Only touch the twelve this session wrote, never hand-written content.
        if (entries.arraySize != 12)
        {
            log.AppendLine($"   FAQ has {entries.arraySize} entries, not the 12 written here - left alone.");
            return -1;
        }

        SerializedProperty first = entries.GetArrayElementAtIndex(0).FindPropertyRelative("question");
        if (first.stringValue != "How do I place a bet?" && !first.stringValue.StartsWith("faq."))
        {
            log.AppendLine("   FAQ entries have been edited by hand - left alone.");
            return -1;
        }

        for (int i = 0; i < 12; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("question").stringValue = $"faq.q{i + 1}";
            e.FindPropertyRelative("answer").stringValue = $"faq.a{i + 1}";
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(accordion);
        return 12;
    }

    private static GameObject FindByPath(string path)
    {
        string[] parts = path.Split('/');

        GameObject root = null;
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == parts[0]) { root = go; break; }

        if (root == null) return null;

        Transform t = root.transform;
        for (int i = 1; i < parts.Length; i++)
        {
            t = t.Find(parts[i]);
            if (t == null) return null;
        }
        return t.gameObject;
    }
}
