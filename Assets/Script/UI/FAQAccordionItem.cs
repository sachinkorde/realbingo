using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One Q/A row inside a FAQAccordion. Tapping the header expands/collapses the
/// answer with a smooth height tween instead of an abrupt SetActive, so it
/// reads as intentional rather than glitchy on a low-end Android device.
///
/// PREFAB SETUP (build this once, the accordion instantiates it per question):
///   Row (this component)
///   ├─ Header (Button)              -> wire to `header`
///   │   └─ QuestionText (TMP_Text)  -> wire to `questionText`
///   ├─ Arrow (Image, optional)      -> wire to `arrow` — rotates 180° when open
///   └─ Body (RectTransform)         -> wire to `body`
///       └─ AnswerText (TMP_Text)    -> wire to `answerText`
///
/// Body needs a LayoutElement (this script drives its Preferred Height).
/// AnswerText's RectTransform needs a Content Size Fitter set to
/// Vertical Fit = Preferred Size — that is what lets this script measure how
/// tall the wrapped answer text actually needs to be before animating to it.
/// </summary>
public class FAQAccordionItem : MonoBehaviour
{
    [SerializeField] private Button header;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private TMP_Text answerText;
    [SerializeField] private RectTransform body;
    [SerializeField] private RectTransform arrow;

    [Header("Animation")]
    [SerializeField] private float animateSeconds = 0.25f;

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Fires just before this item opens. FAQAccordion uses this to close
    /// whichever item was open before, so only one question is expanded at a
    /// time — standard FAQ behaviour.
    /// </summary>
    public event Action<FAQAccordionItem> OnBeforeOpen;

    private LayoutElement bodyLayout;
    private float openHeight = -1f;   // measured lazily on first Open()
    private Tween heightTween;

    private void Awake()
    {
        if (body != null)
        {
            bodyLayout = body.GetComponent<LayoutElement>();
            if (bodyLayout == null)
                Debug.LogWarning($"[FAQAccordionItem] '{name}': Body has no LayoutElement — add one so its height can be animated.", this);
        }

        if (header != null) header.onClick.AddListener(Toggle);

        SetHeightImmediate(0f);   // start collapsed
        if (arrow != null) arrow.localRotation = Quaternion.identity;
    }

    /// <summary>Fill this row with one FAQ entry's text. Call before Awake runs its course, i.e. right after Instantiate.</summary>
    public void SetContent(string question, string answer)
    {
        if (questionText != null) questionText.text = question;
        if (answerText != null) answerText.text = answer;

        // The wrapped height depends on this text, so any previous
        // measurement is now stale.
        openHeight = -1f;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;

        OnBeforeOpen?.Invoke(this);
        IsOpen = true;

        if (openHeight < 0f) openHeight = MeasureAnswerHeight();

        AnimateHeightTo(openHeight);
        if (arrow != null) arrow.DOLocalRotate(new Vector3(0f, 0f, 180f), animateSeconds);
        Vector2 pos = arrow.anchoredPosition;
        pos.x = -64f;

        arrow.anchoredPosition = pos;
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        AnimateHeightTo(0f);
        if (arrow != null) arrow.DOLocalRotate(Vector3.zero, animateSeconds);
        Vector2 pos = arrow.anchoredPosition;
        pos.x = -24f;
        arrow.anchoredPosition = pos;
    }

    /// <summary>
    /// Reads how tall the wrapped answer text actually needs — from
    /// answerText's OWN rect (sized by its Content Size Fitter), never from
    /// `body`'s rect, since body's height is the very thing this script
    /// overrides; measuring from it would just read back our own last value.
    /// </summary>
    private float MeasureAnswerHeight()
    {
        if (answerText == null) return 0f;

        RectTransform rt = answerText.rectTransform;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        return rt.rect.height;
    }

    private void AnimateHeightTo(float height)
    {
        if (bodyLayout == null) return;

        heightTween?.Kill();
        heightTween = DOTween.To(
            () => bodyLayout.preferredHeight,
            h => bodyLayout.preferredHeight = h,
            height,
            animateSeconds
        ).SetEase(Ease.OutCubic);
    }

    private void SetHeightImmediate(float height)
    {
        if (bodyLayout != null) bodyLayout.preferredHeight = height;
    }

    private void OnDestroy() => heightTween?.Kill();
}
