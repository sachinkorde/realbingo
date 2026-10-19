using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row in the "My Referrals" list. Purely a display component — no
/// interaction, nothing to wire up beyond the four fields below.
/// </summary>
public class ReferralRow : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text mobileText;
    [SerializeField] private TMP_Text joinedText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Image statusBadge;

    private static readonly Color EarnedColor = new(0.20f, 0.65f, 0.35f);
    private static readonly Color PendingColor = new(0.75f, 0.55f, 0.10f);

    public void SetData(ReferralItem item)
    {
        if (item == null) return;

        if (nameText != null) nameText.text = string.IsNullOrEmpty(item.name) ? "Player" : item.name;
        if (mobileText != null) mobileText.text = item.mobile_masked ?? "";
        if (joinedText != null) joinedText.text = FormatJoinedDate(item.joined_at);

        bool earned = item.status == "earned";

        if (statusText != null) statusText.text = earned ? "Earned" : "Pending";
        if (statusBadge != null) statusBadge.color = earned ? EarnedColor : PendingColor;
    }

    /// <summary>Server sends yyyy-MM-dd; shown as-is if parsing ever fails rather than blank.</summary>
    private static string FormatJoinedDate(string isoDate)
    {
        if (string.IsNullOrEmpty(isoDate)) return "";

        return System.DateTime.TryParse(isoDate, out System.DateTime parsed)
            ? parsed.ToString("dd MMM yyyy")
            : isoDate;
    }
}
