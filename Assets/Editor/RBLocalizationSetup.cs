using System.Reflection;
using RealBingo.Localization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu:  Real Bingo ▸ Localization ▸ ① Set Up Everything (Main scene)
///
/// Everything else in the localization feature is code and a CSV, which means
/// nothing is visible in the game until a "Choose your language" panel actually
/// exists in the scene and UIManagerMenu knows about it. Building that panel by
/// hand is a dozen fiddly steps, so this does it.
///
/// WHAT IT DOES, with the Main scene open:
///   1. builds the Devanagari TMP font asset and adds it to the global fallback
///   2. creates a LanguageSelectPopup panel under the scene's Canvas —
///      dimmer, card, title, subtitle, a scrollable row per language, Continue
///   3. wires every Inspector reference on it, including the button template
///   4. assigns it to UIManagerMenu's languageSelectPopup field
///   5. marks the scene dirty so you can look it over before saving
///
/// Re-running is safe: it finds the existing panel and re-wires rather than
/// creating a second one.
///
/// It only builds the PANEL. Translating the ~100 existing scene labels is a
/// separate pass — Real Bingo ▸ Localization ▸ Localization Tools.
/// </summary>
public static class RBLocalizationSetup
{
    private const string PanelName = "LanguageSelectPopup";

    // Matches the project's canvas: 1920x1080 reference, match = height.
    private static readonly Vector2 CardSize = new Vector2(820f, 900f);

    [MenuItem("Real Bingo/Localization/① Set Up Everything (Main scene)")]
    public static void SetUpEverything()
    {
        Canvas canvas = FindBestCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("No Canvas",
                "Open the Main scene first — this builds the language panel under its Canvas.", "OK");
            return;
        }

        // 1 — font (quiet: this method shows its own summary at the end)
        RBDevanagariFontSetup.SetUp(showDialog: false);

        // 2 — panel
        LanguageSelectPopup popup = Object.FindFirstObjectByType<LanguageSelectPopup>(FindObjectsInactive.Include);
        if (popup == null)
        {
            popup = BuildPanel(canvas);
            Debug.Log("[Localization] built the language panel.");
        }
        else
        {
            Debug.Log("[Localization] found an existing language panel — re-wiring it.");
        }

        // 3 — hook into the startup sequence
        var menu = Object.FindFirstObjectByType<UIManagerMenu>(FindObjectsInactive.Include);
        if (menu != null)
        {
            SerializedObject so = new SerializedObject(menu);
            SerializedProperty prop = so.FindProperty("languageSelectPopup");
            if (prop != null)
            {
                prop.objectReferenceValue = popup;
                so.ApplyModifiedProperties();
                Debug.Log("[Localization] assigned the panel to UIManagerMenu.");
            }
        }
        else
        {
            Debug.LogWarning("[Localization] no UIManagerMenu in this scene — assign the panel by hand.");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = popup.gameObject;
        EditorGUIUtility.PingObject(popup.gameObject);

        EditorUtility.DisplayDialog("Localization set up",
            "Done:\n" +
            "  • Devanagari font built + added to TMP fallback\n" +
            "  • Language panel created under the Canvas\n" +
            "  • Wired into UIManagerMenu\n\n" +
            "Press Play on the Main scene to see it. The scene is NOT saved yet — " +
            "look it over, reposition the card if you like, then save.\n\n" +
            "To see the popup again after choosing once:\n" +
            "Real Bingo ▸ Localization ▸ Reset Saved Language.",
            "OK");
    }

    /// <summary>Prefer the canvas UIManagerMenu's screens live under; else the first one.</summary>
    private static Canvas FindBestCanvas()
    {
        var menu = Object.FindFirstObjectByType<UIManagerMenu>(FindObjectsInactive.Include);
        if (menu != null)
        {
            Canvas c = menu.GetComponentInParent<Canvas>();
            if (c != null) return c.rootCanvas;
        }

        Canvas[] all = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Canvas c in all)
            if (c.renderMode == RenderMode.ScreenSpaceOverlay || c.renderMode == RenderMode.ScreenSpaceCamera)
                return c.rootCanvas;

