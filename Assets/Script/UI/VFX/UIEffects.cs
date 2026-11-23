using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// All the PRD's screen effects, built from pooled UI Images and DOTween.
///
/// WHY NOT ParticleSystem
/// ----------------------
/// Every canvas in this game is Screen Space UI. A ParticleSystem renders in the
/// 3D queue, so it either draws behind the whole interface or in front of all of
/// it, and it cannot be sorted between two UI elements without a UIParticle
/// bridge component on every effect. Pooled Images have none of that: they are
/// ordinary UI, they respect canvas order, they mask correctly, and they cost
/// almost nothing.
///
/// It also sidesteps the layout-group trap that bit the chips and the results
/// strip — effects are parented to their own full-screen overlay rect, never to
/// a layout-controlled object, so nothing fights them for position.
///
/// TEXTURES
/// --------
/// Generated in code at first use (a soft radial blob, a spark, a coin) and
/// cached for the lifetime of the app. Nothing to import, nothing to assign.
/// Assign the optional Sprite overrides if you want to swap in real artwork
/// later — the animation code does not change.
///
/// USAGE
/// -----
///     UIEffects.SmokePoof(slotTransform);              // Undo / Cancel
///     UIEffects.Firework(resultPanelTransform);        // standard win
///     UIEffects.CoinShower(canvas, 5f);                // 'J' jackpot win
///
/// Every call is safe from anywhere; if nothing can be resolved it quietly does
/// nothing rather than throwing.
/// </summary>
public class UIEffects : MonoBehaviour
{
    // ── Optional artwork overrides ────────────────────────────────────────────

    [Header("Optional Sprite Overrides (leave empty to use generated textures)")]
    public Sprite smokeSprite;
    public Sprite sparkSprite;
    public Sprite coinSprite;

    [Header("Smoke / Dust Poof (PRD Topic 6.3)")]
    [Range(3, 20)] public int smokePuffCount = 9;
    public float smokeDuration = 0.5f;
    public float smokeSpread = 55f;
    public float smokeStartScale = 0.35f;
    public float smokeEndScale = 1.5f;
    public Color smokeColor = new Color(0.85f, 0.82f, 0.78f, 0.9f);

    [Header("Firework Burst (PRD Topic 6.5)")]
    [Range(6, 60)] public int sparkCount = 26;
    public float fireworkDuration = 0.85f;
    public float fireworkRadius = 260f;
    public float fireworkGravity = 320f;
    public Color[] fireworkColors =
    {
        new Color(1f, 0.85f, 0.25f),
        new Color(1f, 0.45f, 0.2f),
        new Color(1f, 1f, 1f),
        new Color(0.4f, 0.85f, 1f),
    };

    [Header("Gold Coin Shower (PRD Topic 6.5 — 'J' win)")]
    [Range(10, 120)] public int coinCount = 45;
    public float coinFallDuration = 2.2f;
    public float coinSpinSpeed = 220f;
    public Color coinColor = new Color(1f, 0.82f, 0.25f);

    [Header("Chip Flight (PRD Topic 6.3)")]
    [Tooltip("Seconds for the chip to travel from the chip tray to the betting slot.")]
    public float chipFlightDuration = 0.38f;
    [Tooltip("How high the chip arcs above the straight line, in UI units. 0 = a flat slide.")]
    public float chipFlightArc = 150f;
    [Tooltip("Chip size while in flight, in UI units.")]
    public float chipFlightSize = 70f;
    [Tooltip("Degrees of spin over the whole flight, so it reads as a tossed chip.")]
    public float chipFlightSpin = 180f;
    [Tooltip("Seconds for the chip to dissolve once it lands. Kept short: nothing may linger after impact.")]
    public float chipImpactDuration = 0.12f;

    [Header("Performance")]
    [Tooltip("Hard ceiling on simultaneous effect images. Protects low-end devices when several effects overlap.")]
    public int maxPooledImages = 160;

    // ── Singleton / pool ──────────────────────────────────────────────────────

    private static UIEffects instance;

    private readonly Stack<Image> pool = new Stack<Image>();
    private readonly List<Image> live = new List<Image>();
    private RectTransform overlay;
    private Canvas hostCanvas;

