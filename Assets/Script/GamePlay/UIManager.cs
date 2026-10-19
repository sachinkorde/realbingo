using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Handles all game UI: wallet display, bet totals, result panel,
/// betting warnings, panel show/hide, and win/lose animations.
///
/// FIX-UI-01: ResetRoundUI() now has null guards on numberButtons so a
///            missing reference doesn't throw NullReferenceException.
/// FIX-UI-02: UpdateNumberHighlight() has a bounds check on the array.
/// FIX-UI-03: Button listeners cleaned up in OnDestroy to prevent memory leaks.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Top HUD")]
    public TMP_Text walletText;
    public TMP_Text totalBetText;

    [Header("Result Panel")]
    public TMP_Text resultText;
    public TMP_Text winningNumberText;

    [Header("Quotes Settings")]
    public string[] winningQuotes = { "WINNER!", "JACKPOT!", "YOU WON!" };
    public string[] losingQuotes = { "Try Again", "Better Luck!", "So Close!" };

    [Header("Betting Controls")]
    public CanvasGroup bettingPanelGroup;
    public Button okButton;
    public Button cancelButton;
    public Button repeatButton;

    [Tooltip("Assign the Undo button so it locks with the rest once the bet is committed.")]
    public Button undoButton;

    [Tooltip("Deselect all chips when the board locks, so the float animation stops and the tray goes uniformly dim (PRD Topic 5.4 'Post-Bet State').")]
    public bool deselectChipsOnLock = true;

    [Tooltip("How much the Total Bet number pops on each new bet. Keep small — it always returns to its resting size.")]
    [Range(0f, 0.5f)] public float totalBetPunchStrength = 0.1f;
    [Range(0.05f, 1f)] public float totalBetPunchDuration = 0.2f;

    [Header("Timer Warning")]
    public Image timerWarningFlash;
    public Color warningFlashColor = new Color(1f, 0f, 0f, 0.3f);

    [Header("Critical Red Alert (PRD Topic 6.2)")]
    [Tooltip("In the final seconds the screen edge strobes aggressively instead of a single soft fade.")]
    public bool useCriticalStrobe = true;
    [Tooltip("Seconds remaining at which the aggressive strobe takes over.")]
    public int criticalStrobeThreshold = 3;
    [Tooltip("Deep crimson used for the emergency strobe. Alpha is the peak opacity.")]
    public Color criticalStrobeColor = new Color(0.85f, 0f, 0.05f, 0.55f);
    [Tooltip("Flashes packed into each critical second.")]
    [Range(1, 6)] public int criticalStrobeFlashes = 2;

    [Header("Board")]
    public NumberButton[] numberButtons;
    public LastResultsDisplay historyDisplay;

    [Header("Side Panels")]
    public UIPanelTweener walletPanel;
    public UIPanelTweener notificationPanel;
    public UIPanelTweener profilePanel;
    public GameObject addCashPanel;
    public GameObject profilePanelView;
    public GameObject bankdetailsPanel;
    public GameObject withdrawPanel;
    public GameObject transferMoneyPanel;
    public GameObject logoPanel;
    public GameObject outsideClickBlocker;

    [Header("Win Animation")]
    public GameObject winParticles;
    public float winTextPunchScale = 1.3f;

    [Header("Bet Clear VFX (PRD Topic 6.3)")]
    [Tooltip("Smoke/dust poof when Undo or Cancel clears a stake.")]
    public bool useClearEffects = true;
    [Range(0.3f, 3f)] public float clearEffectScale = 1f;

    [Header("Win VFX (PRD Topic 6.5)")]
    [Tooltip("Firework burst over the result panel on a standard win.")]
    public bool useWinFirework = true;
    [Range(0.3f, 3f)] public float winFireworkScale = 1f;
    [Tooltip("Screen-wide gold coin cascade. Reserved for the 'J' jackpot — see PlayJackpotCoinShower().")]
    public float coinShowerDuration = 5f;

    [Header("Music Ducking (PRD Topic 7.3)")]
    [Tooltip("Seconds after the result appears before the background music fades back to full volume.")]
    public float musicRestoreDelay = 3f;

    // Active tweens — killed in OnDestroy to prevent memory leaks
    private Tween warningFlashTween;
    private Tween statusPulseTween;
    private Tween walletCountTween;
    private long displayedWalletValue;

    private Tween winCountTween; // <--- ADD THIS LINE
    private OdometerCounter walletOdometer;

    // Punch state — each element remembers its resting scale so repeated punches
    // can never compound. See PunchOnce().
    private Tween totalBetPunchTween;
    private Vector3 totalBetBaseScale = Vector3.one;
    private bool totalBetBaseCaptured;

    private Tween panelPunchTween;
    private Vector3 panelBaseScale = Vector3.one;
    private bool panelBaseCaptured;
    private bool betsConfirmedThisRound;
    private bool betSubmitSentThisRound;
    private bool bettingInputEnabled;
    [SerializeField] private ChipButton[] chipButtons;

    public bool IsBettingInputEnabled => bettingInputEnabled;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        //DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (GameManager.Instance)
        {
            GameManager.Instance.walletManager.OnWalletChanged += UpdateWalletUI;
            GameManager.Instance.betManager.OnTotalBetChanged += UpdateTotalBetUI;
            GameManager.Instance.betManager.OnBetPlaced += UpdateNumberHighlight;

            displayedWalletValue = GameManager.Instance.walletManager.GetBalance();
            UpdateWalletUI(displayedWalletValue);
        }

        //CacheChipButtons();

        //CloseAddCashPanel();
        CloseAllPanels();
        
        if (okButton) okButton.onClick.AddListener(OnOkClicked);
        if (cancelButton) cancelButton.onClick.AddListener(OnCancelClicked);
        if (repeatButton) repeatButton.onClick.AddListener(OnRepeatClicked);

        // Start warning flash invisible
        if (timerWarningFlash != null)
            timerWarningFlash.color = new Color(
                warningFlashColor.r, warningFlashColor.g, warningFlashColor.b, 0f);
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    public void OnOkClicked()
    {
        ConfirmBets(playClickSound: true, submitBetData: true);

        // NEW: push the player's bets to the server for the current round.
        if (GameManager.Instance != null)
            GameManager.Instance.SubmitBidsToServer();
    }

    public void ConfirmBetsFromTimer()
    {
        ConfirmBets(playClickSound: false, submitBetData: !betSubmitSentThisRound);
    }

    private void ConfirmBets(bool playClickSound, bool submitBetData)
    {
        if (bettingPanelGroup == null) return;

        if (playClickSound)
        {
            AudioManager.Instance?.PlayOneShot("UIButtonClick");

            // "Metallic Haptic Strike" (PRD Topic 7.2): high amplitude, very short.
            // Physically separates the locked board from further input.
            HapticManager.Strike();
        }

        // Visual feedback for confirmation (same for OK and timer)
        PunchBettingPanel();

        if (submitBetData)
{
            MarkBetSubmitSent();
        }

        if (betsConfirmedThisRound)
        {
            SetBettingInteractable(false); // smooth fade-out
            return;
        }

        betsConfirmedThisRound = true;
        SetBettingInteractable(false);
    }

    private void MarkBetSubmitSent()
    {
        if (betSubmitSentThisRound) return;

        betSubmitSentThisRound = true;
        // Front-end guard for the future bet-submit API call.
    }

    public void OnCancelClicked()
    {
        if (GameManager.Instance.CanBet())
        {
            GameManager.Instance.betManager.ClearTable();
            AudioManager.Instance?.PlayOneShot("UIButtonClick");

            PunchBettingPanel();
        }
    }

    public void OnRepeatClicked()
    {
        if (GameManager.Instance.CanBet())
        {
            GameManager.Instance.betManager.RepeatLastBets();
            AudioManager.Instance?.PlayOneShot("UIButtonClick");
        }
    }

    // ── Betting warnings ──────────────────────────────────────────────────────

    public void ShowBettingWarning(int secondsLeft)
    {
        if (timerWarningFlash == null) return;

        warningFlashTween?.Kill();

        // Final seconds: a hard, repeated crimson strobe around the screen edge.
        if (useCriticalStrobe && secondsLeft > 0 && secondsLeft <= criticalStrobeThreshold)
        {
            timerWarningFlash.color = criticalStrobeColor;

            // Pack the flashes into slightly under one second so each strobe
            // finishes before the next countdown tick restarts it.
            float perFlash = 0.9f / Mathf.Max(1, criticalStrobeFlashes);

            Sequence strobe = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            for (int i = 0; i < criticalStrobeFlashes; i++)
            {
                strobe.Append(timerWarningFlash.DOFade(0f, perFlash * 0.5f).SetEase(Ease.InQuad));
                strobe.Append(timerWarningFlash.DOFade(criticalStrobeColor.a, perFlash * 0.5f).SetEase(Ease.OutQuad));
            }
            strobe.Append(timerWarningFlash.DOFade(0f, 0.15f));

            warningFlashTween = strobe;
            return;
        }

        timerWarningFlash.color = warningFlashColor;
        warningFlashTween = timerWarningFlash
            .DOFade(0f, 0.5f)
            .SetEase(Ease.OutQuad);
    }

    /// <summary>
    /// Visual half of the "insufficient balance" rejection (PRD Topic 7.9).
    /// The sound and haptic fire from BetManager; this makes the wallet the
    /// thing the player's eye lands on, so the reason is obvious.
    /// </summary>
    public void ShowInsufficientBalance()
    {
        if (walletText != null)
        {
            walletText.transform.DOKill(true);
            walletText.DOKill();

            walletText.transform.DOShakePosition(0.35f, new Vector3(14f, 0f, 0f), 20, 90f, false, true)
                                .SetUpdate(true);
            walletText.DOColor(Color.red, 0.1f)
                      .SetUpdate(true)
                      .OnComplete(() => walletText.DOColor(Color.white, 0.35f).SetUpdate(true));
        }

        // Double red flash across the screen edge — the PRD's "Double Red Alert Strobe".
        if (timerWarningFlash != null)
        {
            warningFlashTween?.Kill();

            Sequence alert = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            timerWarningFlash.color = criticalStrobeColor;
            alert.Append(timerWarningFlash.DOFade(0f, 0.09f));
            alert.Append(timerWarningFlash.DOFade(criticalStrobeColor.a, 0.09f));
            alert.Append(timerWarningFlash.DOFade(0f, 0.14f));

            warningFlashTween = alert;
        }
    }

    public void ShowBettingClosed()
    {
        if (timerWarningFlash != null)
        {
            warningFlashTween?.Kill();
            timerWarningFlash.color = new Color(1f, 0f, 0f, 0.5f);
            warningFlashTween = timerWarningFlash
                .DOFade(0f, 1f)
                .SetEase(Ease.OutQuad);
        }

        if (bettingPanelGroup != null)
            bettingPanelGroup.transform.DOShakePosition(0.3f, 5f, 20, 90f);
    }

    // ── Results ───────────────────────────────────────────────────────────────

    // ── Results ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Screen-wide gold coin cascade plus the 4-second heavy rumble — the 'J'
    /// jackpot celebration (PRD Topic 6.5 / 7.7).
    ///
    /// Not called yet: the Joker slot does not exist in the wheel or the board.
    /// Call this from the payout path the moment the 'J' result lands.
    /// </summary>
    public void PlayJackpotCoinShower()
    {
        UIEffects.CoinShower(transform, coinShowerDuration);
        HapticManager.Rumble(4f);
    }

    public void ShowResult(int number, long winAmount)
    {
        // The wheel ducked the music when it started spinning; bring it back once
        // the announcement has had its moment (PRD Topic 7.3).
        AudioManager.Instance?.RestoreMusicAfter(musicRestoreDelay);

        // Announcer slang. Silent no-op until clips are assigned on VoiceOverManager.
        if (VoiceOverManager.Instance != null)
        {
            if (winAmount > 0) VoiceOverManager.Instance.PlayWin();
            else if (GameManager.Instance != null &&
                     GameManager.Instance.betManager != null &&
                     GameManager.Instance.betManager.GetGrandTotalBet() > 0)
            {
                VoiceOverManager.Instance.PlayLose();
            }
        }

        if (winningNumberText != null)
        {
            winningNumberText.text = number.ToString();
            winningNumberText.transform.localScale = Vector3.zero;
            winningNumberText.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);
        }

        if (resultText != null)
        {
            winCountTween?.Kill(); // Kill previous counting animation if it's still running

            if (winAmount > 0)
            {
                string quote = winningQuotes.Length > 0
                    ? winningQuotes[Random.Range(0, winningQuotes.Length)]
                    :"";

                // Reset scale and apply the pop effect
                resultText.transform.localScale = Vector3.one;
                resultText.transform.DOPunchScale(Vector3.one * (winTextPunchScale - 1f), 0.5f, 5, 0.5f);

                if (winParticles != null) winParticles.SetActive(true);

                // Firework / sparkler burst on the result panel (PRD Topic 6.5).
                if (useWinFirework)
                    UIEffects.Firework(resultText.transform, winFireworkScale);

                // --- THE MAGIC COUNTING ANIMATION ---
                // This counts from 0 to the winAmount over 1.5 seconds smoothly
                winCountTween = DOVirtual.Float(0f, winAmount, 1.5f, (currentValue) =>
                {
                    long currentDisplay = (long)currentValue;
                    //resultText.text = $"<size=50%><color=#FFD700></size>{quote}</color>\n<size=120%>+{currentDisplay:N0}</size>";
                    resultText.text = currentDisplay.ToString();
                }).SetEase(Ease.OutCubic);
            }
            else
            {
                // Loss -> show 0 in the Winning box (so it doesn't keep showing a
                // previous round's win amount now that it persists between rounds).
                winCountTween?.Kill();
                resultText.text = "0";
            }
        }
    }

    // ── Wallet ────────────────────────────────────────────────────────────────

    // ── Wallet ────────────────────────────────────────────────────────────────

    public void UpdateWalletUI(long balance)
    {
        if (walletText == null) return;

        // Opt-in: if an OdometerCounter is on the wallet text, it owns the number
        // and does the PRD's mechanical digit roll. Without one, the original
        // count-up below runs exactly as before.
        if (walletOdometer == null) walletOdometer = walletText.GetComponent<OdometerCounter>();

        if (walletOdometer != null)
        {
            walletOdometer.SetValue(balance);

            // Keep the colour flash and pop — they are what make the change noticed.
            FlashWalletChange(displayedWalletValue, balance);
            displayedWalletValue = balance;
            return;
        }

        // 1. Kill previous animations to prevent glitching/warping if money is added rapidly
        walletCountTween?.Kill();
        walletText.transform.DOKill(true);
        walletText.transform.localScale = Vector3.one;

        long startValue = displayedWalletValue;
        long endValue = balance;

        // Calculate duration based on how big the difference is (between 0.5s and 2s)
        float duration = Mathf.Clamp(Mathf.Abs(endValue - startValue) / 1000f, 0.5f, 2f);

        // 2. The Counting Animation
        walletCountTween = DOVirtual.Float(startValue, endValue, duration, value =>
        {
            displayedWalletValue = (long)value;
            walletText.text = $"{displayedWalletValue:N0}";
        }).SetEase(Ease.OutQuad);

        // 3. The Color Flash (Green for win/add, Red for bet/deduct)
        Color flashColor = endValue > startValue ? Color.green
                         : endValue < startValue ? Color.red
                         : Color.white;

        walletText.DOColor(flashColor, 0.1f)
                  .OnComplete(() => walletText.DOColor(Color.white, 0.3f));

        // 4. THE MAGIC POP ANIMATION (Makes the player actually notice the change!)
        if (endValue != startValue)
        {
            walletText.transform.DOPunchScale(Vector3.one * 0.25f, 0.3f, 5, 0.5f);
        }
    }

    // ── Bet-clear VFX (PRD Topic 6.3) ─────────────────────────────────────────

    /// <summary>
    /// Smoke/dust poof over one betting slot — the Undo command clearing a
    /// single stake. Does nothing if the slot index is out of range.
    /// </summary>
    public void PlaySlotClearEffect(int number)
    {
        if (!useClearEffects) return;
        if (numberButtons == null) return;
        if (number < 0 || number >= numberButtons.Length) return;
        if (numberButtons[number] == null) return;

        UIEffects.SmokePoof(numberButtons[number].transform, clearEffectScale);
    }

    /// <summary>
    /// Smoke across every slot at once — the Cancel command wiping the board.
    /// The PRD asks for all boxes to clear "simultaneously".
    /// </summary>
    public void PlayBoardClearEffect()
    {
        if (!useClearEffects || numberButtons == null) return;

        for (int i = 0; i < numberButtons.Length; i++)
        {
            if (numberButtons[i] == null) continue;
            UIEffects.SmokePoof(numberButtons[i].transform, clearEffectScale);
        }
    }

    /// <summary>
    /// Green for money in, red for money out, plus a pop so the change is
    /// impossible to miss. Shared by both the odometer and the plain counter.
    /// </summary>
    private void FlashWalletChange(long startValue, long endValue)
    {
        if (walletText == null || startValue == endValue) return;

        Color flashColor = endValue > startValue ? Color.green : Color.red;

        walletText.DOKill();
        walletText.DOColor(flashColor, 0.1f)
                  .OnComplete(() => walletText.DOColor(Color.white, 0.3f));

        walletText.transform.DOKill(true);
        walletText.transform.localScale = Vector3.one;
        walletText.transform.DOPunchScale(Vector3.one * 0.25f, 0.3f, 5, 0.5f);
    }

    public void UpdateTotalBetUI()
    {
        if (totalBetText == null) return;

        long total = GameManager.Instance.betManager.GetGrandTotalBet();
        totalBetText.text = $"{total:N0}";

        PunchOnce(totalBetText.transform, ref totalBetPunchTween,
                  ref totalBetBaseScale, ref totalBetBaseCaptured,
                  totalBetPunchStrength, totalBetPunchDuration, 3);
    }

    /// <summary>
    /// A punch that always starts and ends at the element's resting scale.
    ///
    /// WHY THIS EXISTS
    /// DOPunchScale captures whatever scale it finds at the moment it starts as
    /// its "home" value, and restores to that when it finishes. Fire a second
    /// punch while the first is still mid-flight and it adopts the enlarged
    /// scale as home — so tapping chips quickly compounds the growth and the
    /// number eventually spills out of its box. It never shrinks back on its own
    /// because every punch is doing exactly what it was told.
    ///
    /// Killing the previous tween and snapping back to the captured rest scale
    /// makes each punch independent. The OnComplete is belt-and-braces for the
    /// case where the tween is killed externally mid-flight.
    /// </summary>
    private void PunchOnce(Transform target, ref Tween tween, ref Vector3 baseScale,
                           ref bool captured, float strength, float duration, int vibrato)
    {
        if (target == null) return;

        if (!captured)
        {
            baseScale = target.localScale;   // respect whatever the designer set
            captured = true;
        }

        // Only this punch is killed — never target.DOKill(), which would also
        // destroy tweens other scripts are running on the same object.
        tween?.Kill();
        target.localScale = baseScale;

        Vector3 rest = baseScale;
        tween = target
            .DOPunchScale(Vector3.one * strength, duration, vibrato, 0.5f)
            .SetLink(gameObject)
            .OnComplete(() => target.localScale = rest);
    }

    /// <summary>Confirmation squeeze on the betting panel. Safe to call repeatedly.</summary>
    private void PunchBettingPanel()
    {
        if (bettingPanelGroup == null) return;

        PunchOnce(bettingPanelGroup.transform, ref panelPunchTween,
                  ref panelBaseScale, ref panelBaseCaptured,
                  -0.05f, 0.2f, 5);
    }

    // ── Betting panel control ─────────────────────────────────────────────────

    public void SetBettingInteractable(bool state)
    {
        // This MUST be set before anything can early-out: GameManager.CanBet()
        // reads it, so a missed assignment would leave betting open after the
        // board was meant to lock.
        bettingInputEnabled = state;

        // Disable/enable through the CanvasGroup so the chips + number buttons that
        // are its children fade SMOOTHLY together, instead of each button snapping
        // to its disabled tint.
        if (bettingPanelGroup != null)
        {
            bettingPanelGroup.interactable = state;
            bettingPanelGroup.blocksRaycasts = state;

            bettingPanelGroup.DOKill();
            bettingPanelGroup.DOFade(state ? 1f : 0.5f, 0.4f).SetEase(Ease.OutQuad);
        }

        // The CanvasGroup only reaches its own children. In this scene the chips,
        // number grid and control buttons are siblings under the canvas, not
        // children of the betting panel, so each is locked explicitly.
        SetBettingControlsInteractable(state);
    }

    /// <summary>
    /// Locks every control the player could bet with once the round is committed:
    /// OK, Cancel, Undo, Repeat, all ten chips and the 0-9 grid.
    ///
    /// On lock the chips are also deselected, which stops the floating animation
    /// and returns them to a uniform dim/static state — PRD Topic 5.4's
    /// "Post-Bet State".
    ///
    /// Every reference is optional; unassigned ones are simply skipped.
    /// </summary>
    private void SetBettingControlsInteractable(bool state)
    {
        if (okButton) okButton.interactable = state;
        if (cancelButton) cancelButton.interactable = state;
        if (repeatButton) repeatButton.interactable = state;
        if (undoButton) undoButton.interactable = state;

        SetChipButtonsInteractable(state);

        if (numberButtons != null)
        {
            foreach (NumberButton numberButton in numberButtons)
            {
                if (numberButton == null) continue;
                numberButton.SetInteractable(state);
            }
        }

        // Stop the selected chip bobbing and dim the whole tray the moment the
        // bet is locked in.
        if (!state && deselectChipsOnLock) ChipButton.ClearAllSelections();
    }

    /*private void CacheChipButtons()
    {
        if (bettingPanelGroup != null)
        {
            chipButtons = bettingPanelGroup.GetComponentsInChildren<ChipButton>(true);
        }

        if (chipButtons == null || chipButtons.Length == 0)
        {
            chipButtons = FindObjectsByType<ChipButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }*/

    private void SetChipButtonsInteractable(bool state)
    {
        // This now runs every round from SetBettingInteractable, so an unassigned
        // array must not throw. (The guard here was previously commented out.)
        if (chipButtons == null) return;

        foreach (ChipButton chipButton in chipButtons)
        {
            if (chipButton == null) continue;
            chipButton.SetInteractable(state);
        }
    }

    public void ResetRoundUI()
    {
        betsConfirmedThisRound = false;
        betSubmitSentThisRound = false;

        // NO DEAD TIME: keep the Winning amount (resultText) visible into the next
        // round — it is overwritten when the next result is shown. Only the bet
        // board, chips, particles and the wheel number are reset here.
        if (winParticles != null) winParticles.SetActive(false);
        if (winningNumberText != null) winningNumberText.text = "";

        if (numberButtons != null)
        {
            foreach (var btn in numberButtons)
            {
                if (btn == null) continue;
                btn.SetAmount(0);
                btn.SetHighlight(false);
            }
        }

        ChipButton.ClearAllSelections();
    }

    // ── History & highlights ──────────────────────────────────────────────────

    public void UpdateHistory(List<int> history)
    {
        historyDisplay?.SetResults(history);
    }

    private void UpdateNumberHighlight(int number)
    {
        // FIX-UI-02: bounds check before array access
        if (numberButtons == null) return;
        if (number < 0 || number >= numberButtons.Length) return;
        if (numberButtons[number] == null) return;

        long currentBet = GameManager.Instance.betManager.GetTotalBetOnNumber(number);
        numberButtons[number].SetHighlight(currentBet > 0);
    }

    // ── Panel controls ────────────────────────────────────────────────────────

    public void ToggleWalletPanel()
    {
        if (walletPanel == null) return;
        walletPanel.Toggle();
        SetBlocker(walletPanel.IsOpen);
    }

    public void ToggleProfilePanel()
    {
        if (profilePanel == null) return;
        profilePanel.Toggle();
        SetBlocker(profilePanel.IsOpen);
    }

    public void OpenAddCashPanel()
    {
        if (addCashPanel == null) return;
        CloseAllPanels();
        addCashPanel.transform.parent.gameObject.SetActive(true);
        logoPanel.SetActive(true);
        addCashPanel.SetActive(true);
    }

    public void OpenProfilePanel()
    {
        if (profilePanelView == null) return;
        CloseAllPanels();
        profilePanelView.transform.parent.gameObject.SetActive(true);
        logoPanel.SetActive(true);
        profilePanelView.SetActive(true);
    }

    public void OpenWithdrawPanel()
    {
        if (withdrawPanel == null) return;
        CloseAllPanels();
        withdrawPanel.transform.parent.gameObject.SetActive(true);
        logoPanel.SetActive(true);
        withdrawPanel.SetActive(true);
    }

    public void OpenTransferCashPanel()
    {
        if (transferMoneyPanel == null) return;
        CloseAllPanels();
        transferMoneyPanel.transform.parent.gameObject.SetActive(true);
        logoPanel.SetActive(true);
        transferMoneyPanel.SetActive(true);
    }

    public void CloseAddCashPanel()
    {
        if (addCashPanel == null) return;
        addCashPanel.transform.parent.gameObject.SetActive(false);
        logoPanel.SetActive(false);
        addCashPanel.SetActive(false);
    }

    public void CloseProfilePanel()
    {
        if (profilePanelView == null) return;
        profilePanelView.transform.parent.gameObject.SetActive(false);
        logoPanel.SetActive(false);
        profilePanelView.SetActive(false);
    }

    public void ToggleNotificationPanel()
    {
        if (notificationPanel == null) return;

        notificationPanel.Toggle();
        SetBlocker(notificationPanel.IsOpen);

        // --- ADD THIS BLOCK ---
        // If the panel just opened, spawn the messages and clear the badge
        if (notificationPanel.IsOpen && NotificationManager.Instance != null)
        {
            NotificationManager.Instance.PopulateScrollView();
        }
    }

    public void CloseAllPanels()
    {
        addCashPanel.SetActive(false);
        withdrawPanel.SetActive(false);
        bankdetailsPanel.SetActive(false);
        transferMoneyPanel.SetActive(false);
        logoPanel.SetActive(false);
        profilePanelView.SetActive(false);
        walletPanel?.Close();
        profilePanel?.Close();
        notificationPanel?.Close();
        SetBlocker(false);

        Transform parentPanel = addCashPanel.transform.parent.transform;
        for (int i = 0; i < parentPanel.childCount; i++)
        {
            parentPanel.GetChild(i).gameObject.SetActive(false);
        }
    }

    private void SetBlocker(bool active)
    {
        if (outsideClickBlocker != null)
            outsideClickBlocker.SetActive(active);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        // FIX-UI-03: Kill tweens to avoid memory leaks / callbacks on dead objects
        warningFlashTween?.Kill();
        statusPulseTween?.Kill();
        walletCountTween?.Kill();
        winCountTween?.Kill();
        totalBetPunchTween?.Kill();
        panelPunchTween?.Kill();

        // Unsubscribe events
        if (GameManager.Instance)
        {
            GameManager.Instance.walletManager.OnWalletChanged -= UpdateWalletUI;
            GameManager.Instance.betManager.OnTotalBetChanged -= UpdateTotalBetUI;
            GameManager.Instance.betManager.OnBetPlaced -= UpdateNumberHighlight;
        }
    }
}
