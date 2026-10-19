using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BetManager : MonoBehaviour
{
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

        if (GameManager.Instance.walletManager.TryDeduct(currentChipValue))
        {
            currentBetHistory.Add(new BetInfo { number = number, value = currentChipValue });

            if (!betTotals.ContainsKey(number)) betTotals[number] = 0;
            betTotals[number] += currentChipValue;

            PlaySound(string.IsNullOrWhiteSpace(numberBetSoundKey) ? "ChipSelect" : numberBetSoundKey);
            HapticManager.Medium();   // real money left the wallet — make it felt

            OnBetPlaced?.Invoke(number);
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
            PlaySound("ChipPlace");
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
