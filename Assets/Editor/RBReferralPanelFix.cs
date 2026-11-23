using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: Real Bingo / Fixes / Fix Refer and Earn Panel Scaling
///
/// The Refer and Earn popup was the one screen that changed shape from device to
/// device. Its frame - ReferAndEarn/PopUp - was anchored to all four corners of the
/// full-screen dimmer with a fixed inset (sizeDelta -1100, -450), so its size was
/// "screen minus 1100 x 450" rather than a size of its own.
///
/// The canvas scaler is Scale With Screen Size / Expand against 1920x1080, which
/// keeps 1920x1080 in ONE axis and grows the other:
///
///     19.5:9 phone  -> canvas 2340x1080 -> popup 1240x630   (aspect 1.97)
///     16:9  phone   -> canvas 1920x1080 -> popup  820x630   (aspect 1.30)
///     4:3   tablet  -> canvas 1920x1440 -> popup  820x990   (aspect 0.83)
///
/// Every child inside it is a fixed size pinned to an edge or to the centre, so the
/// frame stretched while the contents did not: on a tablet the title floated 200
/// units below the frame's painted title tab and the buttons sank to the bottom; on
/// a wide phone the frame was stretched 30% horizontally. The background sprite is
/// Simple with no 9-slice border (border 0,0,0,0), so each of those aspect ratios
/// visibly distorted the gold frame.
///
/// The fix is the pattern the rest of the game already uses - SettingPanel/Popup is
/// a fixed 1650x950 centred rect inside a full-screen dimmer. This gives the popup
/// one fixed size, taken from its own sprite so the frame is never distorted, then
/// tidies the two children whose placement only made sense against a stretched
/// frame:
///
///   - PopUp            : centre-anchored, fixed size at the sprite's native aspect
///   - Title-holderImg  : narrowed to sit inside the frame's painted title tab
///   - announcement_img : the megaphone, scaled into the bottom-left margin so it
///                        no longer lands on top of the stats panel
///
/// Nothing else moves: every other child was already centre- or edge-anchored and
/// is correct once the frame stops changing size.
///
/// Safe to run twice, and a single undo step.
/// </summary>
public static class RBReferralPanelFix
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";

    /// <summary>
    /// Height of the popup in canvas units. Expand keeps one axis at the reference
    /// size and grows the other, so the canvas is never SMALLER than 1920x1080
    /// reference units on any aspect ratio - which is what makes a fixed size safe
    /// here: 636 always fits, with at least 220 units of dimmer above and below.
    /// The width comes from the sprite, never from a guess.
    /// </summary>
    private const float PopUpHeight = 636f;

    /// <summary>
    /// How much of the frame's width the painted title tab covers, measured off the
    /// source sheet (the pill is 510px wide on a 779px frame).
    /// </summary>
    private const float TitleTabWidthFraction = 0.66f;

    /// <summary>The frame sprite's native size, used only if the sprite is missing.</summary>
    private const float FallbackAspect = 779f / 516f;

    /// <summary>
    /// Set once the fix has actually been applied, so this never runs a second time.
    /// EditorPrefs rather than SessionState: it has to survive restarting Unity.
    /// </summary>
    private const string AutoRunKey = "RBReferral.AutoRunDone.v1";

    /// <summary>
    /// Runs the fix by itself on the next domain reload, so it does not depend on
    /// finding a menu item. It fires at most once ever, only outside Play mode, and
    /// only while GameScene is the open scene; it marks the scene dirty but never
    /// saves, so keeping the result is still a deliberate Ctrl+S.
    ///
    /// To cancel it before it fires, delete this file. To let it run again, clear
    /// the key with: Real Bingo / Fixes / Reset Refer and Earn Auto-Run.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        if (EditorPrefs.GetBool(AutoRunKey, false)) return;

        // Wait until the editor is idle and the scene is actually loaded.
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(AutoRunKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (EditorSceneManager.GetActiveScene().path != GameScenePath)
            {
                Debug.Log("[RBReferral] Ready, but GameScene is not the open scene - " +
                          $"\"{EditorSceneManager.GetActiveScene().path}\" is. Open GameScene and " +
                          "refresh (Ctrl+R), or run Real Bingo / Fixes / Fix Refer and Earn Panel Scaling.");
                return;
            }

            // Claim the run before doing the work, so a failure part way through
            // cannot leave this firing on every reload.
            EditorPrefs.SetBool(AutoRunKey, true);
            Debug.Log("[RBReferral] Applying the Refer and Earn scaling fix automatically (one time).");
            FixReferralPanelScaling();
        };
    }

    [MenuItem("Real Bingo/Fixes/Reset Refer and Earn Auto-Run", priority = 13)]
    private static void ResetAutoRun()
    {
        EditorPrefs.DeleteKey(AutoRunKey);
        Debug.Log("[RBReferral] Auto-run cleared - it will apply again on the next script reload.");
    }

    [MenuItem("Real Bingo/Fixes/Fix Refer and Earn Panel Scaling", priority = 12)]
    public static void FixReferralPanelScaling()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            const string msg = "Stop Play mode first - scene changes cannot be saved while the game is running.";
            Debug.Log("[RBReferral] " + msg);
            EditorUtility.DisplayDialog("Not in Play mode", msg, "OK");
            return;
        }

        // Work on the scene as it is open, unsaved edits included. Only open
        // GameScene if it is not the one in front of us.
        if (EditorSceneManager.GetActiveScene().path != GameScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[RBReferral] cancelled - nothing was changed.");
                return;
            }
            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        }

        RectTransform popUp = FindPopUp(out string why);
        if (popUp == null)
        {
            Report(false, why);
            return;
        }

        var log = new StringBuilder();

        // 1. The frame itself.
        float aspect = FallbackAspect;
        Image frame = popUp.GetComponent<Image>();
        if (frame != null && frame.sprite != null && frame.sprite.rect.height > 0f)
            aspect = frame.sprite.rect.width / frame.sprite.rect.height;

        float targetW = Mathf.Round(PopUpHeight * aspect);
        Vector2 targetSize = new Vector2(targetW, PopUpHeight);

        log.AppendLine($"PopUp was: anchors {popUp.anchorMin} - {popUp.anchorMax}, " +
                       $"sizeDelta {popUp.sizeDelta}, actual rect {popUp.rect.size}");

        Undo.RecordObject(popUp, "Fix Refer and Earn Panel Scaling");
        popUp.anchorMin = new Vector2(0.5f, 0.5f);
        popUp.anchorMax = new Vector2(0.5f, 0.5f);
        popUp.pivot = new Vector2(0.5f, 0.5f);
        popUp.anchoredPosition = Vector2.zero;
        popUp.sizeDelta = targetSize;
        EditorUtility.SetDirty(popUp);

        log.AppendLine($"PopUp now: centre-anchored, fixed {targetSize.x} x {targetSize.y} " +
                       $"(sprite aspect {aspect:F3}, so the frame art is no longer stretched)");

        // 2. The title, into the frame's painted tab.
        RectTransform title = Find(popUp, "Title-holderImg");
        if (title == null)
        {
            log.AppendLine("Title-holderImg: not found, skipped.");
        }
        else
        {
            float content = 0f;                      // icon + label at their own widths
            for (int i = 0; i < title.childCount; i++)
            {
                RectTransform c = title.GetChild(i) as RectTransform;
                if (c != null && c.gameObject.activeSelf) content += c.rect.width;
            }

            float tab = Mathf.Max(content, Mathf.Round(targetSize.x * TitleTabWidthFraction));

            if (tab < title.sizeDelta.x - 0.5f)
            {
                Undo.RecordObject(title, "Fix Refer and Earn Panel Scaling");
                log.AppendLine($"Title-holderImg: width {title.sizeDelta.x} -> {tab} " +
                               $"(the painted tab is about {TitleTabWidthFraction:P0} of the frame; " +
                               $"icon + label need {content})");
                title.sizeDelta = new Vector2(tab, title.sizeDelta.y);
                EditorUtility.SetDirty(title);
            }
            else
            {
                log.AppendLine($"Title-holderImg: width {title.sizeDelta.x} already fits the tab.");
            }
        }

        // 3. The megaphone, out of the stats panel.
        RectTransform mega = Find(popUp, "announcement_img");
        RectTransform panel = Find(popUp, "Panel");

        if (mega == null)
        {
            log.AppendLine("announcement_img: not found, skipped.");
        }
        else
        {
            const float inset = 18f;
            const float clear = 6f;

            // The free strip is everything left of the stats panel.
            float panelHalf = panel != null ? panel.rect.width * 0.5f : targetSize.x * 0.35f;
            float strip = (targetSize.x * 0.5f) - panelHalf - inset - clear;

            float megaAspect = mega.sizeDelta.y > 0f ? mega.sizeDelta.x / mega.sizeDelta.y : 0.857f;
            float w = Mathf.Min(mega.sizeDelta.x, Mathf.Max(60f, Mathf.Round(strip)));
            float h = Mathf.Round(w / megaAspect);

            Undo.RecordObject(mega, "Fix Refer and Earn Panel Scaling");
            log.AppendLine($"announcement_img: {mega.sizeDelta} at {mega.anchoredPosition} -> " +
                           $"({w}, {h}) at ({inset}, {inset}), inside the {strip:F0}-unit strip " +
                           $"left of the stats panel, aspect kept");
            mega.anchorMin = Vector2.zero;
            mega.anchorMax = Vector2.zero;
            mega.pivot = Vector2.zero;
            mega.sizeDelta = new Vector2(w, h);
            mega.anchoredPosition = new Vector2(inset, inset);
            EditorUtility.SetDirty(mega);
        }

        log.AppendLine();
        log.AppendLine("The popup is now one fixed size instead of \"screen minus 1100 x 450\", so");
        log.AppendLine("19.5:9 phone, 16:9 phone and 4:3 tablet all draw it identically. Expand");
        log.AppendLine("never makes the canvas smaller than 1920x1080 reference units, so it fits");
        log.AppendLine("with room to spare on every one of them.");

        Report(true, log.ToString());
    }

    // Helpers.

    private static RectTransform FindPopUp(out string why)
    {
        why = null;

        // Go through the script, so we fix the panel the game actually opens.
        ReferAndEarnPanel script =
            Object.FindFirstObjectByType<ReferAndEarnPanel>(FindObjectsInactive.Include);

        GameObject root = null;

        if (script != null)
        {
            var so = new SerializedObject(script);
            SerializedProperty prop = so.FindProperty("panelParent");
            if (prop != null) root = prop.objectReferenceValue as GameObject;
        }

        if (root == null)
        {
            foreach (RectTransform t in Object.FindObjectsByType<RectTransform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == "ReferAndEarn") { root = t.gameObject; break; }
            }
        }

        if (root == null)
        {
            why = "Could not find the Refer and Earn panel: ReferAndEarnPanel.panelParent is " +
                  "unassigned and no object called \"ReferAndEarn\" exists in the open scene.";
            return null;
        }

        RectTransform popUp = Find(root.transform as RectTransform, "PopUp");
        if (popUp == null)
            why = $"\"{root.name}\" has no child called \"PopUp\" - nothing to resize.";

        return popUp;
    }

    private static RectTransform Find(RectTransform parent, string name)
    {
        if (parent == null) return null;

        foreach (RectTransform t in parent.GetComponentsInChildren<RectTransform>(true))
            if (t != parent && t.name == name) return t;

        return null;
    }

    private static void Report(bool changed, string message)
    {
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            message += "\nScene marked dirty - press Ctrl+S to keep it.";
        }

        Debug.Log("[RBReferral]\n" + message);
        EditorUtility.DisplayDialog(changed ? "Refer and Earn panel fixed" : "Nothing to change",
                                    message, "OK");
    }
}
