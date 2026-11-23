using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Menu: Real Bingo / Fixes / Build Help Screens
///
/// Fills in the FAQ with real questions and builds the two screens next to it:
///
///   UICanvas/HowToPlayPanel   a plain scrollable page of rules
///   UICanvas/HelpPanel        phone, WhatsApp and email, each tappable
///
/// and wires the "How to Play" and "Help" buttons in the profile menu to them,
/// the same way the FAQ button was wired - a persistent SetActive(true) straight
/// on the button, with the close button on each panel set to SetActive(false).
///
/// ── About the content ────────────────────────────────────────────────────────
///
/// Everything in the FAQ and the How to Play page is taken from what the game
/// actually does: 0-9 plus J, 60 second rounds with betting closing just before
/// the spin, 9x on a number and 20x on J (GameManager.payoutMultiplier and
/// jokerPayoutMultiplier), Repeat / Undo, Last 10 Results, the referral screen,
/// and the settings switches.
///
/// It deliberately states NO policy: no withdrawal times, no fees, no minimum or
/// maximum amounts, no bonus terms, no KYC or age rules. Those are commercial and
/// legal commitments in a real money app, they are not in the code, and they are
/// not mine to invent. Write them yourself before release.
///
/// THE PHONE NUMBER AND EMAIL BELOW ARE PLACEHOLDERS and must be replaced. The
/// address uses the reserved .example domain precisely so it cannot work by
/// accident and cannot belong to anyone else.
///
/// Safe to run twice: each panel is skipped if it already exists, and the FAQ
/// entries are only replaced while they are still the three samples.
/// </summary>
public static class RBHelpPanelsBuilder
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string AutoRunKey = "RBHelp.ScreensBuilt.v1";

    // ── PLACEHOLDERS - replace all three before release ──────────────────────
    private const string SupportNumberDisplay = "+91 90000 00000";
    private const string SupportNumberDigits = "919000000000";      // wa.me wants digits only
    private const string SupportEmail = "support@realbingo.example";

    private const float PopUpWidth = 1200f;
    private const float PopUpHeight = 800f;
    private const float HelpWidth = 900f;
    private const float HelpHeight = 620f;

    private static readonly Color DimmerColor = new Color(0f, 0f, 0f, 0.69f);
    private static readonly Color PopUpColor = new Color(0.16f, 0.07f, 0.24f, 1f);
    private static readonly Color PageColor = new Color(0.11f, 0.05f, 0.17f, 1f);
    private static readonly Color AccentColor = new Color(1f, 0.85f, 0.45f, 1f);
    private static readonly Color BodyTextColor = new Color(0.90f, 0.88f, 0.95f, 1f);
    private static readonly Color CloseColor = new Color(0.75f, 0.18f, 0.22f, 1f);
    private static readonly Color WhatsAppColor = new Color(0.14f, 0.83f, 0.40f, 1f);
    private static readonly Color RowColor = new Color(0.22f, 0.10f, 0.32f, 1f);

    private static readonly (string q, string a)[] Faq =
    {
        ("How do I place a bet?",
         "Pick a chip at the bottom of the screen, then tap a number from 0 to 9, or the J slot. " +
         "That amount leaves your wallet and appears on the slot. Tap the same slot again to add more, " +
         "and spread chips across as many numbers as you like in one round."),

        ("What is J?",
         "J is the Joker - an eleventh target alongside the numbers 0 to 9. It pays more than a number " +
         "does, and you can stake any chip on it, exactly like the rest."),

        ("How much do I win?",
         "A winning number pays 9 times your stake on it, and J pays 20 times. The game reads these " +
         "rates from the server at the start of a round, so always go by what the game shows you."),

        ("How long does a round last?",
         "About 60 seconds. The timer shows how long you have left, and betting closes a moment before " +
         "the wheel starts to spin. Bets already placed stay on the table."),

        ("Can I take a bet back?",
         "Undo removes the last chip you placed and returns it to your wallet, and you can keep tapping " +
         "it to walk back further. Both only work while betting is still open."),

        ("What does Repeat do?",
         "It places the same bets you made in the previous round, in one tap. If your wallet cannot " +
         "cover all of them, whatever it can cover is placed."),

        ("What are the Last 10 Results?",
         "The row of numbers near the top left is the last ten winning numbers, most recent first. " +
         "It is there to look back at - it does not predict anything, and every spin is independent."),

        ("How does Refer and Earn work?",
         "Refer and Earn shows your own referral code and a Share button. When somebody signs up using " +
         "your code you earn a bonus, and that screen also shows how many people have joined through " +
         "you and how much you have earned in total."),

        ("How do I add money or cash out?",
         "Add Cash tops up your wallet. To cash out, open your profile and use the withdraw option. " +
         "You may be asked to complete your profile and bank details first."),

        ("Can I play in Hindi or Marathi?",
         "Yes. Settings has English, Hindi and Marathi, and the whole game changes language straight " +
         "away - you do not need to restart it."),

        ("How do I turn the sound or notifications off?",
         "Settings has separate switches for notifications, vibration, button sounds and background " +
         "music, plus a slider for music volume. They are remembered between sessions."),

        ("Something looks wrong with my wallet.",
         "Winnings are added to your wallet as soon as the round finishes, and the amount is shown on " +
         "screen. If a balance still looks wrong, get in touch from the Help screen with the time of " +
         "the round and what you expected."),
    };

    private const string HowToPlayBody =
        "<b>The idea</b>\n" +
        "Each round the wheel stops on one of eleven targets: the numbers 0 to 9, or J, the Joker. " +
        "You win if you have a chip on whatever it stops on.\n\n" +

        "<b>1. Pick a chip</b>\n" +
        "The chips run along the bottom of the screen. Tap one to select it - that is how much goes " +
        "down each time you tap a slot. Any chip can go on any target, J included.\n\n" +

        "<b>2. Place your bets</b>\n" +
        "Tap a number from 0 to 9, or J. The stake leaves your wallet and shows on that slot. Tap the " +
        "same slot again to raise it, or back as many numbers as you like in the same round.\n\n" +

        "<b>3. Watch the timer</b>\n" +
        "A round runs for about 60 seconds. Betting closes a moment before the wheel starts to turn, " +
        "so get your chips down before the timer runs out.\n\n" +

        "<b>4. The spin</b>\n" +
        "The wheel spins and settles on the winning target. It is then added to Last 10 Results.\n\n" +

        "<b>5. Getting paid</b>\n" +
        "A winning number pays <b>9x</b> your stake on it. J pays <b>20x</b>. Winnings go straight into " +
        "your wallet and the amount is shown on screen. Stakes on every other target are lost.\n\n" +

        "<b>Repeat, Undo and the result strip</b>\n" +
        "Repeat puts down the same bets as last round in one tap. Undo takes back the chip you placed " +
        "last and returns it to your wallet. Last 10 Results is a record of recent winning numbers - " +
        "every spin is independent of the ones before it.\n\n" +

        "<b>Play sensibly</b>\n" +
        "Only stake money you can afford to lose, and take a break when you need one.";

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
                Debug.Log("[RBHelp] Ready, but GameScene is not the open scene. Open GameScene and " +
                          "refresh (Ctrl+R), or run Real Bingo / Fixes / Build Help Screens.");
                return;
            }

            EditorPrefs.SetBool(AutoRunKey, true);
            BuildHelpScreens();
        };
    }

    [MenuItem("Real Bingo/Fixes/Build Help Screens", priority = 16)]
    public static void BuildHelpScreens()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[RBHelp] Stop Play mode first.");
            return;
        }

        if (EditorSceneManager.GetActiveScene().path != GameScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        }

        Canvas uiCanvas = null;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.name == "UICanvas") { uiCanvas = c; break; }

        if (uiCanvas == null)
        {
            Debug.LogWarning("[RBHelp] No canvas called \"UICanvas\" in GameScene.");
            return;
        }

        var log = new StringBuilder();

        FillFaqContent(log);
        BuildHowToPlay(uiCanvas, log);
        BuildHelp(uiCanvas, log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        log.AppendLine();
        log.AppendLine("PLACEHOLDERS to replace before release:");
        log.AppendLine($"   phone / WhatsApp : {SupportNumberDisplay}");
        log.AppendLine($"   email            : {SupportEmail}");
        log.AppendLine();
        log.AppendLine("Scene marked dirty - press Ctrl+S to keep it.");

        Debug.Log("[RBHelp]\n" + log);
        EditorUtility.DisplayDialog("Help screens built", log.ToString(), "OK");
    }

    // ── FAQ content ──────────────────────────────────────────────────────────

    private static void FillFaqContent(StringBuilder log)
    {
        FAQAccordion accordion = Object.FindFirstObjectByType<FAQAccordion>(FindObjectsInactive.Include);
        if (accordion == null) { log.AppendLine("FAQ: no FAQAccordion in the scene, skipped."); return; }

        var so = new SerializedObject(accordion);
        SerializedProperty entries = so.FindProperty("entries");
        if (entries == null) { log.AppendLine("FAQ: no 'entries' field, skipped."); return; }

        // Only overwrite the samples this session added, never real content.
        bool isSamples = entries.arraySize == 0 ||
                         (entries.arraySize == 3 &&
                          entries.GetArrayElementAtIndex(0).FindPropertyRelative("question")
                                 .stringValue.StartsWith("Sample question"));

        if (!isSamples)
        {
            log.AppendLine($"FAQ: {entries.arraySize} entries already written by hand - left alone.");
            return;
        }

        entries.ClearArray();
        for (int i = 0; i < Faq.Length; i++)
        {
            entries.InsertArrayElementAtIndex(i);
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("question").stringValue = Faq[i].q;
            e.FindPropertyRelative("answer").stringValue = Faq[i].a;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(accordion);
        log.AppendLine($"FAQ: wrote {Faq.Length} questions.");
    }

    // ── How to Play ──────────────────────────────────────────────────────────

    private static void BuildHowToPlay(Canvas uiCanvas, StringBuilder log)
    {
        if (Exists(uiCanvas, "HowToPlayPanel")) { log.AppendLine("HowToPlayPanel: already there."); return; }

        RectTransform popUp = MakePanel(uiCanvas, "HowToPlayPanel", 12, PopUpWidth, PopUpHeight,
                                        "How to Play", out GameObject panel);

        // A single scrolling column of text - no accordion, nothing to tap.
        RectTransform scroll = NewUI("Scroll View", popUp);
        Stretch(scroll, 40f, 40f, 130f, 40f);
        Image scrollBg = scroll.gameObject.AddComponent<Image>();
        scrollBg.sprite = Builtin("UI/Skin/UISprite.psd");
        scrollBg.type = Image.Type.Sliced;
        scrollBg.color = PageColor;
        ScrollRect scrollRect = scroll.gameObject.AddComponent<ScrollRect>();

        RectTransform viewport = NewUI("Viewport", scroll);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.pivot = new Vector2(0f, 1f);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.sprite = Builtin("UI/Skin/UISprite.psd");
        viewportImage.type = Image.Type.Sliced;
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        RectTransform content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.offsetMin = new Vector2(28f, content.offsetMin.y);
        content.offsetMax = new Vector2(-28f, content.offsetMax.y);

        TextMeshProUGUI page = content.gameObject.AddComponent<TextMeshProUGUI>();
        page.text = HowToPlayBody;
        page.fontSize = 28f;
        page.color = BodyTextColor;
        page.alignment = TextAlignmentOptions.TopLeft;
        page.margin = new Vector4(0f, 24f, 0f, 24f);
        page.raycastTarget = false;

        // The text sizes itself, and that height is what the scroll view scrolls.
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = content;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.scrollSensitivity = 30f;

        WireMenuButton("How to Play", panel, log);
        log.AppendLine("HowToPlayPanel: built, with the rules written out.");
    }

    // ── Help ─────────────────────────────────────────────────────────────────

    private static void BuildHelp(Canvas uiCanvas, StringBuilder log)
    {
        if (Exists(uiCanvas, "HelpPanel")) { log.AppendLine("HelpPanel: already there."); return; }

        RectTransform popUp = MakePanel(uiCanvas, "HelpPanel", 12, HelpWidth, HelpHeight,
                                        "Help & Support", out GameObject panel);

        RectTransform intro = NewUI("Intro", popUp);
        intro.anchorMin = new Vector2(0f, 1f);
        intro.anchorMax = new Vector2(1f, 1f);
        intro.pivot = new Vector2(0.5f, 1f);
        intro.anchoredPosition = new Vector2(0f, -120f);
        intro.sizeDelta = new Vector2(-120f, 70f);
        TextMeshProUGUI introText = intro.gameObject.AddComponent<TextMeshProUGUI>();
        introText.text = "Stuck on something? Get in touch and we will help.";
        introText.fontSize = 26f;
        introText.color = BodyTextColor;
        introText.alignment = TextAlignmentOptions.Center;
        introText.raycastTarget = false;

        float y = -210f;
        MakeContactRow(popUp, "PhoneRow", "Call us", SupportNumberDisplay,
                       $"tel:+{SupportNumberDigits}", AccentColor, ref y);
        MakeContactRow(popUp, "WhatsAppRow", "Chat on WhatsApp", SupportNumberDisplay,
                       $"https://wa.me/{SupportNumberDigits}", WhatsAppColor, ref y);
        MakeContactRow(popUp, "EmailRow", "Email us", SupportEmail,
                       $"mailto:{SupportEmail}", AccentColor, ref y);

        RectTransform note = NewUI("Note", popUp);
        note.anchorMin = new Vector2(0f, 0f);
        note.anchorMax = new Vector2(1f, 0f);
        note.pivot = new Vector2(0.5f, 0f);
        note.anchoredPosition = new Vector2(0f, 30f);
        note.sizeDelta = new Vector2(-120f, 60f);
        TextMeshProUGUI noteText = note.gameObject.AddComponent<TextMeshProUGUI>();
        noteText.text = "Support hours and languages go here.";
        noteText.fontSize = 22f;
        noteText.color = new Color(0.65f, 0.62f, 0.72f, 1f);
        noteText.alignment = TextAlignmentOptions.Center;
        noteText.raycastTarget = false;

        WireMenuButton("Help", panel, log);
        log.AppendLine("HelpPanel: built, with phone, WhatsApp and email rows.");
    }

    /// <summary>
    /// One tappable contact row: icon, what it is, and the number or address in
    /// full. The text is spelled out rather than hidden behind the icon so it can
    /// still be read and copied on a device with no WhatsApp or no mail client.
    /// </summary>
    private static void MakeContactRow(RectTransform parent, string name, string label,
                                       string value, string url, Color iconColor, ref float y)
    {
        RectTransform row = NewUI(name, parent);
        row.anchorMin = new Vector2(0.5f, 1f);
        row.anchorMax = new Vector2(0.5f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, y);
        row.sizeDelta = new Vector2(HelpWidth - 120f, 100f);
        y -= 118f;

        Image rowBg = row.gameObject.AddComponent<Image>();
        rowBg.sprite = Builtin("UI/Skin/UISprite.psd");
        rowBg.type = Image.Type.Sliced;
        rowBg.color = RowColor;

        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = rowBg;

        // The icon. Built-in placeholder sprite in the right colour - drop the
        // real WhatsApp mark in here, it is deliberately not drawn by hand.
        RectTransform icon = NewUI(name == "WhatsAppRow" ? "WhatsAppIcon" : "Icon", row);
        icon.anchorMin = new Vector2(0f, 0.5f);
        icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0f, 0.5f);
        icon.anchoredPosition = new Vector2(24f, 0f);
        icon.sizeDelta = new Vector2(60f, 60f);
        Image iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.sprite = Builtin("UI/Skin/Knob.psd");
        iconImage.color = iconColor;
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;

        RectTransform text = NewUI("Label", row);
        Stretch(text, 110f, 24f, 0f, 0f);
        TextMeshProUGUI tmp = text.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = $"{label}\n<size=80%><color=#CFC8DC>{value}</color></size>";
        tmp.fontSize = 28f;
        tmp.color = AccentColor;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;

        ContactLink link = row.gameObject.AddComponent<ContactLink>();
        var so = new SerializedObject(link);
        so.FindProperty("url").stringValue = url;
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddVoidPersistentListener(button.onClick, new UnityAction(link.Open));
    }

    // ── Shared panel shell ───────────────────────────────────────────────────

    private static RectTransform MakePanel(Canvas uiCanvas, string panelName, int sortingOrder,
                                           float width, float height, string title,
                                           out GameObject panelGo)
    {
        RectTransform panel = NewUI(panelName, uiCanvas.transform);
        Stretch(panel, 0f, 0f, 0f, 0f);
        panelGo = panel.gameObject;

        Image dim = panelGo.AddComponent<Image>();
        dim.sprite = Builtin("UI/Skin/UISprite.psd");
        dim.type = Image.Type.Sliced;
        dim.color = DimmerColor;

        Canvas canvas = panelGo.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
        panelGo.AddComponent<GraphicRaycaster>();

        // Fixed, centre-anchored - the same shape the Refer and Earn fix settled on.
        RectTransform popUp = NewUI("PopUp", panel);
        popUp.anchorMin = popUp.anchorMax = popUp.pivot = new Vector2(0.5f, 0.5f);
        popUp.anchoredPosition = Vector2.zero;
        popUp.sizeDelta = new Vector2(width, height);
        Image popBg = popUp.gameObject.AddComponent<Image>();
        popBg.sprite = Builtin("UI/Skin/UISprite.psd");
        popBg.type = Image.Type.Sliced;
        popBg.color = PopUpColor;

        RectTransform titleRt = NewUI("Title", popUp);
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -30f);
        titleRt.sizeDelta = new Vector2(-200f, 80f);
        TextMeshProUGUI titleText = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        titleText.text = title;
        titleText.fontSize = 44f;
        titleText.color = AccentColor;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.raycastTarget = false;

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
            closeButton.onClick, new UnityAction<bool>(panelGo.SetActive), false);

        RectTransform x = NewUI("X", close);
        Stretch(x, 0f, 0f, 0f, 0f);
        TextMeshProUGUI xText = x.gameObject.AddComponent<TextMeshProUGUI>();
        xText.text = "X";
        xText.fontSize = 40f;
        xText.color = Color.white;
        xText.alignment = TextAlignmentOptions.Center;
        xText.raycastTarget = false;

        Undo.RegisterCreatedObjectUndo(panelGo, "Build Help Screens");
        panelGo.SetActive(false);
        return popUp;
    }

    private static void WireMenuButton(string buttonName, GameObject panel, StringBuilder log)
    {
        foreach (Button b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b.name != buttonName) continue;

            // Do not stack a second opener on a button that already has one.
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            {
                if (b.onClick.GetPersistentTarget(i) == panel)
                {
                    log.AppendLine($"   \"{buttonName}\" was already wired.");
                    return;
                }
            }

            UnityEventTools.AddBoolPersistentListener(b.onClick, new UnityAction<bool>(panel.SetActive), true);
            EditorUtility.SetDirty(b);
            log.AppendLine($"   wired \"{buttonName}\" -> {panel.name}.SetActive(true)");
            return;
        }

        log.AppendLine($"   no button called \"{buttonName}\" found - wire it by hand.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool Exists(Canvas uiCanvas, string name)
    {
        foreach (RectTransform t in uiCanvas.GetComponentsInChildren<RectTransform>(true))
            if (t.name == name) return true;
        return false;
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
        if (s == null) Debug.LogWarning($"[RBHelp] built-in sprite not found: {path}");
        return s;
    }
}
