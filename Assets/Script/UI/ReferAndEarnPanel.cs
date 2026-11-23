using TMPro;
using UnityEngine;
using RealBingo.Localization;
using UnityEngine.UI;

/// <summary>
/// "Refer &amp; Earn" screen: the player's own referral code, the current bonus
/// amount, how many people used it, and how much they have earned — plus a
/// Share button that opens Android's native share sheet.
///
/// Opens/closes the same way as ProfileManager's OpenProfilePanel /
/// CloseProfilePanel and UIManager's OpenAddCashPanel family: close whatever
/// else is open, activate the enclosing parent, then the panel itself. Wire
/// the "Refer & Earn" menu button straight to OpenPanel.
/// </summary>
public class ReferAndEarnPanel : MonoBehaviour
{
    [Header("Cross-reference (optional — closes other panels on open)")]
    [SerializeField] private UIManager uiManager;

    [Header("Panel root")]
    [SerializeField] private GameObject panelParent;

    [Header("Display")]
    [SerializeField] private TMP_Text referralCodeText;
    [SerializeField] private TMP_Text bonusAmountText;
    [SerializeField] private TMP_Text totalReferredText;
    [SerializeField] private TMP_Text totalEarnedText;
    [SerializeField] private GameObject loadingIndicator;

    [Header("Buttons")]
    [SerializeField] private Button copyCodeButton;
    [SerializeField] private Button shareButton;
    [SerializeField] private Button closeButton;

    [Header("Share message")]
    [TextArea(2, 4)]
    [SerializeField]
    private string shareMessageTemplate =
        "Join me on The Real Bingo! Use my referral code {CODE} when you sign up — I get Rs. {BONUS} once you make your first deposit.{LINK}";

    // Leave blank until the app has a real Play Store page — the {LINK}
    // placeholder is simply omitted from the shared message until then.
    [SerializeField] private string appDownloadLink = "";

    private string currentReferralCode;
    private string currentBonusDisplay = "50";   // kept for the share message; see OnShare

    private void Awake()
    {
        if (copyCodeButton != null) copyCodeButton.onClick.AddListener(OnCopyCode);
        if (shareButton != null) shareButton.onClick.AddListener(OnShare);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);

        // The panel is a full-screen dimmer on its own canvas at sorting order 10,
        // and the scene is saved with it switched ON so it can be worked on in the
        // editor. Nothing switches it off at runtime: UIManager.CloseAllPanels only
        // walks AddCash's parent, which this is not a child of. Without this line
        // the game opens behind a dark overlay with the referral popup on top of it.
        // OpenPanel is the only thing that should ever show it.
        //
        // This script lives on its own always-active root object, so Awake runs here
        // even while panelParent itself is active.
        if (panelParent != null) panelParent.SetActive(false);
    }

    /// <summary>Wire this to the "Refer & Earn" menu button.</summary>
    public void OpenPanel()
    {
        if (panelParent == null) return;

        uiManager?.CloseAllPanels();
        panelParent.SetActive(true);
        LoadSummary();
    }

    public void ClosePanel()
    {
        if (panelParent != null) panelParent.SetActive(false);
    }

    private void LoadSummary()
    {
        if (APIManager.Instance == null)
        {
            Toast.Error(L.T("error.connect_failed"));
            return;
        }

        SetLoading(true);

        APIManager.Instance.GetReferralSummary((ok, message, data) =>
        {
            if (this == null) return;   // scene unloaded while the request was in flight
            SetLoading(false);

            if (!ok || data == null)
            {
                Toast.Error(string.IsNullOrEmpty(message) ? "Could not load referral info." : message);
                return;
            }

            currentReferralCode = data.referral_code;

            if (referralCodeText != null) referralCodeText.text = data.referral_code ?? "";

            // "Rs." not "₹": the rupee sign (U+20B9) is outside most TMP font
            // assets' default character set and renders as an empty box (☐)
            // unless the font asset's atlas was specifically regenerated to
            // include it. "Rs." renders correctly on every font, no exceptions.
            currentBonusDisplay = decimal.TryParse(data.referral_bonus, out decimal bonus)
                ? $"{bonus:N0}"
                : "50";

            if (bonusAmountText != null) bonusAmountText.text = $"Rs. {currentBonusDisplay}";

            if (totalReferredText != null) totalReferredText.text = data.total_referred.ToString();

            if (totalEarnedText != null)
                totalEarnedText.text = decimal.TryParse(data.total_earned, out decimal earned)
                    ? $"Rs. {earned:N0}"
                    : "Rs. 0";
        });
    }

    private void OnCopyCode()
    {
        if (string.IsNullOrEmpty(currentReferralCode))
        {
            Toast.Warning(L.T("referral.not_loaded"));
            return;
        }

        GUIUtility.systemCopyBuffer = currentReferralCode;
        Toast.Success(L.T("referral.copied"));
    }

    private void OnShare()
    {
        if (string.IsNullOrEmpty(currentReferralCode))
        {
            Toast.Warning(L.T("referral.not_loaded"));
            return;
        }

        string link = string.IsNullOrEmpty(appDownloadLink) ? "" : $" Download: {appDownloadLink}";

        string message = shareMessageTemplate
            .Replace("{CODE}", currentReferralCode)
            .Replace("{BONUS}", currentBonusDisplay)
            .Replace("{LINK}", link);

        NativeShare.ShareText(message, "Share your referral code");
    }

    private void SetLoading(bool loading)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(loading);
    }
}
