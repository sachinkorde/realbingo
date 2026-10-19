using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// The "Last 10 Results" strip.
///
/// PRD Topic 5.3 / 6.5: a new result must SLIDE IN from the right, pushing the
/// older numbers left, and the newest entry must flash for ~2 seconds so the
/// player's eye is pulled to it.
///
/// IMPORTANT — why the boxes themselves are never moved:
/// the result boxes are direct children of a HorizontalLayoutGroup (with a
/// ContentSizeFitter). A layout group OWNS the position of its direct children
/// and rewrites it on every rebuild, so tweening a box makes it jump out of the
/// row and snap back. Instead the TEXT INSIDE each box is animated. The text is
/// a stretch-anchored child of the box, which the layout group does not touch,
/// so it can slide freely while the box stays exactly where the group put it.
/// The result reads identically to the player.
///
/// The public API is unchanged — SetResults(List&lt;int&gt;) — so UIManager and
/// GameManager need no edits, and the Inspector's assigned text list is kept.
/// </summary>
public class LastResultsDisplay : MonoBehaviour
{
    // Inspector मध्ये तुझे 10 Text Objects (History Blocks) इथे लिस्टमध्ये टाक
    public List<TMP_Text> resultsTextList;

    [Header("Shift Animation (PRD Topic 6.5)")]
    [Tooltip("Slide the numbers across when a new result arrives.")]
    public bool animateShift = true;

    [Tooltip("How far each number slides in from the right, in UI units.")]
    public float slideDistance = 70f;

    public float slideDuration = 0.35f;
    public Ease slideEase = Ease.OutCubic;

    [Tooltip("Delay between each box starting its slide. Small values look like one connected push.")]
    [Range(0f, 0.08f)] public float stagger = 0.025f;

    [Header("New Result Highlight")]
    [Tooltip("Flash the newest (rightmost) entry so the player notices it.")]
    public bool highlightNewest = true;

    [Tooltip("PRD asks for exactly 2 seconds.")]
    public float highlightDuration = 2f;

    public Color highlightColor = new Color(1f, 0.85f, 0.1f);

    [Range(1, 12)] public int highlightFlashes = 5;

    [Tooltip("Optional 'NEW' badge positioned over the rightmost box. Shown for the highlight duration, then hidden.")]
    public GameObject newTagOverlay;

    private Vector2[] basePositions;
    private Color[] baseColors;
    private Tween[] slideTweens;
    private Sequence highlightSequence;

    private int lastNewestValue;
    private bool hasDisplayedOnce;

    private void Awake()
    {
        CacheDefaults();
        if (newTagOverlay != null) newTagOverlay.SetActive(false);
    }

    private void OnDestroy()
    {
        KillAllTweens();
    }

    /// <summary>
    /// `results` is newest-first. We display newest on the RIGHT: the latest
    /// winning number goes into the rightmost box, older ones shift left.
    /// (resultsTextList must be assigned left -> right in the Inspector.)
    /// </summary>
    public void SetResults(List<int> results)
    {
        if (resultsTextList == null || resultsTextList.Count == 0) return;

        CacheDefaults();

        int boxCount = resultsTextList.Count;

        for (int i = 0; i < boxCount; i++)
        {
            // i = 0 (newest) -> last box (rightmost); i = 1 -> second from right; ...
            int boxIndex = boxCount - 1 - i;
            if (resultsTextList[boxIndex] == null) continue;

            bool hasValue = results != null && i < results.Count;
            resultsTextList[boxIndex].text = hasValue ? results[i].ToString() : "";
        }

        bool hasResults = results != null && results.Count > 0;
        bool isNewResult = hasResults && (!hasDisplayedOnce || results[0] != lastNewestValue);

        if (hasResults) lastNewestValue = results[0];

        // The first fill is the server seeding the last-10 on entry. Animating it
        // would slide in results the player was never present for.
        if (!hasDisplayedOnce)
        {
            hasDisplayedOnce = true;
            return;
        }

        if (!isNewResult) return;

        if (animateShift) PlayShift();
        if (highlightNewest) PlayHighlight(boxCount - 1);
    }

    // ── Shift ─────────────────────────────────────────────────────────────────

    private void PlayShift()
    {
        for (int i = 0; i < resultsTextList.Count; i++)
        {
            TMP_Text label = resultsTextList[i];
            if (label == null) continue;

            RectTransform rt = label.rectTransform;

            slideTweens[i]?.Kill();

            // Start displaced to the right, then settle home. Rightmost starts
            // first so the motion reads as a push travelling leftwards.
            float delay = stagger * (resultsTextList.Count - 1 - i);

            rt.anchoredPosition = basePositions[i] + new Vector2(slideDistance, 0f);

            slideTweens[i] = rt
                .DOAnchorPos(basePositions[i], slideDuration)
                .SetEase(slideEase)
                .SetDelay(delay)
                .SetLink(gameObject);
        }
    }

    // ── Newest highlight ──────────────────────────────────────────────────────

    private void PlayHighlight(int boxIndex)
    {
        if (boxIndex < 0 || boxIndex >= resultsTextList.Count) return;

        TMP_Text label = resultsTextList[boxIndex];
        if (label == null) return;

        KillHighlight();

        if (newTagOverlay != null) newTagOverlay.SetActive(true);

        float perFlash = highlightDuration / Mathf.Max(1, highlightFlashes);
        Color baseColor = baseColors[boxIndex];

        highlightSequence = DOTween.Sequence().SetLink(gameObject);

        for (int i = 0; i < highlightFlashes; i++)
        {
            highlightSequence.Append(label.DOColor(highlightColor, perFlash * 0.5f));
            highlightSequence.Append(label.DOColor(baseColor, perFlash * 0.5f));
        }

        highlightSequence.OnComplete(() =>
        {
            label.color = baseColor;
            if (newTagOverlay != null) newTagOverlay.SetActive(false);
        });
    }

    // ── Housekeeping ──────────────────────────────────────────────────────────

    /// <summary>
    /// Records each label's resting position and colour so the animations always
    /// return to exactly where the layout put them, however many times they run.
    /// </summary>
    private void CacheDefaults()
    {
        if (resultsTextList == null) return;

        int count = resultsTextList.Count;
        if (basePositions != null && basePositions.Length == count) return;

        KillAllTweens();

        basePositions = new Vector2[count];
        baseColors = new Color[count];
        slideTweens = new Tween[count];

        for (int i = 0; i < count; i++)
        {
            if (resultsTextList[i] == null) continue;

            basePositions[i] = resultsTextList[i].rectTransform.anchoredPosition;
            baseColors[i] = resultsTextList[i].color;
        }
    }

    private void KillAllTweens()
    {
        if (slideTweens != null)
        {
            for (int i = 0; i < slideTweens.Length; i++) slideTweens[i]?.Kill();
        }

        KillHighlight();
    }

    private void KillHighlight()
    {
        if (highlightSequence != null && highlightSequence.IsActive()) highlightSequence.Kill();
        highlightSequence = null;
    }
}
