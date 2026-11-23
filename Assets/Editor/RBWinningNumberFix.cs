using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu:  Real Bingo ▸ Fixes ▸ Restore Winning Number Display
///        Real Bingo ▸ Fixes ▸ Fix J and OK Button Labels
///
/// Two scene repairs that have to run as Editor commands rather than as direct
/// edits to GameScene.unity: the Editor keeps the open scene in memory and
/// rewrites the file from that on save, so anything written to the YAML behind
/// its back is liable to be thrown away the next time you press Ctrl+S.
///
/// ── Restore Winning Number Display ───────────────────────────────────────────
/// UIManager.winningNumberText is None. It used to point at an object called
/// "WinningNumber Text (TMP)" inside the WinningAmt box; that object was deleted
/// and Unity reset the field on the next save.
///
/// UIManager.ShowResult writes BetManager.Label(number) — "0".."9", or "J" — into
/// that field and pops it in with a scale tween. The call is null-guarded, so
/// nothing errors: the winning number simply never appears, and the clear in
/// SetBettingInteractable does nothing. This recreates the object next to the
/// win-amount text and reassigns the field.
///
/// The new label is a COPY of the win-amount text, so it inherits the same font
/// asset, material, size and colour rather than guessing at them. It is set to
/// left alignment while the amount stays centred, which keeps the two legible in
/// one box. Move or resize it afterwards if the designer wants it elsewhere —
/// the field stays wired.
///
/// ── Fix J and OK Button Labels ───────────────────────────────────────────────
/// Two labels were duplicated from a neighbour and never retyped:
///     • the Joker button (myNumber 10) reads "4", so two buttons read "4" and
///       none reads "J" — the 20x target is unfindable
///     • UIManager.okButton reads "Cancel", so there are two Cancel buttons
///
/// The OK one matters before a localization pass: RBLocalizeEverything keys a
/// label by matching its TEXT against the sheet, so left as "Cancel" the OK
/// button would be bound to common.cancel and read "Cancel" in all three
/// languages.
///
/// Both commands are safe to run twice — each checks before it changes anything
/// — and each is a single undo step.
/// </summary>
public static class RBWinningNumberFix
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string WinningNumberObjectName = "WinningNumber Text (TMP)";

    // ── Winning number ───────────────────────────────────────────────────────

    [MenuItem("Real Bingo/Fixes/Restore Winning Number Display", priority = 10)]
    public static void RestoreWinningNumberDisplay()
    {
        if (!TryOpenGameScene(out Scene scene)) return;

        var report = new StringBuilder();
        bool changed = false;

        UIManager ui = Object.FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        if (ui == null)
        {
            Finish(scene, false, "No UIManager in GameScene — nothing to wire.");
            return;
        }

        if (ui.winningNumberText != null)
        {
            Finish(scene, false,
                $"winningNumberText is already assigned to \"{ui.winningNumberText.name}\". Nothing to do.");
            return;
        }

        // The win-amount text is the template AND tells us which box to build in.
        TMP_Text amount = ui.resultText;
        if (amount == null)
        {
            Finish(scene, false,
                "UIManager.resultText is also unassigned, so there is no box to build in and " +
                "nothing to copy the font from. Assign resultText first, then run this again.");
            return;
        }

        Transform box = amount.transform.parent;
        if (box == null)
        {
            Finish(scene, false, $"\"{amount.name}\" has no parent to add the number next to.");
            return;
        }

        // Someone may have recreated the object by hand without wiring it up.
        Transform existing = box.Find(WinningNumberObjectName);
        TMP_Text label = existing != null ? existing.GetComponent<TMP_Text>() : null;

        if (label != null)
        {
            report.AppendLine($"Found an existing \"{WinningNumberObjectName}\" under {Path(box)} — reusing it.");
        }
        else
        {
            GameObject copy = Object.Instantiate(amount.gameObject, box, false);
            copy.name = WinningNumberObjectName;
            Undo.RegisterCreatedObjectUndo(copy, "Restore Winning Number Display");

            label = copy.GetComponent<TMP_Text>();

            // A copied binder would overwrite the number on every language
            // change. The number is a digit or "J" — it is never translated.
            foreach (var binder in copy.GetComponents<RealBingo.Localization.LocalizedText>())
                Object.DestroyImmediate(binder);

            label.text = "";
            label.alignment = TextAlignmentOptions.Left;

            report.AppendLine($"Created \"{copy.name}\" under {Path(box)}, copied from \"{amount.name}\".");
            report.AppendLine($"   font/size/colour inherited; alignment set to Left (the amount stays centred).");
            changed = true;
        }

        Undo.RecordObject(ui, "Restore Winning Number Display");
        ui.winningNumberText = label;
        EditorUtility.SetDirty(ui);
        changed = true;

        report.AppendLine($"Assigned UIManager.winningNumberText -> \"{label.name}\".");
        report.AppendLine();
        report.AppendLine("The winning number (and \"J\") will now show on every result, with its scale pop.");
        report.AppendLine("Reposition or resize it in the Scene view if the designer wants it elsewhere —");
        report.AppendLine("the reference stays wired.");

        Finish(scene, changed, report.ToString());
    }

    // ── Button labels ────────────────────────────────────────────────────────

    [MenuItem("Real Bingo/Fixes/Fix J and OK Button Labels", priority = 11)]
    public static void FixButtonLabels()
    {
        if (!TryOpenGameScene(out Scene scene)) return;

        var report = new StringBuilder();
        bool changed = false;

        // Joker button: found by myNumber, never by name or by what it says —
        // its text is exactly the thing that is wrong.
        foreach (NumberButton nb in Object.FindObjectsByType<NumberButton>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!BetManager.IsJoker(nb.myNumber)) continue;

            TMP_Text label = nb.GetComponentInChildren<TMP_Text>(true);
            if (label == null) { report.AppendLine($"Joker button \"{nb.name}\" has no text child."); continue; }

            string want = BetManager.Label(nb.myNumber);   // "J"
            if (label.text == want)
            {
                report.AppendLine($"Joker button already reads \"{want}\".");
            }
            else
            {
                Undo.RecordObject(label, "Fix J Button Label");
                report.AppendLine($"Joker button \"{nb.name}\": \"{label.text}\" -> \"{want}\".");
                label.text = want;
                EditorUtility.SetDirty(label);
                changed = true;
            }
        }

        // OK button, via UIManager so we fix the one the game actually uses
        // rather than whichever object happens to be called "OK".
        UIManager ui = Object.FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        Button ok = ui != null ? ui.okButton : null;

        if (ok == null)
        {
            report.AppendLine("UIManager.okButton is not assigned — skipped.");
        }
        else
        {
            TMP_Text label = ok.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
            {
                report.AppendLine($"OK button \"{ok.name}\" has no text child.");
            }
            else if (label.text == "OK")
            {
                report.AppendLine("OK button already reads \"OK\".");
            }
            else
            {
                Undo.RecordObject(label, "Fix OK Button Label");
                report.AppendLine($"OK button \"{ok.name}\": \"{label.text}\" -> \"OK\".");
                label.text = "OK";
                EditorUtility.SetDirty(label);
                changed = true;

                report.AppendLine("   Run the localization pass after this so it keys common.ok, not common.cancel.");
            }
        }

        Finish(scene, changed, report.ToString());
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    private static bool TryOpenGameScene(out Scene scene)
    {
        scene = default;

        // Unity refuses to save a scene while the game is running, and opening
        // one would throw away the play session. Say so plainly instead of
        // half-running and failing at the save.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            const string msg = "Stop Play mode first — scenes cannot be saved while the game is running.";
            Debug.Log("[RBFixes] " + msg);
            EditorUtility.DisplayDialog("Not in Play mode", msg, "OK");
            return false;
        }

        // Never discard the user's unsaved work without asking.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[RBFixes] cancelled — nothing was changed.");
            return false;
        }

        scene = SceneManager.GetActiveScene();
        if (scene.path != GameScenePath)
            scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        return scene.IsValid();
    }

    private static void Finish(Scene scene, bool changed, string message)
    {
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            message += "\n\nScene saved.";
        }

        Debug.Log("[RBFixes]\n" + message);
        EditorUtility.DisplayDialog(changed ? "Done" : "Nothing to change", message, "OK");
    }

    private static string Path(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
