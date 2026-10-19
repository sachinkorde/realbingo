using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu:  Real Bingo ▸ Localization ▸ Show Conjunct Test Card
///
/// Drops a full-screen TMP label into the open scene showing every conjunct
/// cluster (जोडाक्षर) that the translations actually use, each next to the
/// word it comes from.
///
/// WHY
/// Whether TextMeshPro forms a given conjunct depends on which OpenType
/// feature the font puts it in. TMP reads liga, kern, mark and mkmk and
/// ignores the Devanagari-specific ones (rphf, blwf, half, vatu, cjct, pres,
/// psts, haln), so some clusters come out correct and others come out as two
/// letters with a visible halant — and which is which cannot be worked out by
/// reading the font tables alone. Putting them all on screen at once answers
/// it in one screenshot.
///
/// WHAT TO LOOK FOR
///   correct : क्ष त्र ज्ञ drawn as ONE joined shape
///   broken  : क् + ष with a visible slanted stroke (्) between them
///   reph    : र् should become a small hook ABOVE the next letter,
///             as in पासवर्ड — not a separate र followed by a halant
///
/// Remove it with the companion menu item when done; it is scene clutter, not
/// something to keep.
/// </summary>
public static class RBConjunctTestCard
{
    private const string RootName = "[Conjunct Test Card]";

    // Cluster -> a real word from the sheet that contains it.
    private static readonly (string cluster, string word, string meaning)[] Samples =
    {
        ("र्ड", "पासवर्ड",   "password - REPH, by far the most common"),
        ("प्र", "प्रयत्न",    "attempt"),
        ("क्र", "क्रमांक",    "number"),
        ("ष्ट", "पुष्टी",     "confirm"),
        ("न्ह", "नाही",      "no"),
        ("र्त", "वापरकर्ता",  "user - REPH"),
        ("त्न", "प्रयत्न",    "attempt"),
        ("ब्ध", "उपलब्ध",    "available"),
        ("न्म", "जन्म",      "birth"),
        ("क्क", "रक्कम",     "amount"),
        ("र्व", "सर्व",      "all - REPH"),
        ("स्थ", "स्थिती",     "status"),
        ("त्त", "खात्री",     "assurance"),
        ("न्य", "अन्य",      "other"),
        ("त्य", "प्रयत्न",    "attempt"),
        ("र्क", "नेटवर्क",    "network - REPH"),
        ("ध्य", "मध्ये",     "in"),
        ("स्ट", "रजिस्टर",   "register"),
        ("स्त", "दस्तऐवज",   "document"),
        ("व्ह", "व्हा",       "be"),
        ("प्त", "समाप्त",     "finished"),
        ("स्व", "स्वागत",     "welcome"),
        ("ल्ल", "बिल्ला",     "badge"),
        ("क्ष", "कक्ष",      "classic test conjunct"),
        ("ज्ञ", "ज्ञान",      "classic test conjunct"),
    };

    [MenuItem("Real Bingo/Localization/Show Conjunct Test Card")]
    private static void Show()
    {
        Remove();   // never stack two

        Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Exclude);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("No Canvas", "Open a scene with a UI Canvas first.", "OK");
            return;
        }

        var root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(canvas.rootCanvas.transform, false);
        root.transform.SetAsLastSibling();
        Undo.RegisterCreatedObjectUndo(root, "Conjunct test card");

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        root.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.93f);

        var labelGO = new GameObject("Text", typeof(RectTransform));
        labelGO.transform.SetParent(root.transform, false);
        RectTransform lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(40, 40); lrt.offsetMax = new Vector2(-40, -40);

        TMP_Text label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = BuildText();
        label.fontSize = 26f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.textWrappingMode = TextWrappingModes.Normal;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log("[Localization] conjunct test card added. Look at it in the Game view " +
                  "(or press Play), screenshot it, then remove it with " +
                  "Real Bingo ▸ Localization ▸ Remove Conjunct Test Card.");
    }

    private static string BuildText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<b>CONJUNCT (जोडाक्षर) TEST</b>");
        sb.AppendLine("<size=20>Broken = you can see the halant stroke (्) between two letters.");
        sb.AppendLine("Reph = र् should be a small hook ABOVE the next letter.</size>");
        sb.AppendLine();

        foreach (var s in Samples)
            sb.AppendLine($"{s.cluster}   {s.word}   <size=18><color=#888888>{s.meaning}</color></size>");

        sb.AppendLine();
        sb.AppendLine("<size=20>Real sentences from the sheet:</size>");
        sb.AppendLine("पासवर्ड कम से कम 6 अक्षरों का होना चाहिए");
        sb.AppendLine("कृपया मोबाइल नंबर या ईमेल डालें");
        sb.AppendLine("वापरकर्तानाव / मोबाइल क्रमांक");
        sb.AppendLine("तुमचे सेशन संपले आहे. कृपया पुन्हा लॉग इन करा.");

        return sb.ToString();
    }

    [MenuItem("Real Bingo/Localization/Remove Conjunct Test Card")]
    private static void Remove()
    {
        var found = new List<GameObject>();
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (go.name == RootName) found.Add(go);

        foreach (GameObject go in found) Undo.DestroyObjectImmediate(go);

        if (found.Count > 0)
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
