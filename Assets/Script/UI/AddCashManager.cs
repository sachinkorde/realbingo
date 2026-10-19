using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Add Cash Manager - Handles wallet top-up functionality
/// Features: Quick amounts, custom input, payment gateway integration ready
/// </summary>
public class AddCashManager : MonoBehaviour
{
    public static AddCashManager Instance { get; private set; }

    [Header("Balance Display")]
    [SerializeField] private TMP_Text currentBalanceText;
    [SerializeField] private TMP_Text newBalancePreviewText;

    [Header("Quick Amount Buttons")]
    [SerializeField] private QuickAmountButton[] quickAmountButtons;

    [Header("Custom Amount")]
    [SerializeField] private TMP_InputField customAmountInput;
    [SerializeField] private Button clearAmountButton;
    [SerializeField] private TMP_Text minAmountHintText;
    [SerializeField] private TMP_Text maxAmountHintText;

    [Header("Amount Limits")]
    [SerializeField] private long minAmount = 100;
    [SerializeField] private long maxAmount = 100000;

    [Header("Action Buttons")]
    [SerializeField] private Button proceedButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text proceedButtonText;

    [Header("Promo Code")]
    [SerializeField] private TMP_InputField promoCodeInput;
    [SerializeField] private Button applyPromoButton;
    [SerializeField] private TMP_Text promoStatusText;
    [SerializeField] private GameObject promoSuccessIcon;
    [SerializeField] private GameObject promoErrorIcon;

    [Header("Payment Methods")]
    [SerializeField] private PaymentMethodButton[] paymentMethods;
    [SerializeField] private TMP_Text selectedPaymentText;

    [Header("Bonus Display")]
    [SerializeField] private GameObject bonusPanel;
    [SerializeField] private TMP_Text bonusAmountText;
    [SerializeField] private TMP_Text bonusPercentText;

    [Header("Loading")]
    [SerializeField] private GameObject loadingPanel;

    [Header("Animations")]
    [SerializeField] private float buttonPunchScale = 0.1f;
    [SerializeField] private float animDuration = 0.2f;

    // Events
    public event Action<long> OnAmountSelected;
    public event Action<string> OnPaymentMethodSelected;
    public event Action<bool, long> OnTransactionComplete;

