using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu: Real Bingo / Fixes / Build FAQ Item Prefab
///
/// Builds the one row prefab FAQAccordion instantiates per question. Everything
/// here is stock Unity UI (Image, Button, TextMeshProUGUI, Vertical Layout Group,
/// Content Size Fitter, Layout Element, Rect Mask 2D) and deliberately plain, so
/// it is easy to restyle afterwards - the structure is what matters.
///
/// It builds exactly what FAQAccordionItem's doc comment asks for:
///
///   FAQItem            VerticalLayoutGroup + ContentSizeFitter + FAQAccordionItem
///   |- Header          Image + Button + LayoutElement      -> header
///   |   |- QuestionText  TextMeshProUGUI                   -> questionText
///   |   +- Arrow         Image                             -> arrow
///   +- Body            LayoutElement + RectMask2D          -> body
///       +- AnswerText    TextMeshProUGUI + ContentSizeFitter -> answerText
///
/// Two things differ from the letter of that comment, both on purpose:
///
///   - Arrow is a child of Header, not a sibling. As a sibling it would be its own
///     row in the Vertical Layout Group and sit under the question instead of
///     beside it. The script only needs the reference, and it rotates the arrow
///     about its own centre either way.
///
///   - Body gets a RectMask2D. Its height animates from 0, and without a mask the
///     answer text would spill out over the row below for the whole tween.
///
/// Why each layout piece is needed, since it is easy to break by tidying up:
///
///   - FAQItem's Vertical Layout Group must have Control Child Height ON. That is
///     what makes Body's height follow the Layout Element the script animates.
///   - FAQItem's Content Size Fitter (vertical = preferred) is what makes the ROW
///     grow and shrink as Body does, so the list reflows.
///   - AnswerText's own Content Size Fitter (vertical = preferred) is what
///     MeasureAnswerHeight reads. Body deliberately has no layout group, because
///     one would override that fitter and the measurement would come back wrong.
///
/// The prefab is built in a preview scene, so the scene you have open is not
/// touched. It refuses to overwrite an existing prefab.
/// </summary>
public static class RBFAQPrefabBuilder
{
    private const string PrefabPath = "Assets/Prefabs/FAQItem.prefab";
    private const string AutoRunKey = "RBFAQ.PrefabBuilt.v2";

    // Plain, readable defaults. Restyle freely - none of this is load-bearing.
    private const float RowWidth = 900f;
    private const float HeaderHeight = 90f;
    private const float QuestionFontSize = 32f;
    private const float AnswerFontSize = 26f;
    private const float SidePadding = 24f;
    private const float ArrowSize = 36f;

    private static readonly Color HeaderColor = new Color(0.22f, 0.10f, 0.32f, 1f);
    private static readonly Color BodyColor = new Color(0.14f, 0.06f, 0.21f, 1f);
    private static readonly Color QuestionColor = new Color(1f, 0.85f, 0.45f, 1f);
    private static readonly Color AnswerColor = new Color(0.90f, 0.88f, 0.95f, 1f);

