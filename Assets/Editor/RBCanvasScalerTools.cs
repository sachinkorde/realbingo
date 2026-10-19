using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Diagnostics and a one-click fix for the canvas-scaling inconsistency that
/// causes the UI to break between small phones and tablets.
///
/// Tools > Real Bingo > UI > Report Canvas Scalers
///     Lists every CanvasScaler in the open scenes with its mode, match value
///     and reference resolution, and flags the ones that disagree. Read-only.
///
/// Tools > Real Bingo > UI > Normalize Canvas Scalers In Open Scenes
///     Sets every CanvasScaler to ScaleWithScreenSize / Expand / 1920x1080 so
///     the whole design area is visible on every aspect ratio and all canvases
///     agree with each other. Registered with Undo — press Ctrl+Z to revert,
///     and review the scene diff before saving.
/// </summary>
public static class RBCanvasScalerTools
{
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

    [MenuItem("Tools/Real Bingo/UI/Report Canvas Scalers")]
    private static void ReportCanvasScalers()
    {
        CanvasScaler[] scalers = FindAllScalers();

        if (scalers.Length == 0)
        {
            Debug.Log("[Canvas Report] No CanvasScaler found in the open scenes.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[Canvas Report] {scalers.Length} CanvasScaler(s) in the open scene(s):");
        sb.AppendLine();

        bool inconsistent = false;
        CanvasScaler.ScreenMatchMode? firstMode = null;
        float? firstMatch = null;

        foreach (CanvasScaler s in scalers)
        {
            if (s == null) continue;

            if (firstMode == null) { firstMode = s.screenMatchMode; firstMatch = s.matchWidthOrHeight; }
            else if (s.screenMatchMode != firstMode || !Mathf.Approximately(s.matchWidthOrHeight, firstMatch.Value))
                inconsistent = true;

            sb.AppendLine($"  • {GetPath(s.transform)}");
            sb.AppendLine($"      scaleMode = {s.uiScaleMode}");
            sb.AppendLine($"      matchMode = {s.screenMatchMode}   matchValue = {s.matchWidthOrHeight:F2}");
            sb.AppendLine($"      reference = {s.referenceResolution.x}x{s.referenceResolution.y}");
            sb.AppendLine($"      {ExplainRisk(s)}");
            sb.AppendLine();
        }

        if (inconsistent)
        {
            sb.AppendLine("⚠ These canvases do NOT share the same match settings. They will scale by");
            sb.AppendLine("  different amounts on non-16:9 screens, so overlays drift relative to the");
            sb.AppendLine("  game board. Run Tools > Real Bingo > UI > Normalize Canvas Scalers.");
        }

        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/Real Bingo/UI/Normalize Canvas Scalers In Open Scenes")]
    private static void NormalizeCanvasScalers()
    {
        // Scene edits made during Play mode are discarded on Stop.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "Exit Play Mode First",
                "Scene changes made during Play mode are discarded when you press Stop.\n\n" +
                "Press Stop, then run this again.",
                "OK");
            return;
        }

        CanvasScaler[] scalers = FindAllScalers();
        int changed = 0;

        foreach (CanvasScaler s in scalers)
        {
            if (s == null) continue;

            Canvas canvas = s.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace) continue;

            Undo.RecordObject(s, "Normalize Canvas Scaler");

            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = ReferenceResolution;
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            EditorUtility.SetDirty(s);
            changed++;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[Canvas Report] Normalized {changed} CanvasScaler(s) to ScaleWithScreenSize / Expand / 1920x1080.\n" +
                  "Check the Game view at 4:3 (1440x1080) and 20:9 (2400x1080) before saving. Ctrl+Z reverts.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CanvasScaler[] FindAllScalers()
    {
        return Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>Plain-English note on what each setting does to a non-16:9 device.</summary>
    private static string ExplainRisk(CanvasScaler s)
    {
        if (s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return "→ Not ScaleWithScreenSize: this canvas ignores screen size entirely.";

        switch (s.screenMatchMode)
        {
            case CanvasScaler.ScreenMatchMode.Expand:
                return "→ OK. Whole design area stays visible on every aspect ratio.";

            case CanvasScaler.ScreenMatchMode.Shrink:
                return "→ Fills the screen but CROPS the overflowing axis on non-16:9 devices.";

            default:
                if (Mathf.Approximately(s.matchWidthOrHeight, 0f))
                    return "→ RISK: matches width only. On a wide phone (20:9) the UI is drawn too " +
                           "large and the top/bottom of the design area falls off screen.";

                if (Mathf.Approximately(s.matchWidthOrHeight, 1f))
                    return "→ RISK: matches height only. On a 4:3 tablet the left/right edges are cut off.";

                return "→ Partial match: splits the error between both axes; still crops at extreme aspects.";
        }
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
