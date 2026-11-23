using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Menu: Real Bingo / Fixes / Build FAQ Panel In Scene
///
/// Creates the FAQ screen in GameScene and finishes wiring FAQAccordion, which
/// until now had itemPrefab, contentParent and entries all empty - so Build()
/// logged a warning and returned without doing anything.
///
///   UICanvas
///   +- FAQPanel            Image (dimmer) + Canvas + GraphicRaycaster, starts OFF
///      +- PopUp            Image, fixed 1200x800, centre-anchored
///         |- Title         TextMeshProUGUI
///         |- CloseBtn      Image + Button  -> FAQPanel.SetActive(false)
///         +- Scroll View   Image + ScrollRect
///            +- Viewport   Image + Mask
///               +- Content VerticalLayoutGroup + ContentSizeFitter  -> contentParent
///
/// This follows the conventions already in the scene: a full-screen dimmer on its
/// own canvas (as SettingPanel and ReferAndEarn do), a centre-anchored PopUp with
/// a FIXED size rather than corner anchors with insets, and the project's own
/// Scroll View shape of Image+ScrollRect / Image+Mask / layout-group Content.
///
/// Two details that matter for the accordion specifically:
///
///   - Content gets a Content Size Fitter (vertical = preferred) as well as the
///     Vertical Layout Group. Without it the scroll area never grows and rows
///     cannot be scrolled to once a few are open.
///   - That Vertical Layout Group has Control Child Height OFF on purpose. Each
///     row prefab sizes ITSELF through its own Content Size Fitter; letting the
///     parent drive the height instead would override that.
///
/// The panel is created switched off, like every other panel in the scene. The
/// rows are still built at Start: FAQAccordion lives on the always-active "FAQ"
/// object, and instantiating into an inactive parent is fine - each row's Awake
/// simply runs later, the first time the panel is shown.
///
/// Safe to run twice: it does nothing if a FAQPanel already exists, and it will
/// not overwrite entries you have already filled in.
/// </summary>
public static class RBFAQPanelBuilder
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string PrefabPath = "Assets/Prefabs/FAQItem.prefab";
    private const string PanelName = "FAQPanel";
    private const string AutoRunKey = "RBFAQ.PanelBuilt.v1";

    private const float PopUpWidth = 1200f;
    private const float PopUpHeight = 800f;

    private static readonly Color DimmerColor = new Color(0f, 0f, 0f, 0.69f);
    private static readonly Color PopUpColor = new Color(0.16f, 0.07f, 0.24f, 1f);
    private static readonly Color ScrollBgColor = new Color(0.11f, 0.05f, 0.17f, 1f);
    private static readonly Color AccentColor = new Color(1f, 0.85f, 0.45f, 1f);
    private static readonly Color CloseColor = new Color(0.75f, 0.18f, 0.22f, 1f);

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
                Debug.Log("[RBFAQPanel] Ready, but GameScene is not the open scene. Open it and " +
                          "refresh (Ctrl+R), or run Real Bingo / Fixes / Build FAQ Panel In Scene.");
                return;
            }

            EditorPrefs.SetBool(AutoRunKey, true);
            BuildPanel();
        };
    }

    [MenuItem("Real Bingo/Fixes/Build FAQ Panel In Scene", priority = 15)]
    public static void BuildPanel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[RBFAQPanel] Stop Play mode first.");
            return;
        }

        if (EditorSceneManager.GetActiveScene().path != GameScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        }

        // Everything we need before touching the scene.
        FAQAccordion accordion = Object.FindFirstObjectByType<FAQAccordion>(FindObjectsInactive.Include);
        if (accordion == null) { Fail("No FAQAccordion in GameScene."); return; }

        GameObject prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        FAQAccordionItem prefabItem = prefabGo != null ? prefabGo.GetComponent<FAQAccordionItem>() : null;
        if (prefabItem == null)
        {
            Fail($"{PrefabPath} is missing or has no FAQAccordionItem. " +
                 "Run Real Bingo / Fixes / Build FAQ Item Prefab first.");
            return;
        }

        Canvas uiCanvas = null;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.name == "UICanvas") { uiCanvas = c; break; }
        if (uiCanvas == null) { Fail("No canvas called \"UICanvas\" in GameScene."); return; }

        foreach (RectTransform t in uiCanvas.GetComponentsInChildren<RectTransform>(true))
        {
            if (t.name == PanelName)
            {
                Fail($"\"{PanelName}\" already exists under UICanvas - leaving it alone.");
                return;
            }
        }

        // Dimmer, on its own canvas so it sorts above the game like the others.
        RectTransform panel = NewUI(PanelName, uiCanvas.transform);
        Stretch(panel, 0f, 0f, 0f, 0f);
        Image dim = panel.gameObject.AddComponent<Image>();
        dim.sprite = Builtin("UI/Skin/UISprite.psd");
        dim.type = Image.Type.Sliced;
        dim.color = DimmerColor;

        Canvas panelCanvas = panel.gameObject.AddComponent<Canvas>();
        panelCanvas.overrideSorting = true;
        panelCanvas.sortingOrder = 11;              // ReferAndEarn sits at 10
        panel.gameObject.AddComponent<GraphicRaycaster>();

        // PopUp: a FIXED centred size, never corner anchors with insets - that is
        // what made the Refer and Earn popup change shape on every device.
        RectTransform popUp = NewUI("PopUp", panel);
        popUp.anchorMin = popUp.anchorMax = popUp.pivot = new Vector2(0.5f, 0.5f);
        popUp.anchoredPosition = Vector2.zero;
        popUp.sizeDelta = new Vector2(PopUpWidth, PopUpHeight);
        Image popBg = popUp.gameObject.AddComponent<Image>();
        popBg.sprite = Builtin("UI/Skin/UISprite.psd");
        popBg.type = Image.Type.Sliced;
        popBg.color = PopUpColor;

        // Title.
        RectTransform title = NewUI("Title", popUp);
        title.anchorMin = new Vector2(0f, 1f);
        title.anchorMax = new Vector2(1f, 1f);
        title.pivot = new Vector2(0.5f, 1f);
        title.anchoredPosition = new Vector2(0f, -30f);
        title.sizeDelta = new Vector2(-200f, 80f);
        TextMeshProUGUI titleText = title.gameObject.AddComponent<TextMeshProUGUI>();
        titleText.text = "FAQ";
        titleText.fontSize = 48f;
        titleText.color = AccentColor;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.raycastTarget = false;

        // Close button, wired straight to the panel - no new code needed.
        RectTransform close = NewUI("CloseBtn", popUp);
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-24f, -24f);
        close.sizeDelta = new Vector2(72f, 72f);
        Image closeBg = close.gameObject.AddComponent<Image>();
        closeBg.sprite = Builtin("UI/Skin/UISprite.psd");
        closeBg.type = Image.Type.Sliced;
        closeBg.color = CloseColor;
        Button closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBg;
        UnityEventTools.AddBoolPersistentListener(
            closeButton.onClick, new UnityAction<bool>(panel.gameObject.SetActive), false);

        RectTransform closeLabel = NewUI("X", close);
        Stretch(closeLabel, 0f, 0f, 0f, 0f);
        TextMeshProUGUI closeText = closeLabel.gameObject.AddComponent<TextMeshProUGUI>();
        closeText.text = "X";
        closeText.fontSize = 40f;
        closeText.color = Color.white;
        closeText.alignment = TextAlignmentOptions.Center;
        closeText.raycastTarget = false;

        // Scroll View, in the shape the rest of the scene already uses.
        RectTransform scroll = NewUI("Scroll View", popUp);
        Stretch(scroll, 40f, 40f, 130f, 40f);
        Image scrollBg = scroll.gameObject.AddComponent<Image>();
        scrollBg.sprite = Builtin("UI/Skin/UISprite.psd");
        scrollBg.type = Image.Type.Sliced;
        scrollBg.color = ScrollBgColor;
        ScrollRect scrollRect = scroll.gameObject.AddComponent<ScrollRect>();

        RectTransform viewport = NewUI("Viewport", scroll);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.pivot = new Vector2(0f, 1f);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.sprite = Builtin("UI/Skin/UISprite.psd");
        viewportImage.type = Image.Type.Sliced;
        Mask viewportMask = viewport.gameObject.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        RectTransform content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(16, 16, 16, 16);
        contentLayout.spacing = 12f;
        contentLayout.childAlignment = TextAnchor.UpperCenter;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;   // each row sizes itself, see the class doc
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter contentFitter = content.gameObject.AddComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = content;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.elasticity = 0.1f;
        scrollRect.scrollSensitivity = 30f;

        // Wire the accordion. Its fields are private [SerializeField].
        var so = new SerializedObject(accordion);
        SetRef(so, "itemPrefab", prefabItem);
        SetRef(so, "contentParent", content);

        SerializedProperty entries = so.FindProperty("entries");
        bool addedSamples = false;
        if (entries != null && entries.arraySize == 0)
        {
            // Obvious placeholders. Nothing here should read as a real policy
            // statement about deposits, withdrawals or bonuses.
            AddEntry(entries, 0, "Sample question 1", "Replace this answer with your own text.");
            AddEntry(entries, 1, "Sample question 2",
                     "Answers wrap over as many lines as they need, and the row grows to fit.");
            AddEntry(entries, 2, "Sample question 3", "Only one question stays open at a time.");
            addedSamples = true;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(accordion);

        Undo.RegisterCreatedObjectUndo(panel.gameObject, "Build FAQ Panel");
        panel.gameObject.SetActive(false);   // off, like every other panel in the scene

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string report =
            $"Created UICanvas/{PanelName} (switched off) and wired FAQAccordion:\n" +
            $"   itemPrefab    -> {PrefabPath}\n" +
            $"   contentParent -> {Path(content)}\n" +
            (addedSamples ? "   entries       -> 3 placeholder questions added\n"
                          : "   entries       -> left alone, you already have some\n") +
            "\nThe close button is wired to switch the panel off. To open it, point any\n" +
            $"button's OnClick at {PanelName} -> GameObject.SetActive, ticked.\n" +
            "\nScene marked dirty - press Ctrl+S to keep it.";

        Debug.Log("[RBFAQPanel]\n" + report);
        EditorUtility.DisplayDialog("FAQ panel built", report, "OK");
    }

    // Helpers.

    private static void AddEntry(SerializedProperty list, int index, string question, string answer)
    {
        list.InsertArrayElementAtIndex(index);
        SerializedProperty element = list.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("question").stringValue = question;
        element.FindPropertyRelative("answer").stringValue = answer;
    }

    private static void SetRef(SerializedObject so, string field, Object value)
    {
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null) { Debug.LogWarning($"[RBFAQPanel] no field '{field}' on FAQAccordion."); return; }
        prop.objectReferenceValue = value;
    }

    private static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        return rt;
    }

    private static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private static Sprite Builtin(string path)
    {
        Sprite s = AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
        if (s == null) Debug.LogWarning($"[RBFAQPanel] built-in sprite not found: {path}");
        return s;
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        for (Transform q = t.parent; q != null; q = q.parent) p = q.name + "/" + p;
        return p;
    }

    private static void Fail(string message)
    {
        Debug.Log("[RBFAQPanel] " + message);
        EditorUtility.DisplayDialog("FAQ panel", message, "OK");
    }
}