    /// <summary>
    /// Builds itself on the next script reload so it does not depend on finding a
    /// menu item. Once only, never in Play mode, and never over an existing prefab.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        if (EditorPrefs.GetBool(AutoRunKey, false)) return;

        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(AutoRunKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EditorPrefs.SetBool(AutoRunKey, true);
            BuildFAQItemPrefab();
        };
    }

    [MenuItem("Real Bingo/Fixes/Build FAQ Item Prefab", priority = 14)]
    public static void BuildFAQItemPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[RBFAQ] Stop Play mode first.");
            return;
        }

        // The prefab already exists: keep whatever has been done to it and only put
        // back what is missing, rather than throwing away the styling.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            RepairExistingPrefab();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));

        // Build in a preview scene so the open scene is never dirtied.
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject rootGo = null;

        try
        {
            RectTransform root = NewUI("FAQItem", null);
            rootGo = root.gameObject;
            EditorSceneManager.MoveGameObjectToScene(rootGo, preview);

            root.sizeDelta = new Vector2(RowWidth, HeaderHeight);

            // The row stacks Header above Body, and grows to fit whatever they need.
            VerticalLayoutGroup rowLayout = rootGo.AddComponent<VerticalLayoutGroup>();
            rowLayout.padding = new RectOffset(0, 0, 0, 0);
            rowLayout.spacing = 0f;
            rowLayout.childAlignment = TextAnchor.UpperCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;     // required: lets Body follow its LayoutElement
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            ContentSizeFitter rowFitter = rootGo.AddComponent<ContentSizeFitter>();
            rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Header: the tappable question bar.
            RectTransform header = NewUI("Header", root);
            Image headerBg = header.gameObject.AddComponent<Image>();
            headerBg.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            headerBg.type = Image.Type.Sliced;
            headerBg.color = HeaderColor;

            Button headerButton = header.gameObject.AddComponent<Button>();
            headerButton.targetGraphic = headerBg;

            LayoutElement headerLayout = header.gameObject.AddComponent<LayoutElement>();
            headerLayout.preferredHeight = HeaderHeight;
            headerLayout.minHeight = HeaderHeight;

            // Question text: fills the bar, leaving room for the arrow.
            RectTransform question = NewUI("QuestionText", header);
            Stretch(question, SidePadding, SidePadding * 2f + ArrowSize, 0f, 0f);
            TextMeshProUGUI questionText = question.gameObject.AddComponent<TextMeshProUGUI>();
            questionText.text = "Question goes here";
            questionText.fontSize = QuestionFontSize;
            questionText.color = QuestionColor;
            questionText.alignment = TextAlignmentOptions.MidlineLeft;
            questionText.raycastTarget = false;      // the Button underneath takes the tap

            // Arrow: rotated 180 degrees by the script while open.
            RectTransform arrow = NewUI("Arrow", header);
            arrow.anchorMin = new Vector2(1f, 0.5f);
            arrow.anchorMax = new Vector2(1f, 0.5f);
            arrow.pivot = new Vector2(1f, 0.5f);
            arrow.anchoredPosition = new Vector2(-SidePadding, 0f);
            arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            Image arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.sprite = BuiltinSprite("UI/Skin/DropdownArrow.psd");
            arrowImage.color = QuestionColor;
            arrowImage.raycastTarget = false;
            arrowImage.preserveAspect = true;

            // Body: height animates 0 -> measured, clipped while it moves.
            RectTransform body = NewUI("Body", root);
            Image bodyBg = body.gameObject.AddComponent<Image>();
            bodyBg.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            bodyBg.type = Image.Type.Sliced;
            bodyBg.color = BodyColor;
            bodyBg.raycastTarget = false;

            LayoutElement bodyLayout = body.gameObject.AddComponent<LayoutElement>();
            bodyLayout.preferredHeight = 0f;         // starts collapsed
            bodyLayout.minHeight = 0f;

            body.gameObject.AddComponent<RectMask2D>();

            // Answer text: sizes ITSELF, which is what the script measures.
            RectTransform answer = NewUI("AnswerText", body);
            answer.anchorMin = new Vector2(0f, 1f);
            answer.anchorMax = new Vector2(1f, 1f);
            answer.pivot = new Vector2(0.5f, 1f);
            answer.offsetMin = new Vector2(SidePadding, 0f);
            answer.offsetMax = new Vector2(-SidePadding, 0f);

            TextMeshProUGUI answerText = answer.gameObject.AddComponent<TextMeshProUGUI>();
            answerText.text = "The answer goes here. It wraps over as many lines as it needs, " +
                              "and the row grows to fit it when the question is tapped.";
            answerText.fontSize = AnswerFontSize;
            answerText.color = AnswerColor;
            answerText.alignment = TextAlignmentOptions.TopLeft;
            answerText.margin = new Vector4(0f, SidePadding * 0.75f, 0f, SidePadding * 0.75f);
            answerText.raycastTarget = false;

            ContentSizeFitter answerFitter = answer.gameObject.AddComponent<ContentSizeFitter>();
            answerFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            answerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Wire the script's own references. They are private [SerializeField],
            // so this goes through SerializedObject rather than direct assignment.
            FAQAccordionItem item = rootGo.AddComponent<FAQAccordionItem>();
            var so = new SerializedObject(item);
            Assign(so, "header", headerButton);
            Assign(so, "questionText", questionText);
            Assign(so, "answerText", answerText);
            Assign(so, "body", body);
            Assign(so, "arrow", arrow);
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath, out bool ok);

            if (!ok)
            {
                Debug.LogError($"[RBFAQ] Unity refused to save {PrefabPath}.");
                return;
            }

            AssetDatabase.ImportAsset(PrefabPath);

            string report =
                $"Built {PrefabPath}\n\n" +
                "FAQItem  (FAQAccordionItem, Vertical Layout Group, Content Size Fitter)\n" +
                "  Header  (Image, Button, Layout Element)\n" +
                "    QuestionText  (TextMeshProUGUI)\n" +
                "    Arrow         (Image)\n" +
                "  Body    (Image, Layout Element, Rect Mask 2D)\n" +
                "    AnswerText    (TextMeshProUGUI, Content Size Fitter)\n\n" +
                "All five references on FAQAccordionItem are wired.\n\n" +
                "To use it, on the FAQ object in GameScene:\n" +
                "  1. Drag this prefab into itemPrefab.\n" +
                "  2. Make a Scroll View; on its Content add a Vertical Layout Group\n" +
                "     and a Content Size Fitter (Vertical Fit = Preferred Size), then\n" +
                "     drag Content into contentParent.\n" +
                "  3. Fill in entries. Rows are built at runtime.";

            Debug.Log("[RBFAQ]\n" + report);
            EditorUtility.DisplayDialog("FAQ item prefab built", report, "OK");
        }
        finally
        {
            if (rootGo != null) Object.DestroyImmediate(rootGo);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    /// <summary>
    /// Puts FAQAccordionItem back on an existing prefab and re-wires its five
    /// references, leaving every object, colour and size alone.
    ///
    /// The component went missing because FAQAccordionItem used to live inside
    /// FAQAccordion.cs. Unity only builds a MonoScript for the class whose name
    /// matches the file, so the component had no script asset to point at and the
    /// reference could not survive a reimport. It now has its own
    /// FAQAccordionItem.cs, so this sticks.
    /// </summary>
    private static void RepairExistingPrefab()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);

        try
        {
            var log = new System.Text.StringBuilder();

            FAQAccordionItem item = contents.GetComponent<FAQAccordionItem>();
            if (item == null)
            {
                item = contents.AddComponent<FAQAccordionItem>();
                log.AppendLine("Added the missing FAQAccordionItem component to the prefab root.");
            }
            else
            {
                log.AppendLine("FAQAccordionItem was already on the root.");
            }

            Transform header = Find(contents.transform, "Header");
            Transform question = Find(contents.transform, "QuestionText");
            Transform answer = Find(contents.transform, "AnswerText");
            Transform body = Find(contents.transform, "Body");
            Transform arrow = Find(contents.transform, "Arrow");

            if (header == null || question == null || answer == null || body == null)
            {
                string missing =
                    (header == null ? "Header " : "") + (question == null ? "QuestionText " : "") +
                    (answer == null ? "AnswerText " : "") + (body == null ? "Body" : "");
                Fail($"The prefab is missing: {missing.Trim()}. Delete {PrefabPath} and run this " +
                     "again to rebuild it from scratch.");
                return;
            }

            var so = new SerializedObject(item);
            Assign(so, "header", header.GetComponent<Button>());
            Assign(so, "questionText", question.GetComponent<TMP_Text>());
            Assign(so, "answerText", answer.GetComponent<TMP_Text>());
            Assign(so, "body", body as RectTransform);
            Assign(so, "arrow", arrow as RectTransform);
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("Re-wired header, questionText, answerText, body and arrow.");

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath, out bool ok);
            if (!ok) { Fail($"Unity refused to save {PrefabPath}."); return; }

            AssetDatabase.ImportAsset(PrefabPath);

            log.AppendLine();
            log.AppendLine("Next: the FAQ object in GameScene still points its itemPrefab at the");
            log.AppendLine("component that went missing, so re-drag the prefab into that slot.");

            Debug.Log("[RBFAQ] Repaired " + PrefabPath + "\n" + log);
            EditorUtility.DisplayDialog("FAQ item prefab repaired", log.ToString(), "OK");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // Helpers.

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name) return t;
        return null;
    }

    private static void Fail(string message)
    {
        Debug.LogWarning("[RBFAQ] " + message);
        EditorUtility.DisplayDialog("FAQ item prefab", message, "OK");
    }

    private static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;   // built-in UI layer
        var rt = (RectTransform)go.transform;
        if (parent != null) rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private static Sprite BuiltinSprite(string path)
    {
        Sprite s = AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
        if (s == null) Debug.LogWarning($"[RBFAQ] Built-in sprite not found: {path} - leaving it blank.");
        return s;
    }

    private static void Assign(SerializedObject so, string field, Object value)
    {
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[RBFAQ] FAQAccordionItem has no field called '{field}' - skipped.");
            return;
        }
        prop.objectReferenceValue = value;
    }
}
