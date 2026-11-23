using RealBingo.Localization;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BetManager : MonoBehaviour
{
    /// <summary>
    /// Bet target for the Joker. The numbers are 0-9, so 10 is the first free
    /// index: this keeps every bet map, UI array and highlight path int-keyed
    /// instead of pushing a string key through the whole game for one target.
    ///
    /// It is only ever a number internally. On the wire it becomes "j" (see
    /// WireKey) and on screen it reads "J" (see Label).
    /// </summary>
    public const int JokerNumber = 10;

    public static bool IsJoker(int number) => number == JokerNumber;

    /// <summary>What the player sees for a bet target: "0".."9", or "J".</summary>
    public static string Label(int number) => IsJoker(number) ? "J" : number.ToString();

    /// <summary>
    /// The key this target uses in the place-bid payload. The backend expects
    /// the Joker as the string "j" alongside the numeric keys:
    ///     [{"j":100,"0":50,"1":20}]
    /// </summary>
    public static string WireKey(int number) => IsJoker(number) ? "j" : number.ToString();

    [SerializeField] private List<BetInfo> currentBetHistory = new List<BetInfo>();
    [SerializeField] private string numberBetSoundKey = "UIButtonClick";

    private Dictionary<int, long> betTotals = new Dictionary<int, long>();

    private List<BetInfo> lastRoundBets = new List<BetInfo>();
    public long currentChipValue;

    public event System.Action<int> OnBetPlaced;
    public event System.Action OnTotalBetChanged;

    public bool PlaceBet(int number)
    {
        if (currentChipValue <= 0) return false;
        if (!GameManager.Instance.CanBet()) return false;

        // Every chip goes on every target, J included: the 5000 chip is a regular
        // chip, not a Joker-only one. There used to be an exclusive-pair rule here
        // (5000 only on J, J only taking 5000) which made the 5000 chip unplaceable
        // anywhere else and J unreachable with any other stake.
        //
        // The Joker is still special where it actually matters: it pays at its own
        // rate (GameManager.jokerPayoutMultiplier) and goes to the backend as "j"
        // (see WireKey). What it is NOT is restricted in what you may stake on it.

        if (GameManager.Instance.walletManager.TryDeduct(currentChipValue))
        {
            currentBetHistory.Add(new BetInfo { number = number, value = currentChipValue });

            if (!betTotals.ContainsKey(number)) betTotals[number] = 0;
            betTotals[number] += currentChipValue;

            PlaySound(string.IsNullOrWhiteSpace(numberBetSoundKey) ? "ChipSelect" : numberBetSoundKey);
            HapticManager.Medium();   // real money left the wallet — make it felt

            OnBetPlaced?.Invoke(number);

            // The slot's own listener has just written the stake text. Now throw
            // the chip at it and punch the number on impact, so the chip reads as
            // becoming the text instead of stacking on the board (PRD Topic 6.3:
            // nothing stays rendered after the hit).
            UIManager.Instance?.PlayChipFlight(number);

            OnTotalBetChanged?.Invoke();
            return true;
        }

        // Rejected for insufficient balance. Sound + haptic + a visual pull to
        // the wallet, so the reason is obvious (PRD Topic 7.9).
        PlaySound("Error");
        HapticManager.Failure();
        UIManager.Instance?.ShowInsufficientBalance();
        return false;
    }

    public void UndoLastBet()
    {
        if (currentBetHistory.Count > 0 && GameManager.Instance.CanBet())
        {
            var last = currentBetHistory[currentBetHistory.Count - 1];
            currentBetHistory.RemoveAt(currentBetHistory.Count - 1);

            if (betTotals.ContainsKey(last.number))
            {
                betTotals[last.number] -= last.value;
                if (betTotals[last.number] <= 0) betTotals.Remove(last.number);
            }

            GameManager.Instance.walletManager.AddAmount(last.value);
            HapticManager.Light();

            // Smoke dissolve on just this slot, in reverse order (PRD Topic 6.3).
            UIManager.Instance?.PlaySlotClearEffect(last.number);

            OnBetPlaced?.Invoke(last.number);
            OnTotalBetChanged?.Invoke();
        }
    }

    public void ClearTable()
    {
        if (currentBetHistory.Count == 0) return;

        long totalRefund = 0;
        foreach (var bet in currentBetHistory) totalRefund += bet.value;

        GameManager.Instance.walletManager.AddAmount(totalRefund);
        HapticManager.Medium();   // the whole table was just cleared — confirm it

        // All boxes wipe simultaneously in a burst of dust (PRD Topic 6.3).
        UIManager.Instance?.PlayBoardClearEffect();

        ResetData();
        OnTotalBetChanged?.Invoke();
    }

    public void ResetForNewRound()
    {
        currentChipValue = 0;
        ResetData();
        OnTotalBetChanged?.Invoke();
    }

    private void ResetData()
    {
        List<int> affected = new List<int>(betTotals.Keys);

        currentBetHistory.Clear();
        betTotals.Clear();

        foreach (int n in affected) OnBetPlaced?.Invoke(n);
    }

    public void RepeatLastBets()
    {
        if (lastRoundBets.Count == 0 || !GameManager.Instance.CanBet()) return;
        ClearTable();
        bool atLeastOnePlaced = false;

        foreach (var bet in lastRoundBets)
        {
            if (GameManager.Instance.walletManager.TryDeduct(bet.value))
            {
                currentBetHistory.Add(new BetInfo { number = bet.number, value = bet.value });

                if (!betTotals.ContainsKey(bet.number)) betTotals[bet.number] = 0;
                betTotals[bet.number] += bet.value;

                OnBetPlaced?.Invoke(bet.number);
                atLeastOnePlaced = true;
            }
        }

        if (atLeastOnePlaced)
        {
            PlaySound("PlaceBet");
            HapticManager.Heavy();    // a whole board of bets re-placed at once
            OnTotalBetChanged?.Invoke();
        }
        else
        {
            PlaySound("Error");
            HapticManager.Failure();
        }
    }

    public void SaveCurrentAsLastRound()
    {
        lastRoundBets.Clear();

        foreach (var bet in currentBetHistory)
        {
            lastRoundBets.Add(new BetInfo { number = bet.number, value = bet.value });
        }
    }

    public long GetTotalBetOnNumber(int number)
    {
        return betTotals.ContainsKey(number) ? betTotals[number] : 0;
    }

    /// <summary>
    /// Snapshot of this round's bets as number -> amount, for submitting to the
    /// server via /place-bid.
    /// </summary>
    public Dictionary<int, long> GetBidMap()
    {
        Dictionary<int, long> map = new Dictionary<int, long>();
        foreach (var kvp in betTotals)
        {
            if (kvp.Value > 0) map[kvp.Key] = kvp.Value;
        }
        return map;
    }

    public long GetGrandTotalBet()
    {
        long total = 0;
        foreach (var kvp in betTotals) total += kvp.Value;
        return total;
    }

    private void PlaySound(string name)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayOneShot(name);
    }
}

[System.Serializable]
public class BetInfo
{
    public int number;
    public long value;
}
