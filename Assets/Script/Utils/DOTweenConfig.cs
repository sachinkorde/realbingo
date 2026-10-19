using DG.Tweening;
using UnityEngine;

/// <summary>
/// One place that configures DOTween for this project, applied before the first
/// scene loads so it is in effect before any tween is created.
///
/// WHAT THIS FIXES
/// ---------------
/// DOTween pre-allocates arrays for the maximum number of tweens that can run
/// at once (default 200 tweeners / 50 sequences). When that ceiling is passed it
/// reallocates the arrays mid-game, which shows up as a one-off frame hitch —
/// usually at the worst moment, because the peak happens exactly when the wheel
/// stops and the payout, particles, odometer and result panel all animate at
/// once. Setting the capacity up front means the pool never has to grow.
///
/// THE BIG REMAINING OPTIMISATION: TWEEN RECYCLING
/// -----------------------------------------------
/// DOTweenSettings currently has "defaultRecyclable = 0", so every tween the
/// game creates is a fresh allocation that becomes garbage when it completes.
/// With ~170 buttons, a per-second timer punch, chip select/deselect, wallet
/// counters and warning flashes, that is a steady stream of allocations.
///
/// Turning recycling ON removes almost all of it — but it is NOT a safe
/// flip-the-switch change in this codebase yet, and the reason matters:
///
///   With recycling on, a completed tween goes back into a pool and is handed
///   out again as a DIFFERENT tween. Any code still holding the old reference
///   and calling .Kill() on it will then kill somebody else's animation. The
///   symptom is a chip or panel occasionally freezing mid-animation — rare,
///   random, and very hard to reproduce.
///
/// This project stores 17 tween references in fields (ChipButton, SpinManager,
/// TimerController, UIManager, ButtonFeedback, FAQAccordion, UIPanelTweener,
/// AudioManager) and kills them by reference. Those all need converting to
/// target-based kills — target.DOKill() instead of storedTween.Kill() — before
/// recycling can be enabled safely. That is a mechanical change with no effect
/// on behaviour, but it should be done and tested as its own pass.
///
/// When that is done: set EnableRecycling below to true AND tick
/// "Recycle All Tweens" in the DOTween Utility Panel.
/// </summary>
public static class DOTweenConfig
{
    /// <summary>
    /// Max tweens that can run simultaneously before DOTween has to reallocate.
    /// Peak in this game is the wheel-stop moment (payout + particles + counters
    /// + button feedback), which is well under this.
    /// </summary>
    private const int TweenersCapacity = 500;
    private const int SequencesCapacity = 200;

    /// <summary>
    /// Leave FALSE until the stored-tween-reference audit described above is
    /// done. See the class summary for why.
    /// </summary>
    private const bool EnableRecycling = false;

    private static bool applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        if (applied) return;
        applied = true;

        // Ensures DOTween exists before anything else touches it, and stops the
        // capacity arrays from being resized mid-gameplay.
        DOTween.Init(recycleAllByDefault: EnableRecycling, useSafeMode: true, logBehaviour: LogBehaviour.ErrorsOnly)
               .SetCapacity(TweenersCapacity, SequencesCapacity);
    }

    /// <summary>
    /// Current DOTween state, for the Tools > Real Bingo > DOTween report.
    /// </summary>
    public static string Describe()
    {
        return
            $"DOTween configured:\n" +
            $"  capacity          = {TweenersCapacity} tweeners / {SequencesCapacity} sequences\n" +
            $"  recycling         = {(EnableRecycling ? "ON" : "OFF (see DOTweenConfig.cs for how to enable safely)")}\n" +
            $"  safe mode         = ON (recommended: a money game must never break on a null target)\n" +
            $"  active tweens now = {DOTween.TotalPlayingTweens()}";
    }
}
