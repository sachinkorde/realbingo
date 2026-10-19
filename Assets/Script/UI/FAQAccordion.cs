using System;
using System.Collections.Generic;
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
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        AnimateHeightTo(0f);
        if (arrow != null) arrow.DOLocalRotate(Vector3.zero, animateSeconds);
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

[Serializable]
public struct FAQEntry
{
    [TextArea] public string question;
    [TextArea(2, 6)] public string answer;
}

/// <summary>
/// FAQ panel controller. Builds a list of FAQAccordionItem rows from data set
/// in the Inspector and keeps only one expanded at a time.
///
/// SETUP:
///   1. Build ONE row prefab using FAQAccordionItem (see its doc comment).
///   2. On the ScrollView's Content object, add a Vertical Layout Group AND a
///      Content Size Fitter (Vertical Fit = Preferred Size) — required for
///      the list to reflow and the scroll area to grow as rows open/close.
///   3. Drag the row prefab into `itemPrefab`, the Content transform into
///      `contentParent`.
///   4. Fill in `entries` with your questions and answers. Rows are created
///      automatically at runtime — no per-question scene work needed.
/// </summary>
public class FAQAccordion : MonoBehaviour
{
    [SerializeField] private FAQAccordionItem itemPrefab;
    [SerializeField] private Transform contentParent;
    [SerializeField] private List<FAQEntry> entries = new();

    /// <summary>true = only one row open at a time (standard FAQ behaviour).</summary>
    [SerializeField] private bool singleOpenAtATime = true;

    private readonly List<FAQAccordionItem> items = new();
    private FAQAccordionItem currentlyOpen;

    private void Start() => Build();

    private void Build()
    {
        if (itemPrefab == null || contentParent == null)
        {
            Debug.LogWarning("[FAQAccordion] itemPrefab or contentParent not assigned.", this);
            return;
        }

        foreach (Transform child in contentParent)
            Destroy(child.gameObject);
        items.Clear();
        currentlyOpen = null;

        foreach (FAQEntry entry in entries)
        {
            FAQAccordionItem item = Instantiate(itemPrefab, contentParent);
            item.SetContent(entry.question, entry.answer);

            if (singleOpenAtATime)
                item.OnBeforeOpen += HandleBeforeOpen;

            items.Add(item);
        }
    }

    private void HandleBeforeOpen(FAQAccordionItem opening)
    {
        if (currentlyOpen != null && currentlyOpen != opening)
            currentlyOpen.Close();

        currentlyOpen = opening;
    }

    private void OnDestroy()
    {
        foreach (FAQAccordionItem item in items)
            if (item != null) item.OnBeforeOpen -= HandleBeforeOpen;
    }
}