    private long selectedAmount = 0;
    private string selectedPaymentMethod = "";
    private string appliedPromoCode = "";
    private float promoDiscountPercent = 0;
    private long currentBalance = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        SetupUI();
        SetupButtons();
        UpdateUI();
    }

    private void SetupUI()
    {
        if (minAmountHintText != null)
            minAmountHintText.text = $"Min: {minAmount:N0}";
        
        if (maxAmountHintText != null)
            maxAmountHintText.text = $"Max: {maxAmount:N0}";

        // Setup quick amount buttons
        foreach (var btn in quickAmountButtons)
        {
            if (btn != null)
            {
                btn.Initialize(OnQuickAmountClicked);
            }
        }

        // Setup payment method buttons
        foreach (var pm in paymentMethods)
        {
            if (pm != null)
            {
                pm.Initialize(OnPaymentMethodClicked);
            }
        }

        // Hide bonus panel initially
        if (bonusPanel != null) bonusPanel.SetActive(false);
        
        // Hide promo status
        if (promoSuccessIcon != null) promoSuccessIcon.SetActive(false);
        if (promoErrorIcon != null) promoErrorIcon.SetActive(false);
    }

    private void SetupButtons()
    {
        // Custom amount input
        customAmountInput?.onValueChanged.AddListener(OnCustomAmountChanged);
        customAmountInput?.onEndEdit.AddListener(OnCustomAmountEndEdit);
        
        clearAmountButton?.onClick.AddListener(ClearAmount);
        proceedButton?.onClick.AddListener(OnProceedClicked);
        cancelButton?.onClick.AddListener(OnCancelClicked);
        applyPromoButton?.onClick.AddListener(OnApplyPromoClicked);
    }

    #region Public Methods

    public void Show()
    {
        gameObject.SetActive(true);
        RefreshBalance();
        ClearAmount();
        ClearPromo();
        
        // Animate in
        transform.localScale = Vector3.zero;
        transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
    }

    public void Hide()
    {
        transform.DOScale(0f, 0.2f).SetEase(Ease.InBack).OnComplete(() =>
        {
            gameObject.SetActive(false);
        });
    }

    public void RefreshBalance()
    {
        if (WalletManager.Instance != null)
        {
            currentBalance = WalletManager.Instance.GetBalance();
            UpdateBalanceDisplay();
        }
    }

    public void SetAmount(long amount)
    {
        selectedAmount = (long)Mathf.Clamp(amount, 0, maxAmount);
        
        if (customAmountInput != null)
            customAmountInput.text = selectedAmount > 0 ? selectedAmount.ToString() : "";
        
        UpdateUI();
        OnAmountSelected?.Invoke(selectedAmount);
    }

    #endregion

    #region Button Callbacks

    private void OnQuickAmountClicked(long amount)
    {
        PlayButtonSound();
        
        // Deselect all quick buttons
        foreach (var btn in quickAmountButtons)
        {
            btn?.SetSelected(btn.GetAmount() == amount);
        }
        
        SetAmount(amount);
        
        // Visual feedback
        if (customAmountInput != null)
        {
            customAmountInput.transform.DOPunchScale(Vector3.one * buttonPunchScale, animDuration, 5, 0.5f);
        }
    }

    private void OnPaymentMethodClicked(string methodId)
    {
        PlayButtonSound();
        selectedPaymentMethod = methodId;
        
        // Update selection visuals
        foreach (var pm in paymentMethods)
        {
            pm?.SetSelected(pm.GetMethodId() == methodId);
        }
        
        if (selectedPaymentText != null)
        {
            selectedPaymentText.text = GetPaymentMethodName(methodId);
        }
        
        OnPaymentMethodSelected?.Invoke(methodId);
        UpdateUI();
    }

    private void OnCustomAmountChanged(string value)
    {
        // Remove non-numeric characters
        string cleanValue = "";
        foreach (char c in value)
        {
            if (char.IsDigit(c)) cleanValue += c;
        }
        
        if (cleanValue != value && customAmountInput != null)
        {
            customAmountInput.text = cleanValue;
        }
        
        // Deselect quick buttons
        foreach (var btn in quickAmountButtons)
        {
            btn?.SetSelected(false);
        }
        
        if (long.TryParse(cleanValue, out long amount))
        {
            selectedAmount = amount;
        }
        else
        {
            selectedAmount = 0;
        }
        
        UpdateUI();
    }

    private void OnCustomAmountEndEdit(string value)
    {
        if (long.TryParse(value, out long amount))
        {
            if (amount < minAmount && amount > 0)
            {
                ShowNotification($"Minimum amount is {minAmount:N0}");
                SetAmount(minAmount);
            }
            else if (amount > maxAmount)
            {
                ShowNotification($"Maximum amount is {maxAmount:N0}");
                SetAmount(maxAmount);
            }
        }
    }

    private void OnApplyPromoClicked()
    {
        PlayButtonSound();
        
        string code = promoCodeInput?.text?.Trim().ToUpper() ?? "";
        
        if (string.IsNullOrEmpty(code))
        {
            ShowPromoError("Please enter a promo code");
            return;
        }
        
        // Validate promo code (in real app, call API)
        ValidatePromoCode(code);
    }

    private void OnProceedClicked()
    {
        PlayButtonSound();
        
        // Validation
        if (selectedAmount < minAmount)
        {
            ShowNotification($"Minimum amount is {minAmount:N0}");
            ShakeInput(customAmountInput);
            return;
        }
        
        if (selectedAmount > maxAmount)
        {
            ShowNotification($"Maximum amount is {maxAmount:N0}");
            return;
        }
        
        if (string.IsNullOrEmpty(selectedPaymentMethod))
        {
            ShowNotification("Please select a payment method");
            return;
        }
        
        // Proceed to payment
        InitiatePayment();
    }

    private void OnCancelClicked()
    {
        PlayButtonSound();
        Hide();
    }

    private void ClearAmount()
    {
        selectedAmount = 0;
        
        if (customAmountInput != null)
            customAmountInput.text = "";
        
        foreach (var btn in quickAmountButtons)
        {
            btn?.SetSelected(false);
        }
        
        UpdateUI();
    }

    #endregion

    #region Promo Code

    private void ValidatePromoCode(string code)
    {
        ShowLoading();
        
        // Simulate API call - replace with actual API
        StartCoroutine(SimulatePromoValidation(code));
    }

    private IEnumerator SimulatePromoValidation(string code)
    {
        yield return new WaitForSeconds(1f);
        HideLoading();
        
        // Example promo codes
        if (code == "BONUS10")
        {
            appliedPromoCode = code;
            promoDiscountPercent = 10;
            ShowPromoSuccess($"10% bonus applied!");
        }
        else if (code == "WELCOME50")
        {
            appliedPromoCode = code;
            promoDiscountPercent = 50;
            ShowPromoSuccess($"50% bonus applied!");
        }
        else
        {
            ShowPromoError("Invalid promo code");
        }
        
        UpdateUI();
    }

    private void ShowPromoSuccess(string message)
    {
        if (promoStatusText != null)
        {
            promoStatusText.text = message;
            promoStatusText.color = Color.green;
        }
        if (promoSuccessIcon != null) promoSuccessIcon.SetActive(true);
        if (promoErrorIcon != null) promoErrorIcon.SetActive(false);

        HapticManager.Success();
    }

    private void ShowPromoError(string message)
    {
        if (promoStatusText != null)
        {
            promoStatusText.text = message;
            promoStatusText.color = Color.red;
        }
        if (promoSuccessIcon != null) promoSuccessIcon.SetActive(false);
        if (promoErrorIcon != null) promoErrorIcon.SetActive(true);

        HapticManager.Failure();

        appliedPromoCode = "";
        promoDiscountPercent = 0;
    }

    private void ClearPromo()
    {
        if (promoCodeInput != null) promoCodeInput.text = "";
        if (promoStatusText != null) promoStatusText.text = "";
        if (promoSuccessIcon != null) promoSuccessIcon.SetActive(false);
        if (promoErrorIcon != null) promoErrorIcon.SetActive(false);
        
        appliedPromoCode = "";
        promoDiscountPercent = 0;
    }

    #endregion

    #region Payment

    private void InitiatePayment()
    {
        ShowLoading();
        
        // Calculate final amount with bonus
        long finalAmount = selectedAmount;
        long bonusAmount = (long)(selectedAmount * promoDiscountPercent / 100f);
        long totalCredit = finalAmount + bonusAmount;
        
        // In real implementation, integrate with payment gateway
        // For now, simulate success
        StartCoroutine(SimulatePayment(totalCredit));
    }

    private IEnumerator SimulatePayment(long amount)
    {
        yield return new WaitForSeconds(2f);
        HideLoading();
        
        // Simulate success
        bool success = UnityEngine.Random.value > 0.1f; // 90% success rate for demo
        
        if (success)
        {
            // Add to wallet
            WalletManager.Instance?.AddAmount(amount);
            
            ShowNotification($"{amount:N0} added successfully!");
            HapticManager.Success();
            OnTransactionComplete?.Invoke(true, amount);

            DOVirtual.DelayedCall(1.5f, Hide);
        }
        else
        {
            ShowNotification("Payment failed. Please try again.");
            HapticManager.Failure();
            OnTransactionComplete?.Invoke(false, 0);
        }
    }

    #endregion

    #region UI Updates

    private void UpdateUI()
    {
        UpdateBalanceDisplay();
        UpdateBonusDisplay();
        UpdateProceedButton();
    }

    private void UpdateBalanceDisplay()
    {
        if (currentBalanceText != null)
            currentBalanceText.text = $"{currentBalance:N0}";
        
        if (newBalancePreviewText != null)
        {
            long bonus = (long)(selectedAmount * promoDiscountPercent / 100f);
            long newBalance = currentBalance + selectedAmount + bonus;
            newBalancePreviewText.text = $"{newBalance:N0}";
            
            // Animate if changed
            if (selectedAmount > 0)
            {
                newBalancePreviewText.transform.DOPunchScale(Vector3.one * 0.1f, 0.2f, 5, 0.5f);
            }
        }
    }

    private void UpdateBonusDisplay()
    {
        if (bonusPanel == null) return;
        
        if (promoDiscountPercent > 0 && selectedAmount > 0)
        {
            bonusPanel.SetActive(true);
            
            long bonusAmount = (long)(selectedAmount * promoDiscountPercent / 100f);
            
            if (bonusAmountText != null)
                bonusAmountText.text = $"+{bonusAmount:N0}";
            
            if (bonusPercentText != null)
                bonusPercentText.text = $"{promoDiscountPercent}% Bonus";
        }
        else
        {
            bonusPanel.SetActive(false);
        }
    }

    private void UpdateProceedButton()
    {
        bool canProceed = selectedAmount >= minAmount && !string.IsNullOrEmpty(selectedPaymentMethod);
        
        if (proceedButton != null)
        {
            proceedButton.interactable = canProceed;
        }
        
        if (proceedButtonText != null)
        {
            if (selectedAmount > 0)
            {
                long bonus = (long)(selectedAmount * promoDiscountPercent / 100f);
                long total = selectedAmount + bonus;
                proceedButtonText.text = $"Add {total:N0}";
            }
            else
            {
                proceedButtonText.text = "Enter Amount";
            }
        }
    }

    private string GetPaymentMethodName(string methodId)
    {
        switch (methodId)
        {
            case "upi": return "UPI";
            case "card": return "Credit/Debit Card";
            case "netbanking": return "Net Banking";
            case "wallet": return "Wallet";
            default: return methodId;
        }
    }

    #endregion

    #region Helpers

    private void ShowLoading()
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
    }

    private void HideLoading()
    {
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    private void ShowNotification(string message)
    {
        UIManagerMenu.Instance?.ShowPopup(message);
    }

    private void PlayButtonSound()
    {
        AudioManager.Instance?.PlayOneShot("UIButtonClick");
    }

    private void ShakeInput(TMP_InputField input)
    {
        if (input != null)
        {
            input.transform.DOShakePosition(0.3f, 10f, 20, 90f);
        }
    }

    #endregion
}

