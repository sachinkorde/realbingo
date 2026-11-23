using System;
using System.Collections.Generic;
using RealBingo.Localization;
using UnityEngine;

/// <summary>
/// One question and its answer.
///
/// Each field may hold EITHER a localization key from strings.csv (faq.q1,
/// faq.a1 …) or the text itself. A value that matches a key in the sheet is
/// translated; anything else is shown exactly as typed. That way the shipped
/// questions follow the player's language, and anyone can still drop a one-off
/// question straight into the Inspector without touching the sheet.
/// </summary>
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

    private void Start()
    {
        Build();
        LocalizationManager.OnLanguageChanged += ApplyContent;
    }

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
            item.SetContent(Resolve(entry.question), Resolve(entry.answer));

            if (singleOpenAtATime)
                item.OnBeforeOpen += HandleBeforeOpen;

            items.Add(item);
        }
    }

    /// <summary>
    /// Re-reads every row in the new language. The rows themselves are kept
    /// rather than rebuilt, so switching language while the FAQ is open does not
    /// make the list jump or collapse under the player.
    /// </summary>
    private void ApplyContent()
    {
        int count = Mathf.Min(items.Count, entries.Count);

        for (int i = 0; i < count; i++)
        {
            if (items[i] == null) continue;
            items[i].SetContent(Resolve(entries[i].question), Resolve(entries[i].answer));
        }
    }

    /// <summary>
    /// A value that is a key in the sheet becomes the translation; anything else
    /// is returned untouched. Checking HasKey first is what allows both - L.T on
    /// its own would turn ordinary typed text into "[some typed text]".
    /// </summary>
    private static string Resolve(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        LocalizationManager manager = LocalizationManager.Ensure();
        return manager != null && manager.HasKey(value) ? L.T(value) : value;
    }

    private void HandleBeforeOpen(FAQAccordionItem opening)
    {
        if (currentlyOpen != null && currentlyOpen != opening)
            currentlyOpen.Close();

        currentlyOpen = opening;
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= ApplyContent;

        foreach (FAQAccordionItem item in items)
            if (item != null) item.OnBeforeOpen -= HandleBeforeOpen;
    }
}