        return all.Length > 0 ? all[0].rootCanvas : null;
    }

    // ── Panel construction ────────────────────────────────────────────────────

    private static LanguageSelectPopup BuildPanel(Canvas canvas)
    {
        // Root — full screen, sits above everything else in the canvas.
        GameObject root = NewUI(PanelName, canvas.transform);
        Stretch(root);
        root.transform.SetAsLastSibling();

        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 1f;

        // Dimmer. Also eats clicks aimed at the login screen behind it.
        GameObject dim = NewUI("Dimmer", root.transform);
        Stretch(dim);
        Image dimImage = dim.AddComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0.82f);

        // Card
        GameObject card = NewUI("Card", root.transform);
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = CardSize;
        cardRect.anchoredPosition = Vector2.zero;

        Image cardImage = card.AddComponent<Image>();
        cardImage.color = new Color(0.09f, 0.11f, 0.17f, 1f);

        var cardLayout = card.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(48, 48, 48, 48);
        cardLayout.spacing = 20f;
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = false;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        // Title + subtitle. Given LocalizedText so they translate as the player
        // taps through the list — the clearest possible preview of the choice.
        MakeLabel(card.transform, "Title", "Choose your language", 54, FontStyles.Bold,
                  new Color(1f, 1f, 1f), 76f, "language.title");
        MakeLabel(card.transform, "Subtitle", "You can change this later in Settings", 30, FontStyles.Normal,
                  new Color(0.68f, 0.72f, 0.80f), 44f, "language.subtitle");

        // Scrollable list — 3 languages today, 22 later, same panel.
        GameObject scrollGO = NewUI("LanguageScroll", card.transform);
        var scrollLayout = scrollGO.AddComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.minHeight = 480f;

        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scrollGO.AddComponent<RectMask2D>();

        GameObject content = NewUI("Content", scrollGO.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = new Vector2(0f, 0f);

        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 14f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = contentRect;
        scroll.viewport = scrollGO.GetComponent<RectTransform>();

        // The template row. LanguageSelectPopup clones this per language and
        // hides the original, so it must stay a child of Content.
        Button template = MakeLanguageRow(content.transform);

        // Confirm
        Button confirm = MakeConfirmButton(card.transform);

        // Component + wiring
        var popup = root.AddComponent<LanguageSelectPopup>();
        var so = new SerializedObject(popup);
        Set(so, "panelRoot", root);
        Set(so, "buttonTemplate", template);
        Set(so, "buttonContainer", content.transform);
        Set(so, "confirmButton", confirm);
        so.ApplyModifiedProperties();

        root.SetActive(false);   // Awake would do this anyway; keeps the scene tidy.
        return popup;
    }

    private static Button MakeLanguageRow(Transform parent)
    {
        GameObject row = NewUI("LanguageRowTemplate", parent);

        var le = row.AddComponent<LayoutElement>();
        le.minHeight = 104f;
        le.preferredHeight = 104f;

        Image bg = row.AddComponent<Image>();
        bg.color = Color.white;

        Button button = row.AddComponent<Button>();
        button.targetGraphic = bg;

        // First TMP child = native name, second = English. LanguageSelectPopup
        // fills them in that order.
        GameObject native = NewUI("NativeName", row.transform);
        RectTransform nr = native.GetComponent<RectTransform>();
        nr.anchorMin = new Vector2(0f, 0f);
        nr.anchorMax = new Vector2(0.62f, 1f);
        nr.offsetMin = new Vector2(32f, 0f);
        nr.offsetMax = Vector2.zero;

        TMP_Text nativeText = native.AddComponent<TextMeshProUGUI>();
        nativeText.text = "हिन्दी";
        nativeText.fontSize = 40f;
        nativeText.color = new Color(0.08f, 0.09f, 0.12f);
        nativeText.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject english = NewUI("EnglishName", row.transform);
        RectTransform er = english.GetComponent<RectTransform>();
        er.anchorMin = new Vector2(0.62f, 0f);
        er.anchorMax = new Vector2(1f, 1f);
        er.offsetMin = Vector2.zero;
        er.offsetMax = new Vector2(-32f, 0f);

        TMP_Text englishText = english.AddComponent<TextMeshProUGUI>();
        englishText.text = "Hindi";
        englishText.fontSize = 28f;
        englishText.color = new Color(0.42f, 0.45f, 0.52f);
        englishText.alignment = TextAlignmentOptions.MidlineRight;

        return button;
    }

    private static Button MakeConfirmButton(Transform parent)
    {
        GameObject go = NewUI("ConfirmButton", parent);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 110f;
        le.preferredHeight = 110f;

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.17f, 0.72f, 0.38f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = bg;

        GameObject labelGO = NewUI("Label", go.transform);
        Stretch(labelGO);

        TMP_Text label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = "Continue";
        label.fontSize = 40f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;

        var loc = labelGO.AddComponent<LocalizedText>();
        loc.EditorSetKey("language.continue");

        return button;
    }

    private static void MakeLabel(Transform parent, string name, string text, float size,
                                  FontStyles style, Color color, float height, string key)
    {
        GameObject go = NewUI(name, parent);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;

        TMP_Text label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;

        if (!string.IsNullOrEmpty(key))
            go.AddComponent<LocalizedText>().EditorSetKey(key);
    }

    // ── Small helpers ─────────────────────────────────────────────────────────

    private static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        return go;
    }

    private static void Stretch(GameObject go)
    {
        RectTransform r = go.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    private static void Set(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) p.objectReferenceValue = value;
        else Debug.LogWarning($"[Localization] no serialized field '{field}' on LanguageSelectPopup.");
    }

    // ── Testing helpers ───────────────────────────────────────────────────────

    [MenuItem("Real Bingo/Localization/Reset Saved Language (show the popup again)")]
    private static void ResetLanguage()
    {
        PlayerPrefs.DeleteKey(LocalizationManager.PREF_KEY);
        PlayerPrefs.Save();
        Debug.Log("[Localization] cleared the saved language — the popup shows on the next Play.");
    }

    [MenuItem("Real Bingo/Localization/Switch Language/English")]
    private static void SwitchEn() => SwitchTo("en");

    [MenuItem("Real Bingo/Localization/Switch Language/हिन्दी (Hindi)")]
    private static void SwitchHi() => SwitchTo("hi");

    [MenuItem("Real Bingo/Localization/Switch Language/मराठी (Marathi)")]
    private static void SwitchMr() => SwitchTo("mr");

    /// <summary>
    /// Flips the language while the game is running, so you can check every
    /// screen without restarting. Outside Play mode it just records the choice
    /// for the next run — there is no live manager to tell.
    /// </summary>
    private static void SwitchTo(string code)
    {
        if (Application.isPlaying && LocalizationManager.Instance != null)
        {
            LocalizationManager.Instance.SetLanguage(code);
            Debug.Log($"[Localization] switched to '{code}' live.");
            return;
        }

        PlayerPrefs.SetString(LocalizationManager.PREF_KEY, code);
        PlayerPrefs.Save();
        Debug.Log($"[Localization] saved '{code}' — it applies on the next Play.");
    }
}
