using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Send points to a friend" panel — single-button flow: TRANSFER CASH looks
/// the recipient up and, only once that succeeds, sends the money. A completed
/// transfer cannot be reversed, so the recipient is always re-confirmed right
/// before every send rather than trusting a lookup from earlier.
///
/// Wire in the Inspector:
///   recipientInput       - mobile / email / username (e.g. TRB12345)
///   amountInput          - points to send
///   sendButton           - "TRANSFER CASH" — looks up, then sends
///   recipientLabel       - shows "Ravi  •  98••••3210" once found
///   statusLabel          - errors / confirmation
///   quickAmountsContainer (optional) - panel holding the 100/200/500/... chip
///     buttons. Leave empty to search this GameObject's own children; if the
///     chips live elsewhere in the panel (they usually do — same as
///     WalletManager's addCashViewModePanel), drag that container in.
/// </summary>
public class TransferPanel : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] private TMP_InputField recipientInput;
    [SerializeField] private TMP_InputField amountInput;

    [Header("Buttons")]
    [SerializeField] private Button sendButton;

    [Header("Labels")]
    [SerializeField] private TMP_Text recipientLabel;
    [SerializeField] private TMP_Text statusLabel;

    [Header("Quick amounts (optional — same auto-wiring as Add Cash / Withdraw)")]
    [SerializeField] private GameObject quickAmountsContainer;

    private bool requestRunning;
    private bool quickAmountsWired;

    private void Awake()
    {
        if (sendButton != null) sendButton.onClick.AddListener(OnSend);

        if (recipientInput != null) recipientInput.onValueChanged.AddListener(_ => ClearRecipientLabel());

        WireQuickAmounts();
    }

    /// <summary>
    /// Same pattern as WalletManager.WireAddCashPanel/WireWithdrawCashPanel:
    /// every Button under the container whose name or label text parses as an
    /// amount ("100", "1000", "1K", "Rs. 5,000"…) gets wired to fill the
    /// amount field — no manual per-button hookup needed.
    /// </summary>
    private void WireQuickAmounts()
    {
        if (quickAmountsWired) return;
        quickAmountsWired = true;

        GameObject root = quickAmountsContainer != null ? quickAmountsContainer : gameObject;

        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            if (button == null || button == sendButton) continue;

            if (TryGetButtonAmount(button, out long amount))
            {
                button.onClick.AddListener(() => SelectQuickAmount(amount));
            }
        }
    }

    private void SelectQuickAmount(long amount)
    {
        if (amountInput != null) amountInput.text = amount.ToString();
    }

    private bool TryGetButtonAmount(Button button, out long amount)
    {
        if (WalletManager.TryParseAmount(button.gameObject.name, out amount))
            return true;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        return label != null && WalletManager.TryParseAmount(label.text, out amount);
    }

    private void OnEnable() => ResetPanel();

    private void ResetPanel()
    {
        requestRunning = false;

        if (recipientInput != null) recipientInput.text = "";
        if (amountInput != null) amountInput.text = "";
        ClearRecipientLabel();

        SetStatus("");
    }

    private void ClearRecipientLabel()
    {
        if (recipientLabel != null) recipientLabel.text = "";
    }

    // ── TRANSFER CASH: look the recipient up, and only on success, send ─────
    //
    // A completed transfer cannot be undone, so the recipient is re-confirmed
    // right before every send rather than trusting an earlier lookup — that
    // also means one tap is always enough; there is no separate "Check" step
    // to forget.

    public void OnSend()
    {
        if (requestRunning) return;

        string recipient = recipientInput != null ? recipientInput.text.Trim() : "";

        if (string.IsNullOrEmpty(recipient))
        {
            ShowError("Enter your friend's mobile number or username.");
            return;
        }

        if (!WalletManager.TryParseAmount(amountInput != null ? amountInput.text : "", out long amount))
        {
            ShowError("Enter a valid amount.");
            return;
        }

        if (WalletManager.Instance != null && amount > WalletManager.Instance.GetBalance())
        {
            ShowError("You don't have enough points.");
            return;
        }

        if (APIManager.Instance == null)
        {
            ShowError("Unable to connect. Please try again.");
            return;
        }

        requestRunning = true;
        // Transient progress text only — not an outcome, so no Toast here.
        // Toast fires on every actual result below: success or failure.
        SetStatus("Checking recipient…");

        APIManager.Instance.LookupRecipient(recipient, (lookupOk, lookupMessage, recipientData) =>
        {
            if (!lookupOk || recipientData == null)
            {
                requestRunning = false;
                ClearRecipientLabel();
                // lookupMessage is already player-safe — APIManager never lets a
                // raw backend/framework string (e.g. an undeployed route's
                // "could not be found") reach this callback. See ParseErrorMessage.
                ShowError(string.IsNullOrEmpty(lookupMessage) ? "Player not found." : lookupMessage);
                return;
            }

            if (recipientLabel != null)
                recipientLabel.text = $"{recipientData.name}  •  {recipientData.mobile_masked}";

            SetStatus($"Sending to {recipientData.name}…");

            APIManager.Instance.TransferMoney(recipient, amount, (sendOk, sendMessage, transferData) =>
            {
                requestRunning = false;

                if (!sendOk)
                {
                    string reason = string.IsNullOrEmpty(sendMessage) ? "Transfer failed." : sendMessage;
                    ShowError(reason);
                    return;
                }

                // The server is the source of truth for the balance — never
                // adjust it locally from the amount we happened to send.
                if (WalletManager.Instance != null)
                    WalletManager.Instance.RefreshFromServer();

                string sentTo = transferData != null && !string.IsNullOrEmpty(transferData.recipient_name)
                    ? transferData.recipient_name
                    : recipientData.name;

                ShowSuccess($"Sent {amount:N0} to {sentTo}.");

                if (amountInput != null) amountInput.text = "";
                if (recipientInput != null) recipientInput.text = "";
                ClearRecipientLabel();
            });
        });
    }

    /// <summary>Every failure shows BOTH inline (status label) and as a Toast — money actions must never fail silently or invisibly.</summary>
    private void ShowError(string message)
    {
        SetStatus(message);
        Toast.Error(message);
    }

    private void ShowSuccess(string message)
    {
        SetStatus(message);
        Toast.Success(message);
    }

    public void Close()
    {
        ResetPanel();
        gameObject.SetActive(false);
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null) statusLabel.text = message;
        else if (!string.IsNullOrEmpty(message)) Debug.Log($"[Transfer] {message}");
    }
}
