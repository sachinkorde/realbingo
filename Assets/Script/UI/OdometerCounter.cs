using System.Text;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Mechanical tumbler / suitcase-padlock style number display (PRD Topic 5.2 and 6.6).
///
/// The PRD asks for digits that physically ROLL up or down into place rather
/// than the number simply changing. This does that on a single TMP_Text, with no
/// extra GameObjects, prefabs or sprite strips to set up.
///
/// HOW IT WORKS
/// ------------
/// Each digit column is animated independently: a digit travelling from 3 to 7
/// passes through 4, 5 and 6, and a digit that is not changing does not move at
/// all. Columns further right settle first, exactly like a real odometer, so the
/// number visibly "locks in" from right to left.
///
/// Because it is one text field, there is no per-digit GameObject cost — this
/// stays cheap on low-end Android, which matters when it fires on every win,
/// loss, deposit and withdrawal.
///
/// USAGE — completely opt-in
/// -------------------------
///   1. Add this component to the wallet/score TMP_Text object.
///   2. Nothing else. UIManager keeps working exactly as before on any screen
///      where this component is absent.
///
/// If you want UIManager to drive it, call SetValue() instead of assigning
/// .text directly. Left alone, this component does nothing.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[DisallowMultipleComponent]
public class OdometerCounter : MonoBehaviour
{
    [Header("Roll Behaviour")]
    [Tooltip("Seconds for the whole roll to finish.")]
    public float rollDuration = 1.2f;

    [Tooltip("Extra delay per digit column, right to left. Makes the number lock in like a real odometer.")]
    [Range(0f, 0.25f)] public float columnStagger = 0.06f;

    public Ease rollEase = Ease.OutCubic;

    [Header("Formatting")]
    [Tooltip("Insert thousands separators (78,620).")]
    public bool useThousandsSeparator = true;

    [Tooltip("Pad with leading zeros to this many digits — the PRD's '5 to 7 digit structure'. 0 = no padding.")]
    [Range(0, 9)] public int minimumDigits = 0;

    [Header("Audio & Haptics")]
    [Tooltip("AudioManager key for the tumbler tick. Leave empty for silence.")]
    public string tickSoundKey = "";

    [Tooltip("Micro haptic tap as the digits roll (PRD Topic 7.8). Fires at most a few times, not per digit change.")]
    public bool tickHaptics = false;

    [Range(0.05f, 0.5f)] public float minTickInterval = 0.12f;

    private TMP_Text label;
    private Tween rollTween;
    private long currentValue;
    private long targetValue;
    private float lastTickTime = -999f;
    private readonly StringBuilder builder = new StringBuilder(16);

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    private void OnDestroy()
    {
        rollTween?.Kill();
    }

    /// <summary>The value currently shown (mid-roll this is the in-between value).</summary>
    public long DisplayedValue => currentValue;

    /// <summary>Jump straight to a value with no animation — use for the first load.</summary>
    public void SetValueImmediate(long value)
    {
        rollTween?.Kill();
        currentValue = targetValue = value;
        Render(value);
    }

    /// <summary>
    /// Roll to a new value. Calling this again mid-roll retargets smoothly from
    /// wherever the digits currently are, instead of snapping.
    /// </summary>
    public void SetValue(long value)
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (label == null) return;

        if (value == targetValue && rollTween != null && rollTween.IsActive()) return;

        targetValue = value;

        rollTween?.Kill();

        long from = currentValue;
        long to = value;

        if (from == to)
        {
            Render(to);
            return;
        }

        // Duration scales a little with the size of the jump so a +10 win does
        // not take as long as a +50,000 jackpot.
        float magnitude = Mathf.Abs(to - from);
        float duration = Mathf.Clamp(rollDuration * Mathf.Sqrt(magnitude / 1000f), 0.35f, rollDuration * 2f);

        rollTween = DOVirtual.Float(0f, 1f, duration, progress =>
        {
            RenderRolling(from, to, progress);
        })
        .SetEase(rollEase)
        .SetLink(gameObject)
        .OnComplete(() =>
        {
            currentValue = to;
            Render(to);
        });
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the intermediate string. Each column gets its own progress value,
    /// offset by columnStagger, so the rightmost digits settle first.
    /// </summary>
    private void RenderRolling(long from, long to, float progress)
    {
        string fromDigits = Format(from, false);
        string toDigits = Format(to, false);

        int width = Mathf.Max(fromDigits.Length, toDigits.Length);
        fromDigits = fromDigits.PadLeft(width, '0');
        toDigits = toDigits.PadLeft(width, '0');

        builder.Clear();

        for (int column = 0; column < width; column++)
        {
            // column 0 is the leftmost (most significant) -> settles last.
            int fromRight = width - 1 - column;
            float delay = columnStagger * fromRight;
            float span = Mathf.Max(0.01f, 1f - delay);
            float columnProgress = Mathf.Clamp01((progress - delay) / span);

            int fromDigit = fromDigits[column] - '0';
            int toDigit = toDigits[column] - '0';

            int shown = fromDigit == toDigit
                ? toDigit                                   // not changing -> never move
                : Mathf.RoundToInt(Mathf.Lerp(fromDigit, toDigit, columnProgress));

            builder.Append((char)('0' + Mathf.Clamp(shown, 0, 9)));
        }

        long intermediate;
        if (!long.TryParse(builder.ToString(), out intermediate)) intermediate = to;

        currentValue = intermediate;
        Render(intermediate);

        PlayTick();
    }

    private void Render(long value)
    {
        if (label == null) return;
        label.text = Format(value, useThousandsSeparator);
    }

    private string Format(long value, bool separators)
    {
        string text = separators ? value.ToString("N0") : Mathf.Abs(value).ToString();

        if (minimumDigits > 0 && !separators)
            text = text.PadLeft(minimumDigits, '0');

        return text;
    }

    private void PlayTick()
    {
        if (Time.unscaledTime - lastTickTime < minTickInterval) return;
        lastTickTime = Time.unscaledTime;

        if (!string.IsNullOrEmpty(tickSoundKey))
            AudioManager.Instance?.PlayOneShot(tickSoundKey);

        if (tickHaptics) HapticManager.Selection();
    }
}
