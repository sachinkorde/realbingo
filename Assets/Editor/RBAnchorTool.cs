using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Re-anchors UI to the screen edge it actually belongs to, without moving it.
///
/// THE PROBLEM
/// -----------
/// Most broken-on-tablet layouts are anchored near the centre. When the canvas
/// gets taller (4:3) or wider (21:9), centre-anchored elements float away from
/// the edge they were designed to sit against — which is why a bottom bar ends
/// up hovering with black space beneath it.
///
/// An element anchored to the BOTTOM stays welded to the bottom whether the
/// canvas is 1080 or 1440 tall. That is the whole fix, but doing it by hand for
/// a hundred objects is a day of clicking, and it is easy to nudge things out of
/// place while you do it.
///
/// WHAT THIS DOES
/// --------------
/// For every selected object (and optionally its children) it decides which
/// edge the element is nearest, sets the anchors to that edge, and rewrites the
/// offsets so the element does not visually move by a single pixel. Anything
/// spanning most of its parent becomes a stretch anchor instead.
///
/// Everything is registered with Undo. Nothing is saved for you — look at it
/// first, then decide.
///
/// Tools > Real Bingo > UI > Anchor Selection To Nearest Edge
/// Tools > Real Bingo > UI > Anchor Selection + Children To Nearest Edge
/// </summary>
public static class RBAnchorTool
{
    /// <summary>Past this fraction of the parent, an element is treated as spanning it.</summary>
    private const float StretchThreshold = 0.8f;

    /// <summary>Inside this fraction from an edge, an element is treated as belonging to it.</summary>
    private const float EdgeThreshold = 0.34f;

    [MenuItem("Tools/Real Bingo/UI/Anchor Selection To Nearest Edge")]
    private static void AnchorSelection() => Run(false);

    [MenuItem("Tools/Real Bingo/UI/Anchor Selection + Children To Nearest Edge")]
    private static void AnchorSelectionRecursive() => Run(true);

    private static void Run(bool includeChildren)
    {
        // Unity throws away every scene edit made during Play mode the moment you
        // press Stop. Running this in Play looks like it worked — the log even
        // reports a count — and then silently loses the lot.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "Exit Play Mode First",
                "Scene changes made during Play mode are discarded when you press Stop.\n\n" +
                "Press Stop, then run this again.",
                "OK");
            return;
        }

        GameObject[] selection = Selection.gameObjects;

        if (selection.Length == 0)
        {
            Debug.LogWarning("[Anchor Tool] Nothing selected. Select the UI objects to re-anchor first.");
            return;
        }

        int changed = 0;

        foreach (GameObject go in selection)
        {
            if (includeChildren)
            {
                foreach (RectTransform rt in go.GetComponentsInChildren<RectTransform>(true))
                    if (AnchorToNearestEdge(rt)) changed++;
            }
            else
            {
                if (go.transform is RectTransform rt && AnchorToNearestEdge(rt)) changed++;
            }
        }

        Debug.Log($"[Anchor Tool] Re-anchored {changed} element(s). Nothing moved on screen.\n" +
                  "Now resize the Game view — anything anchored to an edge should stay glued to it.\n" +
                  "Ctrl+Z reverts the whole operation.");
    }

    /// <summary>
    /// True when a ScrollRect or a mask above this element controls its layout.
    /// Those must not be re-anchored or scrolling breaks.
    /// </summary>
    private static bool IsInsideMaskOrScrollRect(Transform t)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            if (p.GetComponent<ScrollRect>() != null) return true;
            if (p.GetComponent<RectMask2D>() != null) return true;
            if (p.GetComponent<Mask>() != null) return true;
        }
        return false;
    }

    private static bool AnchorToNearestEdge(RectTransform rt)
    {
        if (rt == null) return false;

        var parent = rt.parent as RectTransform;
        if (parent == null) return false;                 // root canvas — leave alone

        // A LayoutGroup already owns its direct children's positions. Re-anchoring
        // them achieves nothing and confuses anyone reading the Inspector later.
        if (parent.GetComponent<LayoutGroup>() != null) return false;

        // Scroll-view content and masked containers are positioned by the
        // ScrollRect, not by anchors. Touching these breaks scrolling.
        if (IsInsideMaskOrScrollRect(rt)) return false;

        Rect parentRect = parent.rect;
        if (parentRect.width <= 0f || parentRect.height <= 0f) return false;

        // Where the element sits right now, in the parent's local space.
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);

        Vector2 localMin = parent.InverseTransformPoint(corners[0]);
        Vector2 localMax = parent.InverseTransformPoint(corners[2]);

        Vector2 size = localMax - localMin;
        Vector2 centre = (localMin + localMax) * 0.5f;

        // Normalised 0..1 position of the element's centre inside the parent.
        float nx = Mathf.InverseLerp(parentRect.xMin, parentRect.xMax, centre.x);
        float ny = Mathf.InverseLerp(parentRect.yMin, parentRect.yMax, centre.y);

        Vector2 anchorMin, anchorMax;

        // Horizontal
        if (size.x >= parentRect.width * StretchThreshold) { anchorMin.x = 0f; anchorMax.x = 1f; }
        else if (nx < EdgeThreshold) { anchorMin.x = anchorMax.x = 0f; }
        else if (nx > 1f - EdgeThreshold) { anchorMin.x = anchorMax.x = 1f; }
        else { anchorMin.x = anchorMax.x = 0.5f; }

        // Vertical
        if (size.y >= parentRect.height * StretchThreshold) { anchorMin.y = 0f; anchorMax.y = 1f; }
        else if (ny < EdgeThreshold) { anchorMin.y = anchorMax.y = 0f; }
        else if (ny > 1f - EdgeThreshold) { anchorMin.y = anchorMax.y = 1f; }
        else { anchorMin.y = anchorMax.y = 0.5f; }

        if (rt.anchorMin == anchorMin && rt.anchorMax == anchorMax) return false;

        Undo.RecordObject(rt, "Anchor To Nearest Edge");

        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;

        // Rebuild the offsets from the position we measured BEFORE touching the
        // anchors, so the element lands exactly where it already was.
        // offsetMin/offsetMax are pivot-independent, which is why this is exact.
        Vector2 anchorMinPos = new Vector2(
            parentRect.xMin + anchorMin.x * parentRect.width,
            parentRect.yMin + anchorMin.y * parentRect.height);

        Vector2 anchorMaxPos = new Vector2(
            parentRect.xMin + anchorMax.x * parentRect.width,
            parentRect.yMin + anchorMax.y * parentRect.height);

        rt.offsetMin = localMin - anchorMinPos;
        rt.offsetMax = localMax - anchorMaxPos;

        EditorUtility.SetDirty(rt);
        return true;
    }
}