    private static Sprite generatedSmoke;
    private static Sprite generatedSpark;
    private static Sprite generatedCoin;

    /// <summary>
    /// Finds or creates the effects host. The overlay is built under the same
    /// canvas as the anchor, so effects always draw in the right place.
    /// </summary>
    private static UIEffects Resolve(Transform context)
    {
        if (instance != null) return instance;

        instance = FindAnyObjectByType<UIEffects>();
        if (instance != null) { instance.EnsureOverlay(context); return instance; }

        Canvas canvas = ResolveCanvas(context);
        if (canvas == null) return null;

        var host = new GameObject("~UIEffects");
        host.transform.SetParent(canvas.transform, false);

        instance = host.AddComponent<UIEffects>();
        instance.hostCanvas = canvas;
        instance.EnsureOverlay(context);

        return instance;
    }

    private static Canvas ResolveCanvas(Transform context)
    {
        if (context != null)
        {
            Canvas fromContext = context.GetComponentInParent<Canvas>();
            if (fromContext != null) return fromContext.rootCanvas;
        }

        Canvas[] all = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Canvas best = null;

        foreach (Canvas c in all)
        {
            if (c == null || !c.isRootCanvas) continue;
            if (best == null || c.sortingOrder > best.sortingOrder) best = c;
        }

        return best;
    }

