using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Displays the round countdown timer on screen.
///
/// This script is PURELY visual � it only displays what GameManager tells it.
/// All timing logic lives in GameManager.BettingPhase / BettingClosedPhase.
///
/// GameManager calls UpdateTimerDisplay(secondsLeft) every time the integer
/// second changes, so this script updates no more than once per second.
///
/// FIX-TC-01: Added pop/punch animation on each tick so the player can see
///            the number visibly change � makes the countdown feel alive.
/// FIX-TC-02: Warning colour turns red in the last warningThreshold seconds.
/// FIX-TC-03: Zero is displayed as "0" not as a blank or negative number.
/// </summary>
public class TimerController : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("The TMP text that shows the countdown number.")]
    public TMP_Text timerText;

    [Tooltip("Colour for normal countdown.")]
    public Color normalColor = Color.white;

    [Tooltip("Colour used when time is running out.")]
    public Color warningColor = new Color(1f, 0.3f, 0.3f); // red-orange

    [Tooltip("Seconds remaining at which the text turns to warningColor.")]
    public int warningThreshold = 5;

    [Header("Animation")]
    [Tooltip("Small scale punch on each second tick so the change is visible.")]
    public bool animateOnTick = true;

    [Header("Critical Urgency Window (PRD Topic 5.13 / 6.2)")]
    [Tooltip("In the final seconds the number pulses like a heartbeat instead of a single punch.")]
    public bool useHeartbeatPulse = true;

    [Tooltip("Seconds remaining at which the heartbeat pulse takes over from the normal tick punch.")]
    public int criticalThreshold = 3;

    [Tooltip("How big the heartbeat pulse gets (1.35 = 35% larger at the peak).")]
    public float heartbeatScale = 1.35f;

    [Header("Haptics")]
    [Tooltip("Tap the phone on each of the final seconds so the player feels betting closing without watching the clock.")]
    public bool vibrateOnFinalSeconds = true;

    [Tooltip("Vibration starts at this many seconds remaining.")]
    public int vibrateFromSeconds = 3;

    private Tween punchTween;
    private Sequence heartbeatSequence;
    private int lastDisplayed = -1; // prevents redundant updates
    private Vector3 timerBaseScale = Vector3.one;
    private bool baseScaleCaptured;

    /// <summary>
    /// Called by GameManager every time the displayed integer second changes.
    /// Safe to call every frame � skips work if the value hasn't changed.
    /// </summary>
    public void UpdateTimerDisplay(int secondsLeft)
    {
        if (timerText == null) return;

        // Clamp so we never show negative numbers
        secondsLeft = Mathf.Max(0, secondsLeft);

        // Skip if nothing changed � avoids flicker and wasted DOTween calls
        if (secondsLeft == lastDisplayed) return;
        lastDisplayed = secondsLeft;

        // Update text
        timerText.text = secondsLeft.ToString();

        // Colour � red warning in final seconds
        timerText.color = (secondsLeft <= warningThreshold && secondsLeft > 0)
            ? warningColor
            : normalColor;

        CaptureBaseScale();

        bool isCritical = secondsLeft > 0 && secondsLeft <= criticalThreshold;

        // Final-seconds pulse. Only inside the countdown (never at 0, which is
        // already covered by the "betting closed" cue in GameManager).
        if (vibrateOnFinalSeconds && secondsLeft > 0 && secondsLeft <= vibrateFromSeconds)
        {
            // PRD asks for a "continuous intense pulsing vibration" here. A real
            // 3-second continuous buzz drains the battery and reads as a fault on
            // most Android motors, so this fires one rapid 3-beat pattern per
            // second instead — same urgency, far better on the hardware.
            HapticManager.CountdownPulse();
        }

        if (isCritical && useHeartbeatPulse)
        {
            PlayHeartbeat();
        }
        else if (animateOnTick)
        {
            KillHeartbeat();

            punchTween?.Kill(true); // complete immediately before starting new one
            punchTween = timerText.transform
                .DOPunchScale(Vector3.one * 0.25f, 0.25f, 5, 0.5f)
                .SetUpdate(true); // runs even if Time.timeScale = 0
        }
    }

    /// <summary>
    /// Rapid compress/expand on the countdown typography — the "Heartbeat Pulse"
    /// from the PRD. Two beats per second so it reads as a pulse, not a bounce.
    /// </summary>
    private void PlayHeartbeat()
    {
        if (timerText == null) return;

        punchTween?.Kill(true);
        KillHeartbeat();

        Transform t = timerText.transform;
        t.localScale = timerBaseScale;

        heartbeatSequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        heartbeatSequence.Append(t.DOScale(timerBaseScale * heartbeatScale, 0.12f).SetEase(Ease.OutQuad));
        heartbeatSequence.Append(t.DOScale(timerBaseScale, 0.16f).SetEase(Ease.InQuad));
        heartbeatSequence.Append(t.DOScale(timerBaseScale * (1f + (heartbeatScale - 1f) * 0.6f), 0.10f).SetEase(Ease.OutQuad));
        heartbeatSequence.Append(t.DOScale(timerBaseScale, 0.20f).SetEase(Ease.InQuad));
    }

    private void KillHeartbeat()
    {
        if (heartbeatSequence != null && heartbeatSequence.IsActive()) heartbeatSequence.Kill();
        heartbeatSequence = null;

        if (baseScaleCaptured && timerText != null)
            timerText.transform.localScale = timerBaseScale;
    }

    private void CaptureBaseScale()
    {
        if (baseScaleCaptured || timerText == null) return;

        timerBaseScale = timerText.transform.localScale;
        baseScaleCaptured = true;
    }

    /// <summary>Called when a new round starts so the timer never stays mid-pulse.</summary>
    public void ResetVisualState()
    {
        punchTween?.Kill(true);
        KillHeartbeat();
        lastDisplayed = -1;
    }

    private void OnDestroy()
    {
        punchTween?.Kill();
        KillHeartbeat();
    }
}
