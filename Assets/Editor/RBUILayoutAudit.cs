using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Finds UI that will be cut off, and shows you the exact aspect ratio where it
/// starts happening — instead of you flipping through 15 device presets by eye.
///
/// Tools &gt; Real Bingo &gt; UI &gt; Report Canvas Size By Aspect
///     For each canvas, prints how big the canvas becomes (in design units) on
///     every common phone/tablet shape, under the CURRENT scaler settings and
///     under Expand. If a number is smaller than your design width, everything
///     wider than it is being cut off on that device.
///
/// Tools &gt; Real Bingo &gt; UI &gt; Report UI Overflow (current resolution)
///     At whatever resolution the Game/Simulator view is set to RIGHT NOW,
///     lists every UI element sticking out past its canvas, and by how much.
///     Switch resolution, run again — the list changes.
///
/// Both are read-only.
/// </summary>
public static class RBUILayoutAudit
{
    /// <summary>Landscape test sizes covering everything from an iPad to a 21:9 phone.</summary>
    private static readonly (string name, int w, int h)[] TestResolutions =
    {
        ("iPad / 4:3      ", 2160, 1620),
        ("3:2             ", 2160, 1440),
        ("16:10           ", 2160, 1350),
        ("16:9            ", 1920, 1080),
        ("18:9            ", 2160, 1080),
        ("19.5:9 (iPhone) ", 2340, 1080),
        ("20:9            ", 2400, 1080),
        ("21:9            ", 2520, 1080),
    };

    // ── Report 1: canvas size per aspect ──────────────────────────────────────

    [MenuItem("Tools/Real Bingo/UI/Report Canvas Size By Aspect")]
    private static void ReportCanvasSizeByAspect()
    {
        CanvasScaler[] scalers = Object.FindObjectsByType<CanvasScaler>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (scalers.Length == 0)
        {
            Debug.Log("[UI Audit] No CanvasScaler in the open scenes.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[UI Audit] Canvas size in DESIGN UNITS per device shape.");
        sb.AppendLine("Anything in your layout wider/taller than these numbers is CUT OFF.");
        sb.AppendLine();

        foreach (CanvasScaler s in scalers)
        {
            if (s == null || s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;

            Vector2 reference = s.referenceResolution;
            if (reference.x <= 0f || reference.y <= 0f) continue;

            sb.AppendLine($"── {s.name}  (reference {reference.x}x{reference.y}, mode {s.screenMatchMode} {s.matchWidthOrHeight:F2})");
            sb.AppendLine($"     {"device",-18}{"CURRENT settings",-26}{"with EXPAND"}");

            foreach (var (name, w, h) in TestResolutions)
            {
                Vector2 current = CanvasSize(w, h, reference, s.screenMatchMode, s.matchWidthOrHeight);
                Vector2 expand = CanvasSize(w, h, reference, CanvasScaler.ScreenMatchMode.Expand, 0f);

                string currentFlag = current.x < reference.x - 1f ? $"  ← {reference.x - current.x:F0} too narrow" : "";

                sb.AppendLine(
                    $"     {name,-18}{current.x,6:F0} x {current.y,-6:F0}{currentFlag,-14}" +
                    $"{expand.x,6:F0} x {expand.y,-6:F0}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Reading this: with Expand, the width column NEVER drops below your");
        sb.AppendLine("reference width — that is the whole point. The height grows instead,");
        sb.AppendLine("which is empty space you must anchor content into, not clipping.");

        Debug.Log(sb.ToString());
    }

    /// <summary>Replicates Unity's CanvasScaler maths exactly.</summary>
    private static Vector2 CanvasSize(int screenW, int screenH, Vector2 reference,
                                      CanvasScaler.ScreenMatchMode mode, float match)
    {
        float scale;

        switch (mode)
        {
            case CanvasScaler.ScreenMatchMode.Expand:
                scale = Mathf.Min(screenW / reference.x, screenH / reference.y);
                break;

            case CanvasScaler.ScreenMatchMode.Shrink:
                scale = Mathf.Max(screenW / reference.x, screenH / reference.y);
                break;

            default: // MatchWidthOrHeight — Unity lerps in LOG space, not linear
                float logWidth = Mathf.Log(screenW / reference.x, 2f);
                float logHeight = Mathf.Log(screenH / reference.y, 2f);
                scale = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, match));
                break;
        }

        if (scale <= 0f) return Vector2.zero;
        return new Vector2(screenW / scale, screenH / scale);
    }

    // ── Report 2: what is actually overflowing right now ──────────────────────

    [MenuItem("Tools/Real Bingo/UI/Report UI Overflow (current resolution)")]
    private static void ReportOverflow()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var sb = new StringBuilder();
        sb.AppendLine($"[UI Audit] Overflow at the current view size ({Screen.width}x{Screen.height}).");
        sb.AppendLine("Skipped: inactive objects (closed slide-in panels are off-screen ON PURPOSE)");
        sb.AppendLine("         and anything inside a Mask / RectMask2D (scroll views clip by design).");
        sb.AppendLine();

        if (Screen.width < 800)
        {
            sb.AppendLine("⚠ The view is tiny, so these numbers mean little. Open the Simulator");
            sb.AppendLine("  (or enlarge the Game view) and pick a real device before trusting this.");
            sb.AppendLine();
        }

        int totalProblems = 0;

        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || !canvas.isRootCanvas) continue;

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) continue;

