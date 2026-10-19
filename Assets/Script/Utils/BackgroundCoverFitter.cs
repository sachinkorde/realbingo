using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a full-screen background image COVERING the screen on every aspect
/// ratio, with no empty bars and no squashing.
///
/// Why this is needed
/// ------------------
/// A background sized to the 1920x1080 design area looks correct on a 16:9
/// phone and wrong everywhere else: on a 4:3 tablet it leaves gaps at the top
/// and bottom, on a 20:9 phone it leaves gaps at the sides. Stretching it to
/// fit distorts the artwork instead.
///
/// This scales the image up until it covers the shorter axis completely and
/// lets the excess spill past the screen edge — the same rule CSS calls
/// "background-size: cover". Aspect ratio is preserved, so the art never
/// distorts; you just see slightly more or less of it per device.
///
/// USAGE
/// -----
///   1. Put this on the background Image's GameObject.
///   2. Leave Design Size at the artwork's native size (or tick Use Sprite
///      Native Size to read it from the sprite automatically).
///
/// Design your background art with ~10% of "safe bleed" around the edges that
/// you are happy to lose, since the crop varies by device.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
[ExecuteAlways]
public class BackgroundCoverFitter : MonoBehaviour
{
    [Tooltip("Native size of the background artwork. Ignored when Use Sprite Native Size is on.")]
    public Vector2 designSize = new Vector2(1920f, 1080f);

    [Tooltip("Read the design size from the attached Image's sprite instead of the field above.")]
    public bool useSpriteNativeSize = true;

    [Tooltip("Extra scale applied after covering, e.g. 1.02 to hide a hairline gap at the edges.")]
    [Range(1f, 1.5f)] public float overscan = 1f;

    [Tooltip("Cover the whole screen. Turn off to cover only the parent rect (for a background inside a panel).")]
    public bool coverFullScreen = true;

    private RectTransform rect;
    private RectTransform parentRect;
    private Vector2 lastArea;

    private void OnEnable()
    {
        rect = GetComponent<RectTransform>();
        parentRect = rect.parent as RectTransform;
        lastArea = Vector2.zero;
        Fit();
    }

    private void Update()
    {
        Vector2 area = GetTargetArea();
        if (area == lastArea) return;

        lastArea = area;
        Fit();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        rect = GetComponent<RectTransform>();
        parentRect = rect != null ? rect.parent as RectTransform : null;
        lastArea = Vector2.zero;
    }
#endif

    /// <summary>Resizes the background so it covers its target area. Safe to call any time.</summary>
    public void Fit()
    {
        if (rect == null) rect = GetComponent<RectTransform>();
        if (rect == null) return;

        Vector2 area = GetTargetArea();
        if (area.x <= 0f || area.y <= 0f) return;

        Vector2 native = ResolveDesignSize();
        if (native.x <= 0f || native.y <= 0f) return;

        // Cover: scale by the LARGER ratio so neither axis is left short.
        // (Using the smaller ratio would be "fit", which is what leaves bars.)
        float scale = Mathf.Max(area.x / native.x, area.y / native.y) * overscan;

        // Anchor to the centre so the overflow is split evenly on both sides.
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = native * scale;
    }

    /// <summary>The rect this background has to fill, in canvas units.</summary>
    private Vector2 GetTargetArea()
    {
        if (coverFullScreen)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                RectTransform canvasRect = canvas.rootCanvas.transform as RectTransform;
                if (canvasRect != null) return canvasRect.rect.size;
            }
        }

        if (parentRect == null) parentRect = rect != null ? rect.parent as RectTransform : null;
        return parentRect != null ? parentRect.rect.size : Vector2.zero;
    }

    private Vector2 ResolveDesignSize()
    {
        if (useSpriteNativeSize)
        {
            Image image = GetComponent<Image>();
            if (image != null && image.sprite != null)
            {
                Rect spriteRect = image.sprite.rect;
                if (spriteRect.width > 0f && spriteRect.height > 0f)
                    return new Vector2(spriteRect.width, spriteRect.height);
            }

            RawImage raw = GetComponent<RawImage>();
            if (raw != null && raw.texture != null)
                return new Vector2(raw.texture.width, raw.texture.height);
        }

        return designSize;
    }
}
