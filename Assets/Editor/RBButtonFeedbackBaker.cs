using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// OPTIONAL helper. UIFeedbackInstaller already attaches ButtonFeedback at
/// runtime, so you do not need this � it exists for the case where you would
/// rather see the component sitting on each Button in the Inspector (so you can
/// tune individual controls) instead of having it added at play time.
///
/// Tools ? Real Bingo ? UI Feedback ? Add To Open Scenes
///     adds ButtonFeedback to every Button/Toggle in the currently open
///     scene(s). Everything is registered with Undo, so Ctrl+Z reverts it and
///     you can review the diff before saving.
///
/// Tools ? Real Bingo ? UI Feedback ? Remove From Open Scenes
///     takes them all back off again.
/// </summary>
public static class RBButtonFeedbackBaker
{
    [MenuItem("Tools/Real Bingo/UI Feedback/Add To Open Scenes")]
    private static void AddToOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Exit Play Mode First",
                "Scene changes made during Play mode are discarded when you press Stop.", "OK");
            return;
        }

        int added = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Selectable s in root.GetComponentsInChildren<Selectable>(true))
                {
                    bool isToggle = s is Toggle;
                    if (!(s is Button) && !isToggle) continue;
                    if (s.GetComponent<ButtonFeedback>() != null) continue;

                    var feedback = Undo.AddComponent<ButtonFeedback>(s.gameObject);
                    feedback.hapticType = isToggle ? HapticType.Selection : HapticType.Light;
                    EditorUtility.SetDirty(s.gameObject);
                    added++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[UI Feedback] Added ButtonFeedback to {added} control(s). Save the scene to keep it, or Ctrl+Z to undo.");
    }

    [MenuItem("Tools/Real Bingo/UI Feedback/Remove From Open Scenes")]
    private static void RemoveFromOpenScenes()
    {
        int removed = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ButtonFeedback feedback in root.GetComponentsInChildren<ButtonFeedback>(true))
                {
                    Undo.DestroyObjectImmediate(feedback);
                    removed++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[UI Feedback] Removed ButtonFeedback from {removed} control(s).");
    }
}