    private void EnsureOverlay(Transform context)
    {
        if (overlay != null) return;

        if (hostCanvas == null) hostCanvas = ResolveCanvas(context);
        if (hostCanvas == null) return;

        var go = new GameObject("EffectLayer", typeof(RectTransform));
        overlay = go.GetComponent<RectTransform>();
        overlay.SetParent(hostCanvas.transform, false);

        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = Vector2.zero;
        overlay.offsetMax = Vector2.zero;

        // Always on top, never eats taps.
        overlay.SetAsLastSibling();

        var group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // ── Public effects ────────────────────────────────────────────────────────

    /// <summary>
    /// "Smoke / Dust Poof" used when Undo clears one slot or Cancel wipes the
    /// board (PRD Topic 6.3). Spawns at the given UI element's position.
    /// </summary>
    public static void SmokePoof(Transform target, float scaleMultiplier = 1f)
    {
        UIEffects fx = Resolve(target);
        if (fx == null || target == null) return;

        fx.PlaySmoke(target, scaleMultiplier);
    }

    /// <summary>Firework / sparkler burst for a standard win (PRD Topic 6.5).</summary>
    public static void Firework(Transform target, float scaleMultiplier = 1f)
    {
        UIEffects fx = Resolve(target);
        if (fx == null || target == null) return;

        fx.PlayFirework(target, scaleMultiplier);
    }

    /// <summary>
    /// Screen-wide gold coin cascade for a 'J' jackpot (PRD Topic 6.5).
    /// Pass a context transform so it lands on the right canvas.
    /// </summary>
    public static void CoinShower(Transform context, float duration = 0f)
    {
        UIEffects fx = Resolve(context);
        if (fx == null) return;

        fx.PlayCoinShower(duration);
    }

    /// <summary>
    /// Tosses a chip from <paramref name="from"/> to <paramref name="to"/> along a
    /// parabolic arc, then dissolves it where it lands (PRD Topic 6.3 — the stake
    /// becomes plain text on the slot, and no chip is left rendered after impact).
    ///
    /// <paramref name="onImpact"/> fires the moment the chip arrives, so the caller
    /// can punch or reveal the counter on exactly that frame. It is invoked even
    /// when the effect cannot run, so a caller never loses its follow-up.
    /// </summary>
    public static void ChipFlight(Transform from, Transform to, Sprite chipSprite = null,
                                  float scaleMultiplier = 1f, System.Action onImpact = null)
    {
        if (from == null || to == null)
        {
            onImpact?.Invoke();
            return;
        }

        UIEffects fx = Resolve(to);
        if (fx == null)
        {
            onImpact?.Invoke();
            return;
        }

        fx.PlayChipFlight(from, to, chipSprite, scaleMultiplier, onImpact);
    }

    /// <summary>Immediately clears every running effect. Use on scene change or low-spec fallback.</summary>
    public static void ClearAll()
    {
        if (instance == null) return;

        for (int i = instance.live.Count - 1; i >= 0; i--)
        {
            Image img = instance.live[i];
            if (img == null) continue;

            img.transform.DOKill();
            instance.Release(img);
        }

        instance.live.Clear();
    }

    // ── Smoke ─────────────────────────────────────────────────────────────────

    private void PlaySmoke(Transform target, float scaleMultiplier)
    {
        if (overlay == null) return;

        Vector2 centre = ToOverlaySpace(target);
        Sprite sprite = smokeSprite != null ? smokeSprite : GetSmokeSprite();

        for (int i = 0; i < smokePuffCount; i++)
        {
            Image puff = Take(sprite, smokeColor);
            if (puff == null) return;

            RectTransform rt = puff.rectTransform;

            float size = Random.Range(46f, 78f) * scaleMultiplier;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = centre + Random.insideUnitCircle * (smokeSpread * 0.3f);
            rt.localScale = Vector3.one * smokeStartScale;
            rt.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f));

            // Puffs push outward and drift up, the way real dust behaves.
            Vector2 drift = Random.insideUnitCircle.normalized *
                            Random.Range(smokeSpread * 0.5f, smokeSpread) * scaleMultiplier;
            drift.y += smokeSpread * 0.45f;

            float duration = smokeDuration * Random.Range(0.8f, 1.2f);

            Sequence seq = DOTween.Sequence().SetLink(puff.gameObject);
            seq.Join(rt.DOAnchorPos(rt.anchoredPosition + drift, duration).SetEase(Ease.OutQuad));
            seq.Join(rt.DOScale(smokeEndScale * Random.Range(0.85f, 1.2f), duration).SetEase(Ease.OutQuad));
            seq.Join(rt.DORotate(new Vector3(0f, 0f, rt.localEulerAngles.z + Random.Range(-70f, 70f)), duration));
            seq.Join(puff.DOFade(0f, duration).SetEase(Ease.InQuad));

            Image captured = puff;
            seq.OnComplete(() => Release(captured));
        }
    }

    // ── Firework ──────────────────────────────────────────────────────────────

    private void PlayFirework(Transform target, float scaleMultiplier)
    {
        if (overlay == null) return;

        Vector2 centre = ToOverlaySpace(target);
        Sprite sprite = sparkSprite != null ? sparkSprite : GetSparkSprite();

        // Bright flash at the moment of detonation.
        Image flash = Take(sprite, Color.white);
        if (flash != null)
        {
            RectTransform frt = flash.rectTransform;
            frt.sizeDelta = new Vector2(140f, 140f) * scaleMultiplier;
            frt.anchoredPosition = centre;
            frt.localScale = Vector3.one * 0.2f;

            Sequence flashSeq = DOTween.Sequence().SetLink(flash.gameObject);
            flashSeq.Join(frt.DOScale(1.8f * scaleMultiplier, 0.25f).SetEase(Ease.OutQuad));
            flashSeq.Join(flash.DOFade(0f, 0.25f));

            Image capturedFlash = flash;
            flashSeq.OnComplete(() => Release(capturedFlash));
        }

        for (int i = 0; i < sparkCount; i++)
        {
            Image spark = Take(sprite, PickFireworkColor());
            if (spark == null) return;

            RectTransform rt = spark.rectTransform;

            float size = Random.Range(10f, 20f) * scaleMultiplier;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = centre;
            rt.localScale = Vector3.one;

            // Even angular spread with jitter, so it reads as a burst not a blob.
            float angle = (360f / sparkCount) * i + Random.Range(-7f, 7f);
            float radians = angle * Mathf.Deg2Rad;
            float distance = fireworkRadius * Random.Range(0.55f, 1f) * scaleMultiplier;

            Vector2 apex = centre + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * distance;
            float duration = fireworkDuration * Random.Range(0.8f, 1.15f);

            Sequence seq = DOTween.Sequence().SetLink(spark.gameObject);

            // Outward burst, then gravity pulls the spark down as it fades —
            // two joined tweens read as a real ballistic arc.
            seq.Join(rt.DOAnchorPos(apex, duration * 0.55f).SetEase(Ease.OutCubic));
            seq.Insert(duration * 0.5f,
                rt.DOAnchorPos(apex - new Vector2(0f, fireworkGravity * scaleMultiplier), duration * 0.5f)
                  .SetEase(Ease.InQuad));
            seq.Join(rt.DOScale(0.25f, duration).SetEase(Ease.InQuad));
            seq.Insert(duration * 0.45f, spark.DOFade(0f, duration * 0.55f));

            Image captured = spark;
            seq.OnComplete(() => Release(captured));
        }
    }

    private Color PickFireworkColor()
    {
        if (fireworkColors == null || fireworkColors.Length == 0) return Color.white;
        return fireworkColors[Random.Range(0, fireworkColors.Length)];
    }

    // ── Coin shower ───────────────────────────────────────────────────────────

    private void PlayCoinShower(float duration)
    {
        if (overlay == null) return;

        Rect area = overlay.rect;
        Sprite sprite = coinSprite != null ? coinSprite : GetCoinSprite();

        float spawnWindow = duration > 0f ? duration : coinFallDuration;

        for (int i = 0; i < coinCount; i++)
        {
            Image coin = Take(sprite, coinColor);
            if (coin == null) return;

            RectTransform rt = coin.rectTransform;

            float size = Random.Range(38f, 62f);
            rt.sizeDelta = new Vector2(size, size);
            rt.localScale = Vector3.one;

            float startX = Random.Range(area.xMin, area.xMax);
            float startY = area.yMax + Random.Range(60f, 400f);
            rt.anchoredPosition = new Vector2(startX, startY);

            float fall = coinFallDuration * Random.Range(0.75f, 1.3f);
            float delay = Random.Range(0f, spawnWindow * 0.7f);
            float endY = area.yMin - size;
            float sway = Random.Range(-90f, 90f);

            Sequence seq = DOTween.Sequence().SetLink(coin.gameObject).SetDelay(delay);

            // Linear fall — real falling objects do not ease out at the floor.
            seq.Join(rt.DOAnchorPosY(endY, fall).SetEase(Ease.InQuad));
            seq.Join(rt.DOAnchorPosX(startX + sway, fall).SetEase(Ease.InOutSine));

            // Spin about Y to fake a coin tumbling edge-on.
            seq.Join(rt.DOLocalRotate(new Vector3(0f, coinSpinSpeed * fall, 0f), fall, RotateMode.LocalAxisAdd)
                       .SetEase(Ease.Linear));

            Image captured = coin;
            seq.OnComplete(() => Release(captured));
        }
    }

    // ── Pool ──────────────────────────────────────────────────────────────────

    private Image Take(Sprite sprite, Color color)
    {
        if (overlay == null) return null;
        if (live.Count >= maxPooledImages) return null;   // low-end safety valve

        Image img;

        if (pool.Count > 0)
        {
            img = pool.Pop();
            if (img == null) return null;
            img.gameObject.SetActive(true);
        }
        else
        {
            var go = new GameObject("fx", typeof(RectTransform));
            go.transform.SetParent(overlay, false);

            img = go.AddComponent<Image>();
            img.raycastTarget = false;                     // never block gameplay taps
        }

        img.sprite = sprite;
        img.color = color;
        img.rectTransform.localRotation = Quaternion.identity;
        img.rectTransform.localScale = Vector3.one;

        live.Add(img);
        return img;
    }

    private void Release(Image img)
    {
        if (img == null) return;

        live.Remove(img);

        img.transform.DOKill();
        img.gameObject.SetActive(false);
        pool.Push(img);
    }

    // -- Chip flight -----------------------------------------------------------

    private void PlayChipFlight(Transform from, Transform to, Sprite chipSprite,
                                float scaleMultiplier, System.Action onImpact)
    {
        if (overlay == null)
        {
            onImpact?.Invoke();
            return;
        }

        Vector2 start = ToOverlaySpace(from);
        Vector2 end = ToOverlaySpace(to);

        // The chip's own art when the caller has it, otherwise the generated coin,
        // so the effect still works on a board with no sprite wired up.
        Sprite sprite = chipSprite != null ? chipSprite : GetCoinSprite();

        Image chip = Take(sprite, Color.white);
        if (chip == null)
        {
            onImpact?.Invoke();   // pool exhausted: skip the visual, keep the game correct
            return;
        }

        RectTransform rt = chip.rectTransform;

        float size = chipFlightSize * scaleMultiplier;
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = start;

        // Control point lifted above the midpoint: a real toss, not a slide. A
        // Bezier rather than DOTween's path keeps this in the overlay's 2D space.
        Vector2 control = (start + end) * 0.5f + new Vector2(0f, chipFlightArc * scaleMultiplier);

        float progress = 0f;
        Sequence seq = DOTween.Sequence().SetLink(chip.gameObject);

        seq.Append(DOTween.To(() => progress, value =>
        {
            progress = value;

            float inverse = 1f - value;
            rt.anchoredPosition = inverse * inverse * start
                                + 2f * inverse * value * control
                                + value * value * end;
        }, 1f, chipFlightDuration).SetEase(Ease.InOutQuad));

        seq.Join(rt.DOLocalRotate(new Vector3(0f, 0f, -chipFlightSpin), chipFlightDuration,
                                  RotateMode.LocalAxisAdd).SetEase(Ease.Linear));

        // Impact: hand over to the counter, then take the chip off the board.
        seq.AppendCallback(() => onImpact?.Invoke());
        seq.Append(rt.DOScale(0.2f, chipImpactDuration).SetEase(Ease.InQuad));
        seq.Join(chip.DOFade(0f, chipImpactDuration));

        Image captured = chip;
        seq.OnComplete(() => Release(captured));
    }

    /// <summary>Converts a UI element's position into the effect overlay's local space.</summary>
    private Vector2 ToOverlaySpace(Transform target)
    {
        if (overlay == null || target == null) return Vector2.zero;

        Vector3 world = target.position;
        Vector2 local = overlay.InverseTransformPoint(world);
        return local;
    }

    // ── Generated textures ────────────────────────────────────────────────────

    /// <summary>Soft radial blob — dust and smoke.</summary>
    private static Sprite GetSmokeSprite()
    {
        if (generatedSmoke != null) return generatedSmoke;
        generatedSmoke = BuildRadialSprite(64, 0.0f, 2.2f);
        return generatedSmoke;
    }

    /// <summary>Tight bright core with a fast falloff — sparks.</summary>
    private static Sprite GetSparkSprite()
    {
        if (generatedSpark != null) return generatedSpark;
        generatedSpark = BuildRadialSprite(32, 0.35f, 1.1f);
        return generatedSpark;
    }

    /// <summary>Filled disc with a darker rim, so a flat colour still reads as a coin.</summary>
    private static Sprite GetCoinSprite()
    {
        if (generatedCoin != null) return generatedCoin;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];

        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                byte alpha;
                float shade;

                if (dist > 1f) { alpha = 0; shade = 0f; }
                else
                {
                    alpha = (byte)(Mathf.Clamp01((1f - dist) * 8f) * 255f);

                    // Rim darkening plus a soft highlight up and left.
                    float rim = Mathf.SmoothStep(1f, 0.55f, Mathf.InverseLerp(0.72f, 1f, dist));
                    float highlight = Mathf.Clamp01(1f - new Vector2(dx + 0.32f, dy - 0.32f).magnitude * 1.5f);
                    shade = Mathf.Clamp01(rim + highlight * 0.45f);
                }

                byte v = (byte)(shade * 255f);
                pixels[y * size + x] = new Color32(v, v, v, alpha);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        generatedCoin = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return generatedCoin;
    }

    /// <summary>
    /// Radial gradient texture. innerSolid keeps the middle fully opaque;
    /// falloff controls how sharply the edge fades.
    /// </summary>
    private static Sprite BuildRadialSprite(int size, float innerSolid, float falloff)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];

        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float a;
                if (dist <= innerSolid) a = 1f;
                else a = Mathf.Clamp01(Mathf.Pow(1f - Mathf.InverseLerp(innerSolid, 1f, dist), falloff));

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