/// <summary>
/// Quick amount button component
/// </summary>
[Serializable]
public class QuickAmountButton : MonoBehaviour
{
    [SerializeField] private long amount;
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = Color.yellow;
    
    private Action<long> onClick;
    
    public void Initialize(Action<long> callback)
    {
        onClick = callback;
        
        if (button == null) button = GetComponent<Button>();
        button?.onClick.AddListener(() => onClick?.Invoke(amount));
        
        if (amountText != null)
            amountText.text = $"{amount:N0}";
        
        SetSelected(false);
    }
    
    public void SetSelected(bool selected)
    {
        if (backgroundImage != null)
        {
            backgroundImage.DOColor(selected ? selectedColor : normalColor, 0.2f);
        }
        
        if (selected)
        {
            transform.DOScale(1.1f, 0.2f).SetEase(Ease.OutBack);
        }
        else
        {
            transform.DOScale(1f, 0.15f);
        }
    }
    
    public long GetAmount() => amount;
}

/// <summary>
/// Payment method button component
/// </summary>
[Serializable]
public class PaymentMethodButton : MonoBehaviour
{
    [SerializeField] private string methodId;
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image selectionIndicator;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = Color.green;
    
    private Action<string> onClick;
    
    public void Initialize(Action<string> callback)
    {
        onClick = callback;
        
        if (button == null) button = GetComponent<Button>();
        button?.onClick.AddListener(() => onClick?.Invoke(methodId));
        
        SetSelected(false);
    }
    
    public void SetSelected(bool selected)
    {
        if (selectionIndicator != null)
        {
            selectionIndicator.gameObject.SetActive(selected);
            if (selected)
            {
                selectionIndicator.transform.localScale = Vector3.zero;
                selectionIndicator.transform.DOScale(1f, 0.2f).SetEase(Ease.OutBack);
            }
        }
        
        if (iconImage != null)
        {
            iconImage.DOColor(selected ? selectedColor : normalColor, 0.2f);
        }
    }
    
    public string GetMethodId() => methodId;
}
