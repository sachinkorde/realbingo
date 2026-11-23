using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds a chip denomination to the betting tray.
///
/// Done through the Editor rather than by hand because a chip is not just a
/// value: it is a root with Image + Button + ChipButton + Animator, a TMP label
/// child, and a "Triangle" child carrying the Light2D the selection glow rides.
/// Instantiate copies all of that AND re-points the clone's own serialised
/// references (contentTransform, lightObject) at its own children, which is the
/// part that is easy to get wrong by hand.
///
/// Safe to run twice: it refuses if a chip of that value already exists.
/// </summary>
public static class RBChipTool
{
    private const long JokerChipValue = 5000;

    [MenuItem("Real Bingo/Chips/Add 5000 Chip")]
    private static void AddFiveThousandChip()
    {
        AddChip(JokerChipValue, "5K_Button");
    }

    [MenuItem("Real Bingo/Chips/Add 5000 Chip", true)]
    private static bool AddFiveThousandChipValidate()
    {
        return !Application.isPlaying;
    }

    private static void AddChip(long value, string objectName)
    {
        ChipButton[] chips = Object.FindObjectsByType<ChipButton>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (chips.Length == 0)
        {
            EditorUtility.DisplayDialog("Add chip",
                "No ChipButton found in the open scene. Open GameScene first.", "OK");
            return;
        }

        foreach (ChipButton existing in chips)
        {
            if (existing.chipValue != value) continue;

            EditorUtility.DisplayDialog("Add chip",
                $"A chip worth {value} already exists ('{existing.name}'). Nothing to do.", "OK");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Clone the highest-value chip, so the new one inherits the look of the
        // end of the tray rather than a small-denomination chip.
        ChipButton template = chips[0];
        foreach (ChipButton candidate in chips)
        {
            if (candidate.chipValue > template.chipValue) template = candidate;
        }

        GameObject clone = Object.Instantiate(template.gameObject, template.transform.parent);
        clone.name = objectName;
        Undo.RegisterCreatedObjectUndo(clone, "Add chip");

        ChipButton chip = clone.GetComponent<ChipButton>();
        chip.chipValue = value;

        TMP_Text label = clone.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = value.ToString();

        // Straight after the chip it was cloned from, so the tray stays ordered.
        clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

        EditorUtility.SetDirty(clone);
        EditorSceneManager.MarkSceneDirty(clone.scene);
        Selection.activeGameObject = clone;

        Debug.Log($"[RBChipTool] Added '{objectName}' worth {value}, cloned from " +
                  $"'{template.name}'. UIManager finds it on its own at runtime — " +
                  "check the row still fits, and save the scene.", clone);
    }
}
