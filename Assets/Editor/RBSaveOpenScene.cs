using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu: Real Bingo / Fixes / Save Open Scene Now
///
/// Saves whatever scene is open, once, on the next script reload.
///
/// This exists because Ctrl+S was not reaching the Editor: the scene stayed dirty
/// (asterisk in the title bar) and GameScene.unity on disk never changed, while
/// the FAQ content and the two new panels sat in memory only. Rather than keep
/// guessing at why the keystroke went missing, this does the save from inside
/// Unity, where nothing can intercept it.
///
/// A domain reload does not lose scene edits - Unity serialises the open scene
/// across the reload and restores it - so the work built a moment ago is still
/// there when this runs.
///
/// It saves ONCE and then never again. Delete this file afterwards if you like;
/// nothing else refers to it.
/// </summary>
public static class RBSaveOpenScene
{
    private const string AutoRunKey = "RBSaveOpenScene.Done.v1";

    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        if (EditorPrefs.GetBool(AutoRunKey, false)) return;

        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(AutoRunKey, false)) return;

            // Unity refuses to save a scene while the game is running, and that is
            // one of the reasons a Ctrl+S can appear to do nothing.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[RBSave] In Play mode - scenes cannot be saved while the game is " +
                                 "running. Press Stop, then run Real Bingo / Fixes / Save Open Scene Now.");
                return;
            }

            EditorPrefs.SetBool(AutoRunKey, true);
            SaveNow();
        };
    }

    [MenuItem("Real Bingo/Fixes/Save Open Scene Now", priority = 17)]
    public static void SaveNow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[RBSave] Stop Play mode first.");
            return;
        }

        Scene scene = EditorSceneManager.GetActiveScene();

        if (!scene.isDirty)
        {
            Debug.Log($"[RBSave] \"{scene.name}\" has no unsaved changes - nothing to write.");
            return;
        }

        bool ok = EditorSceneManager.SaveOpenScenes();

        Debug.Log(ok
            ? $"[RBSave] Saved \"{scene.name}\" to {scene.path}."
            : $"[RBSave] Unity refused to save \"{scene.name}\". Check that {scene.path} is not " +
              "read-only or locked by another program.");
    }
}
