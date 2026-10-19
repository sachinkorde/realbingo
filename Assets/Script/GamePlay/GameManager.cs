using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RealBingo.Localization;
using System.Linq;
using DG.Tweening;

public enum GameState { Betting, BettingClosed, Spinning, Payout }

/// <summary>
/// Core game manager — controls the full round loop:
///
///   BETTING (countdown 60→1)
///     → BETTING CLOSED (1 second buffer, timer shows 0)
///       → SPINNING (wheel spins to result)
///         → PAYOUT (show result for 5 seconds)
///           → repeat
///
/// HOW THE TIMER WORKS:
///   totalRoundDuration = 60
///   bettingCloseBeforeSpin = 1
///   bettingTime = 60 - 1 = 59 seconds
///   Display formula: CeilToInt(timeLeft + bettingCloseBeforeSpin)
///   → starts at 60, counts to 1, then BettingClosed shows 0.
///   So the player sees a clean 60 → 0 countdown.
///
/// FIX-GM-01: GameLoop was commented out in Start() — timer never ran.
/// FIX-GM-02: SpinManager, TimerController, and all betting fields restored.
/// FIX-GM-03: CalculateRiggedWinner restored — picks the number with the
///            lowest total bet so the house always favours itself.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Time Settings")]
    [Tooltip("Full countdown length in seconds. Players see this number count to 0.")]
    public float totalRoundDuration = 60f;

    [Header("Betting Close Settings")]
    [Tooltip("Seconds before spin starts when betting is locked. " +
             "The timer will show 0 during this buffer.")]
    public float bettingCloseBeforeSpin = 1f;

    [Tooltip("Play warning sounds + flash when this many seconds are left in betting.")]
    public bool showBettingCloseWarning = true;
    public int warningStartSeconds = 10;

    [Header("Audio Settings")]
    public string gameBgSoundKey = "BGM_Game";
    public float gameBgRestartCheckInterval = 1f;

    [Header("Payout Settings")]
    public int payoutMultiplier = 9;
    public float payoutDisplayDuration = 5f;

    [Header("Live Data")]
    public GameState currentState;
    public List<int> history = new List<int>();

    [Header("References")]
    public BetManager betManager;
    public SpinManager spinManager;      // FIX-GM-02: restored
    public WalletManager walletManager;
    public UIManager uiManager;
    public TimerController timerController;  // FIX-GM-02: restored

    // ── Events (other scripts can subscribe) ─────────────────────────────────
    public System.Action<int> OnBettingTimeWarning;
    public System.Action OnBettingClosed;
    public System.Action<int> OnSpinComplete;
    private float nextGameBgCheckTime;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        if (string.IsNullOrWhiteSpace(gameBgSoundKey))
        {
            gameBgSoundKey = "BGM_Game";
        }
    }

    private void Start()
    {
        Application.targetFrameRate = 60;

        if (AudioManager.Instance)
            AudioManager.Instance.Play(gameBgSoundKey);

        // OLD local loop (kept for reference — do not delete):
        // StartCoroutine(GameLoop());
        //
        // NEW: server-synchronized loop. The backend owns the timer, the winning
        // number, and the wallet, so all devices count down / spin / reveal together.
        StartCoroutine(ServerGameLoop());
    }

    private void Update()
    {
        if (AudioManager.Instance == null || string.IsNullOrEmpty(gameBgSoundKey)) return;
        if (Time.unscaledTime < nextGameBgCheckTime) return;

        nextGameBgCheckTime = Time.unscaledTime + Mathf.Max(0.25f, gameBgRestartCheckInterval);
        AudioManager.Instance.EnsureLoopPlaying(gameBgSoundKey);
    }

    // ── State query ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true only when it is safe to place a bet.
    /// Called by BetManager before accepting any bet.
    /// </summary>
    public bool CanBet()
    {
        return currentState == GameState.Betting &&
               uiManager != null &&
               uiManager.IsBettingInputEnabled;
    }

    public GameState GetCurrentState() => currentState;
    public bool IsInPayout() => currentState == GameState.Payout;

    /* ===========================================================================
     * OLD client-side loop + client-side "rigged winner" (kept for reference,
     * replaced by the server-synchronized ServerGameLoop below — do not delete).
     * ===========================================================================

    // ── Main game loop ────────────────────────────────────────────────────────

    private IEnumerator GameLoop()
    {
        while (true)
        {
            // Pick the spin duration now so we can calculate betting time correctly.
            float spinDuration = Random.Range(spinManager.minDuration, spinManager.maxDuration);

            // Betting window = total round(60) - spinDuration(e.g., 8) - buffer(1)
            float bettingTime = totalRoundDuration - spinDuration - bettingCloseBeforeSpin;

            // Failsafe in case settings are weird
            if (bettingTime <= 0) bettingTime = 10f;

            // Step 1 — Countdown while betting is open
            yield return StartCoroutine(BettingPhase(bettingTime));

            // Step 2 — Lock betting for 1 second, show "0" on timer
            yield return StartCoroutine(BettingClosedPhase());

            // Step 3 — Spin to the winning number
            int winner = CalculateRiggedWinner();
            yield return StartCoroutine(SpinningPhase(winner, spinDuration));

            // Step 4 — Show result and award winnings
            yield return StartCoroutine(PayoutPhase(winner));
        }
    }

    // ── Phase coroutines ─────────────────────────────────────────────────────

    /// <summary>
    /// Countdown phase — timer goes from totalRoundDuration → 1.
    /// Betting is open. Warning sounds fire in the last warningStartSeconds.
    /// </summary>
    private IEnumerator BettingPhase(float bettingTime)
    {
        currentState = GameState.Betting;
        betManager.ResetForNewRound();
        uiManager.ResetRoundUI();
        uiManager.SetBettingInteractable(true);

        float timeLeft = bettingTime;

        // Display starts at totalRoundDuration (e.g. 60) because we add
        // bettingCloseBeforeSpin (1) back into the display value.
        int lastDisplayedTime = Mathf.CeilToInt(timeLeft + bettingCloseBeforeSpin);
        timerController.UpdateTimerDisplay(lastDisplayedTime);

        while (timeLeft > 0)
        {
            timeLeft -= Time.deltaTime;

            // Only update the display when the integer second changes.
            // This prevents calling UpdateTimerDisplay every single frame.
            int currentDisplayTime = Mathf.CeilToInt(timeLeft + bettingCloseBeforeSpin);

            if (currentDisplayTime != lastDisplayedTime)
            {
                timerController.UpdateTimerDisplay(currentDisplayTime);

                // Warning phase — play tick sounds + flash UI in last N seconds
                if (showBettingCloseWarning &&
                    currentDisplayTime <= warningStartSeconds &&
                    currentDisplayTime > 0)
                {
                    AudioManager.Instance?.PlayCountdownTick("TimerTick", currentDisplayTime, warningStartSeconds);
                    OnBettingTimeWarning?.Invoke(currentDisplayTime);
                    uiManager.ShowBettingWarning(currentDisplayTime);
                }

                lastDisplayedTime = currentDisplayTime;
            }

            yield return null;
        }
    }

    /// <summary>
    /// 1-second buffer after betting closes — timer shows 0, UI locked.
    /// </summary>
    private IEnumerator BettingClosedPhase()
    {
        currentState = GameState.BettingClosed;

        uiManager.ConfirmBetsFromTimer();
        uiManager.ShowBettingClosed();

        AudioManager.Instance?.PlayOneShot("BetClosed");
        OnBettingClosed?.Invoke();

        // Save bets so the player can Repeat them next round
        betManager.SaveCurrentAsLastRound();

        // Count down 1 → 0 visually
        float closeTime = bettingCloseBeforeSpin;
        timerController.UpdateTimerDisplay(Mathf.CeilToInt(closeTime));

        while (closeTime > 0)
        {
            closeTime -= Time.deltaTime;
            timerController.UpdateTimerDisplay(Mathf.Max(0, Mathf.CeilToInt(closeTime)));
            yield return null;
        }

        timerController.UpdateTimerDisplay(0);
    }

    /// <summary>
    /// Wheel spins to the pre-determined winner.
    /// GameState stays Spinning until SpinManager fires its callback.
    /// </summary>
    private IEnumerator SpinningPhase(int winner, float spinDuration)
    {
        currentState = GameState.Spinning;
        uiManager.SetBettingInteractable(false);

        bool spinComplete = false;

        spinManager.StartSpin(winner, spinDuration, () =>
        {
            spinComplete = true;
            OnSpinComplete?.Invoke(winner);
        });

        // Wait here until SpinManager calls the callback
        yield return new WaitUntil(() => spinComplete);
    }

    /// <summary>
    /// Show payout result for payoutDisplayDuration seconds, then loop.
    /// </summary>
    private IEnumerator PayoutPhase(int winningNumber)
    {
        currentState = GameState.Payout;
        CalculateWinnings(winningNumber);
        yield return new WaitForSeconds(payoutDisplayDuration);
    }

    // ── Winner calculation ────────────────────────────────────────────────────

    /// <summary>
    /// "Rigged" winner — always picks the number with the LOWEST total bet
    /// so the house payout is minimised. Ties are broken randomly.
    /// If no bets exist at all, returns a fully random number.
    /// </summary>
    private int CalculateRiggedWinner()
    {
        int totalSlots = spinManager.angleSettings.Count;
        if (totalSlots == 0) return 0;

        long minBetAmount = long.MaxValue;
        List<int> lowBetNumbers = new List<int>();

        for (int i = 0; i < totalSlots; i++)
        {
            long currentBet = betManager.GetTotalBetOnNumber(i);

            if (currentBet < minBetAmount)
            {
                minBetAmount = currentBet;
                lowBetNumbers.Clear();
                lowBetNumbers.Add(i);
            }
            else if (currentBet == minBetAmount)
            {
                lowBetNumbers.Add(i);
            }
        }

        if (lowBetNumbers.Count > 0)
            return lowBetNumbers[Random.Range(0, lowBetNumbers.Count)];

        return Random.Range(0, totalSlots);
    }

    // ── Payout calculation ────────────────────────────────────────────────────

    private void CalculateWinnings(int winningNumber)
    {
        long betAmount = betManager.GetTotalBetOnNumber(winningNumber);
        long winAmount = betAmount * payoutMultiplier;

        if (winAmount > 0)
        {
            walletManager.AddAmount(winAmount);
            AudioManager.Instance?.PlayOneShot("Win");
        }
        else
        {
            if (betManager.GetGrandTotalBet() > 0)
                AudioManager.Instance?.PlayOneShot("Lose");
        }

        history.Insert(0, winningNumber);
        if (history.Count > 10) history.RemoveAt(history.Count - 1);

        uiManager.ShowResult(winningNumber, winAmount);
        uiManager.UpdateHistory(history);

        // --- ADD THESE TWO LINES HERE ---
        long currentBalance = walletManager.GetBalance();
        NotificationManager.Instance?.AddNotification($"Winning Number: {winningNumber} | Balance: ₹{currentBalance:N0}");
    }

     * END OLD loop ============================================================ */

    // ── NEW: clock-synced game loop ────────────────────────────────────────────
    // Syncs the server clock ONCE (then re-syncs only every couple of minutes for
    // drift) and computes the session + countdown LOCALLY every tick. So every
    // device shows the exact same second with NO per-second API calls. One light
    // call per session fetches the winning number at spin time.

    [Header("Server Sync")]
    [Tooltip("Panel shown when the device has no internet (assign a 'Please connect to internet' UI).")]
    public GameObject offlinePanel;
    [Tooltip("Re-sync the server clock every N seconds (drift correction only — NOT per second).")]
    public float clockResyncInterval = 120f;

    private double serverOffsetMs;        // server_time - local_time, captured on sync
    private bool clockSynced;
    private float nextResyncTime;
    private long currentSlotNo = -1;
    private int sessionSeconds = 60;
    private int bettingSeconds = 50;
    private int numberCount = 10;
    private bool bidsSubmittedThisRound;
    private bool spunThisRound;
    private bool resultEnteredThisRound;
    private bool resultFetchWarned;   // one warning per round, not per retry tick

    private static double LocalUnixMs()
    {
        return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private IEnumerator ServerGameLoop()
    {
        while (APIManager.Instance == null || !APIManager.Instance.IsLoggedIn())
            yield return new WaitForSeconds(0.5f);

        RefreshServerBalance();
        LoadHistoryFromServer();

        while (true)
        {
            // No internet -> show popup and pause the game until it's back.
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                ShowOffline(true);
                clockSynced = false;
                yield return new WaitForSeconds(1f);
                continue;
            }

            // Sync the clock once, then only occasionally (drift correction).
            if (!clockSynced || Time.unscaledTime >= nextResyncTime)
                yield return SyncServerClock();

            if (!clockSynced)
            {
                ShowOffline(true); // couldn't reach server
                yield return new WaitForSeconds(1f);
                continue;
            }

            ShowOffline(false);
            TickLocal();

            // Local UI tick only — NO server call in this loop.
            yield return new WaitForSeconds(0.25f);
        }
    }

    private IEnumerator SyncServerClock()
    {
        bool done = false;
        bool ok = false;

        APIManager.Instance.GetCurrentRound((success, data) =>
        {
            done = true;
            if (success && data != null)
            {
                serverOffsetMs = (double)data.server_time - LocalUnixMs();
                if (data.session_seconds > 0) sessionSeconds = data.session_seconds;
                if (data.betting_seconds > 0) bettingSeconds = data.betting_seconds;
                if (data.numbers > 0) numberCount = data.numbers;
                if (data.payout_multiplier > 0) payoutMultiplier = data.payout_multiplier;
                ok = true;
            }
        });

        yield return new WaitUntil(() => done);

        clockSynced = ok;
        nextResyncTime = Time.unscaledTime + Mathf.Max(30f, clockResyncInterval);
    }

    private void TickLocal()
    {
        long serverSec = (long)((LocalUnixMs() + serverOffsetMs) / 1000.0);
        long slotNo = serverSec / sessionSeconds;
        int into = (int)(serverSec % sessionSeconds);
        bool betting = into < bettingSeconds;

        // New session?
        if (slotNo != currentSlotNo)
        {
            currentSlotNo = slotNo;
            bidsSubmittedThisRound = false;
            spunThisRound = false;
            resultEnteredThisRound = false;

            currentState = GameState.Betting;
            betManager.ResetForNewRound();
            uiManager.ResetRoundUI();
            uiManager.SetBettingInteractable(true);
        }

        if (betting)
        {
            currentState = GameState.Betting;
            int bettingLeft = bettingSeconds - into;
            timerController.UpdateTimerDisplay(bettingLeft);

            if (showBettingCloseWarning && bettingLeft <= warningStartSeconds && bettingLeft > 0)
            {
                AudioManager.Instance?.PlayCountdownTick("TimerTick", bettingLeft, warningStartSeconds);
                OnBettingTimeWarning?.Invoke(bettingLeft);
                uiManager.ShowBettingWarning(bettingLeft);
            }

            // Fallback auto-submit just before betting closes (server needs betting open).
            //
            // The board MUST be locked before submitting, exactly as the OK button
            // does. Without this the bets go to the server while the grid is still
            // live, so anything tapped in the remaining seconds is deducted from
            // the local wallet and never reaches the server — a ghost bet.
            if (!bidsSubmittedThisRound && bettingLeft <= 3 && betManager.GetGrandTotalBet() > 0)
            {
                uiManager.ConfirmBetsFromTimer();
                SubmitBidsToServer();
            }
        }
        else
        {
            timerController.UpdateTimerDisplay(0);

            if (!resultEnteredThisRound)
            {
                resultEnteredThisRound = true;
                EnterResultPhase();
            }
        }
    }

    private void EnterResultPhase()
    {
        currentState = GameState.BettingClosed;
        uiManager.ConfirmBetsFromTimer();
        uiManager.ShowBettingClosed();
        AudioManager.Instance?.PlayOneShot("BetClosed");
        // PRD Topic 7.4 Phase C: one robust heavy vibe as the board locks down.
        HapticManager.LongHeavy();
        OnBettingClosed?.Invoke();
        betManager.SaveCurrentAsLastRound();

        // One call per session: fetch the winning number + slot id, then spin.
        APIManager.Instance.GetCurrentRound((ok, data) =>
        {
            if (!ok || data == null)
            {
                // Warn once per round, not on every retry tick, or a slow
                // connection buries the screen in toasts.
                if (!resultFetchWarned)
                {
                    resultFetchWarned = true;
                    Toast.Warning(L.T("game.waiting_result"));
                }

                resultEnteredThisRound = false; // retry on the next tick
                return;
            }

            resultFetchWarned = false;

            if (spunThisRound) return;
            spunThisRound = true;

            int winner = data.winning_number;
            int slotId = data.slot_id;

            currentState = GameState.Spinning;
            uiManager.SetBettingInteractable(false);

            float spinDuration = data.spin_seconds > 0
                ? data.spin_seconds
                : Random.Range(spinManager.minDuration, spinManager.maxDuration);

            spinManager.StartSpin(winner, spinDuration, () =>
            {
                OnSpinComplete?.Invoke(winner);
                currentState = GameState.Payout;
                StartCoroutine(AfterSpinSequence(slotId, winner));
            });
        });
    }

    /// <summary>
    /// Runs the instant the wheel stops:
    ///   1) add the new winning number to the Last-10 strip (newest on the right),
    ///   2) wait 1 second (trust beat), then settle the money.
    /// </summary>
    private IEnumerator AfterSpinSequence(int slotId, int winner)
    {
        AddWinnerToHistory(winner);

        yield return new WaitForSeconds(1f);

        ShowServerResult(slotId, winner);
    }

    private void AddWinnerToHistory(int winner)
    {
        history.Insert(0, winner);                 // newest first in the list
        if (history.Count > 10) history.RemoveAt(history.Count - 1);
        uiManager.UpdateHistory(history);          // displayed newest-on-the-right
    }

    private void ShowOffline(bool show)
    {
        if (offlinePanel != null && offlinePanel.activeSelf != show)
            offlinePanel.SetActive(show);
    }

    /// <summary>Builds the bid map and submits it to the server for the active session.</summary>
    public void SubmitBidsToServer()
    {
        if (bidsSubmittedThisRound || APIManager.Instance == null) return;

        var bids = betManager.GetBidMap();
        if (bids == null || bids.Count == 0) return;

        bidsSubmittedThisRound = true; // guard against double-submit

        APIManager.Instance.PlaceBid(currentSlotNo, bids, (ok, msg, data) =>
        {
            if (ok)
            {
                RefreshServerBalance();
            }
            else
            {
                // The player's money is on the line here — never fail silently.
                // The server message is already player-readable ("Betting is
                // closed for this round.", "Insufficient balance.").
                Toast.Error(string.IsNullOrEmpty(msg)
                    ? "Your bet could not be placed. Please try again."
                    : msg);

                // The server never took the stake. Clear the board WITHOUT a
                // local refund (ClearTable would credit locally and then be
                // overwritten by the server value), and pull the authoritative
                // balance instead.
                betManager.ResetForNewRound();
                RefreshServerBalance();

                bidsSubmittedThisRound = false; // allow a retry while betting is open
            }
        });
    }

    private void ShowServerResult(int slotId, int fallbackWinner)
    {
        APIManager.Instance.GetRoundResult(slotId, (ok, data) =>
        {
            int winner = (ok && data != null) ? data.winning_number : fallbackWinner;

            long winAmount = 0;
            if (ok && data != null && decimal.TryParse(data.your_payout, out decimal payout))
                winAmount = (long)payout;

            if (winAmount > 0)
            {
                AudioManager.Instance?.PlayOneShot("Win");
                HapticManager.Win();
            }
            else if (betManager.GetGrandTotalBet() > 0)
            {
                AudioManager.Instance?.PlayOneShot("Lose");
                HapticManager.Lose();
            }

            // History was already updated at spin-stop (AddWinnerToHistory).
            uiManager.ShowResult(winner, winAmount);

            RefreshServerBalance();

            long bal = walletManager.GetBalance();
            NotificationManager.Instance?.AddNotification($"Winning Number: {winner} | Balance: ₹{bal:N0}");
        });
    }

    /// <summary>Seed the last-10 history strip from the server (once, on entry).</summary>
    private void LoadHistoryFromServer()
    {
        StartCoroutine(LoadHistoryRoutine());
    }

    /// <summary>
    /// Fetches the last-10 winning numbers, retrying a few times: the host
    /// sleeps when idle and the first call after a cold start can take longer
    /// than the request timeout.
    ///
    /// On failure the strip is CLEARED rather than left alone — otherwise the
    /// placeholder text sitting in the scene's Text objects stays on screen and
    /// looks like real results.
    /// </summary>
    private IEnumerator LoadHistoryRoutine()
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (APIManager.Instance == null) yield break;

            bool done = false, got = false;

            APIManager.Instance.GetRoundHistory((ok, list) =>
            {
                done = true;
                if (!ok || list == null) return;

                got = true;
                Debug.Log($"[Bingo] history from server: [{string.Join(",", list)}]");

                history.Clear();
                foreach (int n in list) history.Add(n);
                uiManager.UpdateHistory(history);
            });

            while (!done) yield return null;
            if (got) yield break;

            history.Clear();
            uiManager.UpdateHistory(history);

            Debug.LogWarning($"[Bingo] history fetch failed (attempt {attempt}/3)");

            // Only tell the player once every retry has failed — a single slow
            // response is not worth interrupting them for.
            if (attempt == 3)
                Toast.Warning(L.T("game.history_failed"));

            yield return new WaitForSeconds(3f);
        }
    }

    private void RefreshServerBalance()
    {
        if (APIManager.Instance == null || walletManager == null) return;
        APIManager.Instance.GetWalletBalance((ok, balance) =>
        {
            if (ok) walletManager.SetBalanceFromServer(balance);
        });
    }
}
