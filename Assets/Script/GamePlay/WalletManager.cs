using UnityEngine;
using RealBingo.Localization;
using System;
using TMPro;
using UnityEngine.UI;

public class WalletManager : MonoBehaviour
{
    public static WalletManager Instance;

    [SerializeField] private long currentBalance = 15000;
    public event Action<long> OnWalletChanged;
    [SerializeField] private TMP_InputField amountToAdd;
    [SerializeField] private long amount;
    [SerializeField] private GameObject addCashViewModePanel;
    [SerializeField] private Button addCashButton;
    [SerializeField] private TMP_InputField amountToWithdraw;
    [SerializeField] private GameObject withdrawCashViewModePanel;
    [SerializeField] private Button withdrawCashButton;

    private bool addCashPanelWired;
    private bool withdrawCashPanelWired;
    private bool addAmountRequestRunning;

    // Base name only — the real key is this plus the signed-in session, see
    // BalanceKey(). A single shared key let the next account on a device read
    // the previous one's balance until the first server refresh landed.
    private const string BALANCE_PREF_KEY = "WalletBalance";
    private const string TOTAL_WITHDRAWN_PREF_KEY = "WalletTotalWithdrawn";
    private const string LAST_WITHDRAW_AMOUNT_PREF_KEY = "WalletLastWithdrawAmount";

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        //DontDestroyOnLoad(gameObject);
        LoadBalance();
    }

    private void OnDestroy()
    {
        // C#'s ?. operator bypasses Unity's == null override, so a static left
        // pointing at a destroyed object gets called into. Drop it here.
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        NotifyUpdate();
        WireAddCashPanel();
        WireWithdrawCashPanel();
    }

    public bool TryDeduct(long amount)
    {
        if (amount < 0) return false;
        if (currentBalance >= amount)
        {
            currentBalance -= amount;
            SaveBalance();
            return true;
        }
        return false;
    }

    public void AddAmount(long amount)
    {
        if (amount < 0) return;

        currentBalance += amount;
        SaveBalance();
    }

    public void AddAmountAPI()
    {
        if (addAmountRequestRunning) return;

        if (amountToAdd == null)
        {
            Debug.LogError("Add cash amount input is not assigned.");
            ShowWalletMessage(L.T("error.restart_app"));
            return;
        }

        // Previously returned silently — tapping Add Cash with an empty or
        // invalid amount looked like a dead button.
        if (!long.TryParse(amountToAdd.text, out long parsedAmount) || parsedAmount <= 0)
        {
            ShowWalletMessage(L.T("wallet.enter_amount"));
            return;
        }

        amount = parsedAmount;

        if (APIManager.Instance == null)
        {
            Debug.LogError("APIManager.Instance is NULL!");
            ShowWalletMessage(L.T("error.connect_failed"));
            return;
        }

        addAmountRequestRunning = true;

        APIManager.Instance.AddAmount(parsedAmount, (success, response) =>
        {
            if (this == null) return;   // scene unloaded while the request was in flight
            addAmountRequestRunning = false;

            if (success)
            {
                // A deposit is NOT money until the server says "success". In
                // production it comes back "pending" and an admin (or the payment
                // gateway) has to confirm it before the wallet is credited.
                //
                // Never write the deposit amount into the balance label: it is the
                // amount added, not the new balance, and RefreshFromServer would
                // immediately overwrite it with the real value anyway — which is
                // what made the balance appear to "add 10000 then reset to 0".
                string status = response?.data?.status;
                bool credited = status == "success";

                RefreshFromServer();
                amountToAdd.text = "";

                if (credited)
                {
                    ShowWalletMessage(L.T("wallet.added", $"{parsedAmount:N0}"), ToastType.Success);
                }
                else
                {
                    ShowWalletMessage(response?.message
                        ?? L.T("wallet.deposit_pending"),
                        ToastType.Warning);
                }

                HideAddCashPanel();
            }
            else
            {
                // Previously logged to the console only — the player saw nothing.
                ShowWalletMessage(response?.message ?? "Add cash failed. Please try again.",
                    ToastType.Error);
            }
        });
    }

    private void WireAddCashPanel()
    {
        if (addCashPanelWired || addCashViewModePanel == null) return;

        if (amountToAdd == null)
            amountToAdd = addCashViewModePanel.GetComponentInChildren<TMP_InputField>(true);

        if (amountToAdd != null)
            amountToAdd.interactable = false;

        Button[] buttons = addCashViewModePanel.GetComponentsInChildren<Button>(true);

        foreach (Button button in buttons)
        {
            if (button == null) continue;

            if (IsAddCashSubmitButton(button))
            {
                addCashButton = button;
                continue;
            }

            if (TryGetButtonAmount(button, out long buttonAmount))
            {
                button.onClick.AddListener(() => SelectAddCashAmount(buttonAmount));
            }
        }

        if (addCashButton != null)
            addCashButton.onClick.AddListener(AddAmountAPI);

        addCashPanelWired = true;
    }

    private void WireWithdrawCashPanel()
    {
        if (withdrawCashPanelWired) return;

        if (withdrawCashViewModePanel == null && UIManager.Instance != null)
            withdrawCashViewModePanel = UIManager.Instance.withdrawPanel;

        if (withdrawCashViewModePanel == null) return;

        if (amountToWithdraw == null)
            amountToWithdraw = withdrawCashViewModePanel.GetComponentInChildren<TMP_InputField>(true);

        if (amountToWithdraw != null)
            amountToWithdraw.interactable = false;

        Button[] buttons = withdrawCashViewModePanel.GetComponentsInChildren<Button>(true);

        foreach (Button button in buttons)
        {
            if (button == null) continue;

            if (IsWithdrawSubmitButton(button))
            {
                withdrawCashButton = button;
                continue;
            }

            if (TryGetButtonAmount(button, out long buttonAmount))
            {
                button.onClick.AddListener(() => SelectWithdrawCashAmount(buttonAmount));
            }
        }

        if (withdrawCashButton != null)
            withdrawCashButton.onClick.AddListener(WithdrawAmountLocal);

        withdrawCashPanelWired = true;
    }

    private void SelectAddCashAmount(long selectedAmount)
    {
        amount = selectedAmount;

        if (amountToAdd != null)
            amountToAdd.text = selectedAmount.ToString();
    }

    private void SelectWithdrawCashAmount(long selectedAmount)
    {
        amount = selectedAmount;

        if (amountToWithdraw != null)
            amountToWithdraw.text = selectedAmount.ToString();
    }

    public void WithdrawAmountLocal()
    {
        if (amountToWithdraw == null)
        {
            Debug.LogError("Withdraw amount input is not assigned.");
            ShowWalletMessage(L.T("error.restart_app"));
            return;
        }

        if (!long.TryParse(amountToWithdraw.text, out long parsedAmount) || parsedAmount <= 0)
        {
            ShowWalletMessage(L.T("wallet.enter_withdraw_amount"));
            return;
        }

        amount = parsedAmount;

        if (parsedAmount > currentBalance)
        {
            ShowWalletMessage(L.T("wallet.insufficient"), ToastType.Error);
            return;
        }

        if (APIManager.Instance == null)
        {
            ShowWalletMessage(L.T("error.connect_failed"), ToastType.Error);
            return;
        }

        // OLD: deducted locally (kept for reference — do not delete).
        // currentBalance -= parsedAmount;
        // SaveBalance();
        // long totalWithdrawn = GetLongPref(TOTAL_WITHDRAWN_PREF_KEY, 0);
        // SetLongPref(TOTAL_WITHDRAWN_PREF_KEY, totalWithdrawn + parsedAmount);
        // SetLongPref(LAST_WITHDRAW_AMOUNT_PREF_KEY, parsedAmount);
        // PlayerPrefs.Save();
        // amountToWithdraw.text = "";
        // ShowWalletMessage($"Withdrawn {parsedAmount:N0}");
        // HideWithdrawCashPanel();

        // NEW: server-side withdrawal. The backend enforces KYC + bank details,
        // holds the funds, and an admin approves the payout from the dashboard.
        APIManager.Instance.Withdraw(parsedAmount, "bank", (success, message) =>
        {
            if (this == null) return;   // scene unloaded while the request was in flight
            if (success)
            {
                RefreshFromServer();
                amountToWithdraw.text = "";
                ShowWalletMessage(string.IsNullOrEmpty(message)
                    ? L.T("wallet.withdraw_requested", $"{parsedAmount:N0}")
                    : message, ToastType.Success);
                HideWithdrawCashPanel();
            }
            else
            {
                // Server messages here are actionable — e.g. "Withdrawals require
                // completed KYC verification.", "Add your bank/UPI details before
                // withdrawing." — so they tell the player what to do next.
                ShowWalletMessage(string.IsNullOrEmpty(message) ? L.T("wallet.withdraw_failed") : message,
                    ToastType.Error);
            }
        });
    }

    private bool IsAddCashSubmitButton(Button button)
    {
        string buttonName = button.gameObject.name;
        if (buttonName.IndexOf("AddCash", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        return label != null &&
               label.text.IndexOf("Add Cash", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsWithdrawSubmitButton(Button button)
    {
        string buttonName = button.gameObject.name;
        if (buttonName.IndexOf("Withdraw", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        return label != null &&
               label.text.IndexOf("Withdraw", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool TryGetButtonAmount(Button button, out long buttonAmount)
    {
        if (TryParseAmount(button.gameObject.name, out buttonAmount))
            return true;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        return label != null && TryParseAmount(label.text, out buttonAmount);
    }

    /// <summary>
    /// Shared with TransferPanel so every "quick amount" chip in the app — Add
    /// Cash, Withdraw, Transfer — accepts the exact same text formats ("1000",
    /// "1K", "Rs. 1,000") instead of three copies that could quietly drift apart.
    /// </summary>
    public static bool TryParseAmount(string text, out long parsedAmount)
    {
        parsedAmount = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim()
            .Replace("_Button", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Button", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",", "")
            .Replace("Rs.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Rs", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (value.EndsWith("K", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(value.Substring(0, value.Length - 1), out long thousands))
        {
            parsedAmount = thousands * 1000;
            return parsedAmount > 0;
        }

        return long.TryParse(value, out parsedAmount) && parsedAmount > 0;
    }

    private void HideAddCashPanel()
    {
        if (addCashViewModePanel == null) return;

        addCashViewModePanel.SetActive(false);

        Transform panelParent = addCashViewModePanel.transform.parent;
        if (panelParent != null)
            panelParent.gameObject.SetActive(false);
    }

    private void HideWithdrawCashPanel()
    {
        if (withdrawCashViewModePanel == null) return;

        withdrawCashViewModePanel.SetActive(false);

        Transform panelParent = withdrawCashViewModePanel.transform.parent;
        if (panelParent != null)
            panelParent.gameObject.SetActive(false);
    }

    /// <summary>
    /// PlayerPrefs key for the cached balance, scoped to the signed-in session
    /// so one account's number can never surface under another's on a shared
    /// device. Null when nobody is signed in — there is nothing to cache then,
    /// and falling back to a shared key is exactly the bug this avoids.
    /// </summary>
    private static string BalanceKey()
    {
        if (APIManager.Instance == null) return null;

        string session = APIManager.Instance.SessionFingerprint();
        return session == null ? null : $"{BALANCE_PREF_KEY}_{session}";
    }

    /// <summary>
    /// Drops the cached balance for the session that is ending. APIManager calls
    /// this while the token is still in place, since the key is derived from it.
    /// </summary>
    public static void ForgetCachedBalance()
    {
        string key = BalanceKey();
        if (key != null) PlayerPrefs.DeleteKey(key);

        PlayerPrefs.DeleteKey(BALANCE_PREF_KEY);   // leftover from older builds
        PlayerPrefs.Save();
    }

    private void LoadBalance()
    {
        // Older builds cached the balance under one global key, readable by
        // whoever signed in next. Delete that copy on sight — nothing reads it.
        if (PlayerPrefs.HasKey(BALANCE_PREF_KEY))
        {
            PlayerPrefs.DeleteKey(BALANCE_PREF_KEY);
            PlayerPrefs.Save();
        }

        string key = BalanceKey();

        // Signed out: keep the built-in default and wait for the server rather
        // than showing whatever the last account on this device had.
        if (key == null) return;

        currentBalance = GetLongPref(key, currentBalance);
    }

    private void SaveBalance()
    {
        string key = BalanceKey();
        if (key != null)
        {
            SetLongPref(key, currentBalance);
            PlayerPrefs.Save();
        }

        NotifyUpdate();
    }

    private long GetLongPref(string key, long defaultValue)
    {
        string value = PlayerPrefs.GetString(key, defaultValue.ToString());
        return long.TryParse(value, out long parsedValue) ? parsedValue : defaultValue;
    }

    private void SetLongPref(string key, long value)
    {
        PlayerPrefs.SetString(key, value.ToString());
    }

    private void ShowWalletMessage(string message, ToastType type = ToastType.Info)
    {
        if (string.IsNullOrEmpty(message)) return;

        // UIManagerMenu is scene-scoped, so in the game scene it is null and the
        // message used to vanish — a rejected withdrawal ("Withdrawals require
        // completed KYC verification") looked like a button that did nothing.
        // UIManagerMenu.ShowPopup always Toasts internally now regardless, so
        // this branch and the one below both guarantee a Toast either way —
        // the only difference is whether the styled popup ALSO shows.
        //
        // Passing `type` through matters: the single-arg overload silently
        // dropped severity, so every wallet error/success showed as the same
        // neutral colour instead of red/green.
        if (UIManagerMenu.Instance != null)
        {
            UIManagerMenu.Instance.ShowPopup(message, type);
            return;
        }

        switch (type)
        {
            case ToastType.Success: Toast.Success(message); break;
            case ToastType.Error: Toast.Error(message); break;
            case ToastType.Warning: Toast.Warning(message); break;
            default: Toast.Info(message); break;
        }
    }

    public long GetBalance() => currentBalance;

    /// <summary>
    /// Overwrite the local balance with the authoritative server value. The
    /// server wallet is the source of truth; PlayerPrefs is just a cache.
    /// </summary>
    public void SetBalanceFromServer(long serverBalance)
    {
        if (serverBalance < 0) serverBalance = 0;
        currentBalance = serverBalance;
        SaveBalance(); // persists + animates the UI via NotifyUpdate
    }

    /// <summary>Pull the latest balance from the server.</summary>
    public void RefreshFromServer()
    {
        if (APIManager.Instance == null) return;
        APIManager.Instance.GetWalletBalance((ok, balance) =>
        {
            if (this == null) return;   // scene unloaded while the request was in flight
            if (ok) SetBalanceFromServer(balance);
        });
    }

    /// <summary>
    /// Send money to another user by phone/email. Wire your transfer UI to call
    /// this (e.g. on a "Send" button). On success the balance refreshes from the
    /// server. onResult(success, message) lets the UI show feedback.
    /// </summary>
    public void TransferMoney(string recipientPhoneOrEmail, long amount, Action<bool, string> onResult = null)
    {
        if (string.IsNullOrWhiteSpace(recipientPhoneOrEmail) || amount <= 0)
        {
            onResult?.Invoke(false, L.T("transfer.invalid"));
            return;
        }

        if (APIManager.Instance == null)
        {
            onResult?.Invoke(false, L.T("error.connect_failed"));
            return;
        }

        APIManager.Instance.TransferMoney(recipientPhoneOrEmail, amount, (success, message, data) =>
        {
            if (this == null) return;   // scene unloaded while the request was in flight
            if (success) RefreshFromServer();
            ShowWalletMessage(string.IsNullOrEmpty(message)
                ? (success ? "Transfer successful." : "Transfer failed.")
                : message);
            onResult?.Invoke(success, message);
        });
    }

    private void NotifyUpdate() => OnWalletChanged?.Invoke(currentBalance);
}
