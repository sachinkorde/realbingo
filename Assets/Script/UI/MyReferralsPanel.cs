using UnityEngine;
using RealBingo.Localization;
using UnityEngine.UI;

/// <summary>
/// "My Referrals" screen: everyone who signed up with this player's referral
/// code, each showing whether the bonus has actually been paid yet.
///
/// SETUP (same data-driven pattern as FAQAccordion):
///   1. Build ONE row prefab using ReferralRow.
///   2. On the ScrollView's Content object, add a Vertical Layout Group AND a
///      Content Size Fitter (Vertical Fit = Preferred Size).
///   3. Drag the row prefab into `rowPrefab`, the Content transform into
///      `contentParent`.
///   4. Wire the "My Referrals" menu button to OpenPanel.
///
/// Opens/closes the same way as ReferAndEarnPanel / ProfileManager's
/// Open/CloseProfilePanel: close whatever else is open, activate the
/// enclosing parent, then the panel itself.
/// </summary>
public class MyReferralsPanel : MonoBehaviour
{
    [Header("Cross-reference (optional — closes other panels on open)")]
    [SerializeField] private UIManager uiManager;

    [Header("Panel root")]
    [SerializeField] private GameObject panelParent;

    [Header("List")]
    [SerializeField] private ReferralRow rowPrefab;
    [SerializeField] private Transform contentParent;

    [Header("Empty / loading state")]
    [SerializeField] private GameObject emptyStateObject;   // "You haven't referred anyone yet"
    [SerializeField] private GameObject loadingIndicator;

    [SerializeField] private Button closeButton;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);
    }

    /// <summary>Wire this to the "My Referrals" menu button.</summary>
    public void OpenPanel()
    {
        if (panelParent == null) return;

        uiManager?.CloseAllPanels();
        panelParent.SetActive(true);
        LoadReferrals();
    }

    public void ClosePanel()
    {
        if (panelParent != null) panelParent.SetActive(false);
    }

    private void LoadReferrals()
    {
        if (APIManager.Instance == null)
        {
            Toast.Error(L.T("error.connect_failed"));
            return;
        }

        SetLoading(true);
        SetEmptyState(false);
        ClearRows();

        APIManager.Instance.GetReferralList((ok, message, data) =>
        {
            SetLoading(false);

            if (!ok || data == null)
            {
                Toast.Error(string.IsNullOrEmpty(message) ? "Could not load your referrals." : message);
                return;
            }

            ReferralItem[] referrals = data.referrals ?? System.Array.Empty<ReferralItem>();

            if (referrals.Length == 0)
            {
                SetEmptyState(true);
                return;
            }

            BuildRows(referrals);
        });
    }

    private void BuildRows(ReferralItem[] referrals)
    {
        if (rowPrefab == null || contentParent == null) return;

        foreach (ReferralItem item in referrals)
        {
            ReferralRow row = Instantiate(rowPrefab, contentParent);
            row.SetData(item);
        }
    }

    private void ClearRows()
    {
        if (contentParent == null) return;

        foreach (Transform child in contentParent)
            Destroy(child.gameObject);
    }

    private void SetEmptyState(bool show)
    {
        if (emptyStateObject != null) emptyStateObject.SetActive(show);
    }

    private void SetLoading(bool loading)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(loading);
    }
}