            Rect bounds = canvasRect.rect;
            var problems = new List<string>();

            // Active only: a closed wallet/profile/notification panel is parked
            // off-screen deliberately and would otherwise dominate the report.
            foreach (RectTransform child in canvas.GetComponentsInChildren<RectTransform>(false))
            {
                if (child == canvasRect) continue;

                // Only report leaf-ish visible things; a container sticking out is
                // usually reported through its children anyway.
                if (child.GetComponent<Graphic>() == null) continue;

                // Scroll-view content is meant to be clipped by its viewport.
                if (IsInsideMask(child, canvasRect)) continue;

                Vector2 min, max;
                if (!TryGetLocalBounds(child, canvasRect, out min, out max)) continue;

                float overLeft = bounds.xMin - min.x;
                float overRight = max.x - bounds.xMax;
                float overBottom = bounds.yMin - min.y;
                float overTop = max.y - bounds.yMax;

                var sides = new List<string>();
                if (overLeft > 1f) sides.Add($"left {overLeft:F0}");
                if (overRight > 1f) sides.Add($"right {overRight:F0}");
                if (overBottom > 1f) sides.Add($"bottom {overBottom:F0}");
                if (overTop > 1f) sides.Add($"top {overTop:F0}");

                if (sides.Count > 0)
                    problems.Add($"     {GetPath(child, canvasRect)}  →  off-screen: {string.Join(", ", sides)}");
            }

            if (problems.Count == 0) continue;

            totalProblems += problems.Count;
            sb.AppendLine($"── {canvas.name}   canvas rect {bounds.width:F0} x {bounds.height:F0}");
            foreach (string p in problems) sb.AppendLine(p);
            sb.AppendLine();
        }

        if (totalProblems == 0)
            sb.AppendLine("  ✔ Nothing is clipped at this resolution.");
        else
            sb.AppendLine($"  {totalProblems} element(s) clipped. Fix the WIDEST container first — " +
                          "children usually follow their parent back into view.");

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// True when an ancestor clips this element on purpose (Scroll View viewport,
    /// masked container). Those are not layout bugs.
    /// </summary>
    private static bool IsInsideMask(Transform t, Transform stopAt)
    {
        for (Transform p = t.parent; p != null && p != stopAt; p = p.parent)
        {
            if (p.GetComponent<RectMask2D>() != null) return true;
            if (p.GetComponent<Mask>() != null) return true;
        }
        return false;
    }

    /// <summary>Element corners expressed in the root canvas's local space.</summary>
    private static bool TryGetLocalBounds(RectTransform target, RectTransform canvasRect,
                                          out Vector2 min, out Vector2 max)
    {
        min = max = Vector2.zero;

        var corners = new Vector3[4];
        target.GetWorldCorners(corners);

        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        for (int i = 0; i < 4; i++)
        {
            Vector3 local = canvasRect.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        // A zero-size rect tells us nothing useful.
        return (max.x - min.x) > 0.01f || (max.y - min.y) > 0.01f;
    }

    private static string GetPath(Transform t, Transform stopAt)
    {
        string path = t.name;
        while (t.parent != null && t.parent != stopAt)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
