using DG.Tweening;
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[System.Serializable]
public class NumberAngle
{
    public string name;
    public float startAngle;
    public float endAngle;
}

public class SpinManager : MonoBehaviour
{
    private const string DefaultSpinLoopSoundKey = "SpinLoop";

    [Header("Core References")]
    public Transform wheelTransform;
    //public Transform needleVisual;

    [Header("Spin Settings")]
    public float minDuration = 6f;
    public float maxDuration = 8f;
    public int minRounds = 8;
    public int maxRounds = 12;
    public float pointerOffsetAngle = 0f;

    [Header("Realistic Spin Physics")]
    [Tooltip("Graph: X-axis is Time, Y-axis is Rotation. Curve it for realistic friction!")]
    public AnimationCurve spinCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Extra smoothing after the AnimationCurve. Higher values make the spin slower at start and end.")]
    [Range(0f, 1f)]
    public float startEndSlowdown = 0.65f;

    [Header("Wedge Definitions")]
    public List<NumberAngle> angleSettings;

    [Header("Audio")]
    [Tooltip("Assign the wheel spin clip here. This keeps spin audio controlled directly from SpinManager.")]
    public AudioClip spinAudioClip;
    public AudioSource spinAudioSource;
    public string spinTickSoundKey = "TimerTick";
    [Tooltip("Continuous spin audio key registered in AudioManager. This sound follows wheel speed and does not overlap.")]
    public string spinLoopSoundKey = "SpinLoop";
    [Tooltip("Use a dedicated loop for the main spin sound instead of stacking tick one-shots.")]
    public bool useSpeedBasedSpinAudio = true;
    [Range(0.1f, 2f)]
    public float minSpinAudioPitch = 0.65f;
    [Range(0.1f, 2f)]
    public float maxSpinAudioPitch = 1.35f;
    [Range(0f, 1f)]
    public float minSpinAudioVolume = 0.1f;
    [Range(0f, 1f)]
    public float maxSpinAudioVolume = 1f;
public float spinAudioFadeOutDuration = 0.45f;
    [Tooltip("Optional peg click audio. Keep off when the loop is used, to avoid overlapping tick sounds.")]
    public bool playPegTickAudio = false;
    [Range(0.04f, 0.25f)]
    public float minPegTickInterval = 0.09f;

    [Header("Perspective Zoom (PRD Topic 6.4)")]
    [Tooltip("Wheel scales forward during the spin to grab focus, then drops back on stop.")]
    public bool usePerspectiveZoom = true;
    [Tooltip("Object to scale. Leave empty to scale the wheel itself.")]
    public Transform zoomTarget;
    [Tooltip("Zoom factor at full spin. PRD default is 1.2x.")]
    [Range(1f, 2f)] public float perspectiveZoom = 1.2f;
    public float zoomInDuration = 0.6f;
    public float zoomOutDuration = 0.5f;

    [Header("Winner Pocket Blink (PRD Topic 6.4)")]
    [Tooltip("Optional highlight object per wedge, in the SAME order as Angle Settings. Leave empty to skip the blink.")]
    public List<GameObject> wedgeHighlights = new List<GameObject>();
    [Tooltip("How long the winning pocket keeps flashing after the wheel stops.")]
    public float winnerBlinkDuration = 1.6f;
    [Range(1, 12)] public int winnerBlinkCount = 5;

    [Header("Music Ducking (PRD Topic 7.3)")]
    [Tooltip("Drop the background music while the wheel spins. UIManager restores it after the result.")]
    public bool duckMusicDuringSpin = true;
    [Range(0f, 1f)]
    [Tooltip("0.5 = the PRD's 'attenuate by exactly 50%'.")]
    public float musicDuckLevel = 0.5f;

    [Header("Haptics")]
    [Tooltip("Buzz on every peg the wheel passes. Off by default — a 6–8 second continuous rumble drains the battery and gets annoying fast. The landing thump is always played.")]
    public bool playPegTickHaptic = false;
    [Tooltip("Minimum seconds between peg buzzes when peg haptics are on.")]
    [Range(0.06f, 0.4f)]
    public float minPegHapticInterval = 0.12f;

    private float lastPegHapticTime = -999f;
    private Tween zoomTween;
    private Sequence winnerBlinkSequence;
    private Vector3 zoomBaseScale = Vector3.one;
    private bool zoomBaseCaptured;
    private int lastHighlightedWedge = -1;

    private Tween spinTween;
    private Tween spinAudioFadeTween;
    private bool isSpinning;

    // Tracking for programmatic audio ticks
    private float degreesPerPeg;
    private float totalDegreesTraveled;
    private float lastTickThreshold;
    private float previousZ;
    private float lastPegTickTime;
    private bool usingLocalSpinAudio;

    // Zero-allocation needle animation (Replaces DOPunchRotation)
    private float currentNeedleAngle = 0f;

    private void Start()
    {
        Application.targetFrameRate = 60;
        EnsureSpinAudioDefaults();
        EnsureLocalSpinAudioSource();

        if (angleSettings != null && angleSettings.Count > 0)
        {
            degreesPerPeg = 360f / angleSettings.Count;
        }
    }

    /*private void Update()
    {
        // THIS REPLACES DOPUNCHROTATION: 
        // It smoothly slides the needle back to 0 without flooding memory.
        if (needleVisual != null && currentNeedleAngle > 0.1f)
        {
            currentNeedleAngle = Mathf.Lerp(currentNeedleAngle, 0f, Time.deltaTime * 15f);
            needleVisual.localEulerAngles = new Vector3(0, 0, currentNeedleAngle);
        }
    }*/

    public void StartSpin(int targetIndex, float duration, Action onComplete)
    {
        Application.targetFrameRate = 60;

        if (wheelTransform == null || angleSettings == null || angleSettings.Count == 0 || isSpinning) return;

        isSpinning = true;
        spinTween?.Kill();

        targetIndex = Mathf.Clamp(targetIndex, 0, angleSettings.Count - 1);
        NumberAngle targetWedge = angleSettings[targetIndex];
        float landingAngle = Random.Range(targetWedge.startAngle + 2f, targetWedge.endAngle - 2f);

        float currentZ = wheelTransform.localEulerAngles.z;
        currentZ = (currentZ % 360f + 360f) % 360f;

        float targetZ = pointerOffsetAngle + landingAngle;
        float normalizedTargetZ = (targetZ % 360f + 360f) % 360f;

        float distanceToTarget = currentZ - normalizedTargetZ;
        if (distanceToTarget < 0) distanceToTarget += 360f;

        int rounds = Random.Range(minRounds, maxRounds + 1);
        float finalAbsoluteZTarget = currentZ - distanceToTarget - (rounds * 360f);
        float finalDuration = duration > 0f ? duration : Random.Range(minDuration, maxDuration);

        totalDegreesTraveled = 0f;
        lastTickThreshold = 0f;
        previousZ = currentZ;
        lastPegTickTime = -999f;
        lastPegHapticTime = -999f;

        float totalSpinDegrees = Mathf.Abs(finalAbsoluteZTarget - currentZ);
        float expectedMaxAngularSpeed = Mathf.Max(1f, totalSpinDegrees / Mathf.Max(finalDuration, 0.01f) * 1.8f);

        Debug.Log($"Final Duration: {finalDuration:F2}s, Rounds: {rounds}, Target Angle: {landingAngle:F2} deg, Final Z Target: {finalAbsoluteZTarget:F2} deg");

        HideWinnerHighlight();
        StartPerspectiveZoom();
        StartSpinAudio();

        if (duckMusicDuringSpin)
            AudioManager.Instance?.DuckMusic(musicDuckLevel, 0.35f);

        // FIX: We tween a raw percentage 't' from 0.0 to 1.0 over the finalDuration.
        // Then we manually ask your AnimationCurve what the rotation should be!
        spinTween = DOVirtual.Float(0f, 1f, finalDuration, (t) =>
        {
            // 1. Read the curve's value and soften the start/end movement.
            float curveProgress = EvaluateSpinProgress(t);

            // 2. Calculate the exact rotation degree based on the curve's progress
            float currentVal = Mathf.LerpUnclamped(currentZ, finalAbsoluteZTarget, curveProgress);

            wheelTransform.localEulerAngles = new Vector3(0, 0, currentVal);

            float deltaZ = Mathf.Abs(currentVal - previousZ);
            totalDegreesTraveled += deltaZ;
            previousZ = currentVal;

            float angularSpeed = deltaZ / Mathf.Max(Time.deltaTime, 0.0001f);
            float speed01 = Mathf.Clamp01(angularSpeed / expectedMaxAngularSpeed);
            UpdateSpinAudio(speed01);

            if (totalDegreesTraveled >= lastTickThreshold + degreesPerPeg)
            {
                lastTickThreshold += degreesPerPeg;
                PlayTickSoundAndPopNeedle(speed01);
            }
        })
        .SetEase(Ease.Linear) // IMPORTANT: Set DOTween to Linear, because our Curve is doing all the easing work now!
        .SetUpdate(UpdateType.Fixed)
        .OnComplete(() =>
        {
            isSpinning = false;
            StopSpinAudio();
            EndPerspectiveZoom();
            BlinkWinnerPocket(targetIndex);

            // The needle has snapped into a pocket. PRD Topic 7.5 asks for a
            // "Double Shot Sharp Haptic Tap" on stop impact — it lands ~1s
            // before the win/lose result.
            HapticManager.DoubleTap();

            onComplete?.Invoke();
        });
    }

    private float EvaluateSpinProgress(float t)
    {
        t = Mathf.Clamp01(t);

        float curveProgress = spinCurve != null && spinCurve.length > 0
            ? spinCurve.Evaluate(t)
            : t;

        curveProgress = Mathf.Clamp01(curveProgress);
        float extraSmoothProgress = SmootherStep(curveProgress);

        return Mathf.Lerp(curveProgress, extraSmoothProgress, startEndSlowdown);
    }

    private float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (6f * t - 15f) + 10f);
    }

    private void StartSpinAudio()
    {
        EnsureSpinAudioDefaults();
        if (!useSpeedBasedSpinAudio) return;

        AudioClip clip = ResolveSpinAudioClip();
        if (clip != null)
        {
            AudioSource source = EnsureLocalSpinAudioSource();
            if (source == null) return;

            spinAudioFadeTween?.Kill();
            usingLocalSpinAudio = true;
            clip.LoadAudioData();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 64;
            source.ignoreListenerPause = true;
            source.mute = false;
            source.pitch = minSpinAudioPitch;
            source.volume = minSpinAudioVolume;
            source.Play();
            Debug.Log($"Spin audio started from SpinManager: {clip.name}");
            return;
        }

        usingLocalSpinAudio = false;
        if (AudioManager.Instance == null || string.IsNullOrEmpty(spinLoopSoundKey)) return;

        AudioManager.Instance.Play(spinLoopSoundKey);
        AudioManager.Instance.SetSpinPitch(minSpinAudioPitch);
        AudioManager.Instance.SetSpinVolume(minSpinAudioVolume);
    }

    private void UpdateSpinAudio(float speed01)
    {
        EnsureSpinAudioDefaults();
        if (!useSpeedBasedSpinAudio) return;

        float smoothSpeed = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(speed01));
        //float stopFade = 1f - Mathf.SmoothStep(0.9f, 1f, normalizedTime);
        //float volumeEnvelope = Mathf.Clamp01(stopFade);

        float pitch = Mathf.Lerp(minSpinAudioPitch, maxSpinAudioPitch, smoothSpeed);
        float volume = Mathf.Lerp(minSpinAudioVolume, maxSpinAudioVolume, smoothSpeed);

        if (usingLocalSpinAudio && spinAudioSource != null)
        {
            spinAudioSource.pitch = pitch;
            spinAudioSource.volume = volume;
            return;
        }

        if (AudioManager.Instance == null) return;

        AudioManager.Instance.SetSpinPitch(pitch);
        AudioManager.Instance.SetSpinVolume(volume);
    }

    private void StopSpinAudio()
    {
        EnsureSpinAudioDefaults();
        if (!useSpeedBasedSpinAudio) return;

        if (usingLocalSpinAudio && spinAudioSource != null)
        {
            spinAudioFadeTween?.Kill();

            if (!spinAudioSource.isPlaying)
            {
                spinAudioSource.volume = 0f;
                return;
            }

            spinAudioFadeTween = spinAudioSource
                .DOFade(0f, spinAudioFadeOutDuration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => spinAudioSource.Stop());
            return;
        }

        if (AudioManager.Instance == null) return;

        AudioManager.Instance.FadeOutSpin(spinAudioFadeOutDuration);
    }

    private void EnsureSpinAudioDefaults()
    {
        if (string.IsNullOrWhiteSpace(spinLoopSoundKey))
        {
            spinLoopSoundKey = DefaultSpinLoopSoundKey;
        }

        useSpeedBasedSpinAudio = true;

        if (minSpinAudioPitch <= 0f) minSpinAudioPitch = 0.65f;
        if (maxSpinAudioPitch <= 0f) maxSpinAudioPitch = 1.35f;
        if (minSpinAudioVolume < 0.05f) minSpinAudioVolume = 0.1f;
        if (maxSpinAudioVolume < 0.9f) maxSpinAudioVolume = 1f;
        if (spinAudioFadeOutDuration <= 0f) spinAudioFadeOutDuration = 0.45f;
    }

    private AudioClip ResolveSpinAudioClip()
    {
        if (spinAudioClip != null) return spinAudioClip;

        if (AudioManager.Instance != null)
        {
            spinAudioClip = AudioManager.Instance.GetClip(spinLoopSoundKey);
            if (spinAudioClip == null)
            {
                spinAudioClip = AudioManager.Instance.GetClip(DefaultSpinLoopSoundKey);
            }
        }

        if (spinAudioClip == null)
        {
            Debug.LogWarning("SpinManager has no spin audio clip. Assign Spin Audio Clip on SpinManager.");
        }

        return spinAudioClip;
    }

    private AudioSource EnsureLocalSpinAudioSource()
    {
        if (spinAudioSource != null) return spinAudioSource;

        spinAudioSource = GetComponent<AudioSource>();
        if (spinAudioSource == null)
        {
            spinAudioSource = gameObject.AddComponent<AudioSource>();
        }

        spinAudioSource.playOnAwake = false;
        spinAudioSource.loop = true;
        spinAudioSource.spatialBlend = 0f;
        spinAudioSource.priority = 64;
        spinAudioSource.ignoreListenerPause = true;
        return spinAudioSource;
    }

    // ── Perspective zoom ──────────────────────────────────────────────────────

    private Transform ResolveZoomTarget()
    {
        return zoomTarget != null ? zoomTarget : wheelTransform;
    }

    private void StartPerspectiveZoom()
    {
        if (!usePerspectiveZoom) return;

        Transform t = ResolveZoomTarget();
        if (t == null) return;

        if (!zoomBaseCaptured)
        {
            zoomBaseScale = t.localScale;
            zoomBaseCaptured = true;
        }

        zoomTween?.Kill();
        zoomTween = t.DOScale(zoomBaseScale * perspectiveZoom, zoomInDuration).SetEase(Ease.OutQuad);
    }

    private void EndPerspectiveZoom()
    {
        if (!usePerspectiveZoom || !zoomBaseCaptured) return;

        Transform t = ResolveZoomTarget();
        if (t == null) return;

        zoomTween?.Kill();
        zoomTween = t.DOScale(zoomBaseScale, zoomOutDuration).SetEase(Ease.OutBack);
    }

    // ── Winner pocket blink ───────────────────────────────────────────────────

    /// <summary>
    /// Flashes the highlight object sitting on the winning wedge. Does nothing
    /// unless wedgeHighlights has been filled in, so it is safe to leave empty.
    /// </summary>
    private void BlinkWinnerPocket(int index)
    {
        if (wedgeHighlights == null || index < 0 || index >= wedgeHighlights.Count) return;

        GameObject highlight = wedgeHighlights[index];
        if (highlight == null) return;

        KillWinnerBlink();

        lastHighlightedWedge = index;
        highlight.SetActive(true);

        var graphic = highlight.GetComponent<UnityEngine.UI.Graphic>();
        winnerBlinkSequence = DOTween.Sequence().SetUpdate(true).SetLink(highlight);

        float perBlink = winnerBlinkDuration / Mathf.Max(1, winnerBlinkCount);

        for (int i = 0; i < winnerBlinkCount; i++)
        {
            if (graphic != null)
            {
                // Fade blinking looks far better than SetActive toggling.
                winnerBlinkSequence.Append(graphic.DOFade(0.15f, perBlink * 0.5f));
                winnerBlinkSequence.Append(graphic.DOFade(1f, perBlink * 0.5f));
            }
            else
            {
                winnerBlinkSequence.AppendCallback(() => highlight.SetActive(false));
                winnerBlinkSequence.AppendInterval(perBlink * 0.5f);
                winnerBlinkSequence.AppendCallback(() => highlight.SetActive(true));
                winnerBlinkSequence.AppendInterval(perBlink * 0.5f);
            }
        }
    }

    /// <summary>Clears the previous round's highlight before a new spin starts.</summary>
    public void HideWinnerHighlight()
    {
        KillWinnerBlink();

        if (wedgeHighlights == null) return;
        if (lastHighlightedWedge < 0 || lastHighlightedWedge >= wedgeHighlights.Count) return;

        GameObject previous = wedgeHighlights[lastHighlightedWedge];
        if (previous != null) previous.SetActive(false);

        lastHighlightedWedge = -1;
    }

    private void KillWinnerBlink()
    {
        if (winnerBlinkSequence != null && winnerBlinkSequence.IsActive()) winnerBlinkSequence.Kill();
        winnerBlinkSequence = null;
    }

    private void PlayTickSoundAndPopNeedle(float speed01)
    {
        if (playPegTickAudio && AudioManager.Instance != null && Time.time - lastPegTickTime >= minPegTickInterval)
        {
            float pitchVariation = Mathf.Lerp(0.02f, 0.08f, Mathf.Clamp01(speed01));
            AudioManager.Instance.PlayOneShotRandomPitch(spinTickSoundKey, pitchVariation);
            lastPegTickTime = Time.time;
        }

        if (playPegTickHaptic && Time.time - lastPegHapticTime >= minPegHapticInterval)
        {
            HapticManager.Selection();
            lastPegHapticTime = Time.time;
        }

        /*if (needleVisual != null)
        {
            // Instantly snap the needle to 15 degrees. 
            // The Update() loop above smoothly pulls it back to 0.
            currentNeedleAngle = 15f;
            needleVisual.localEulerAngles = new Vector3(0, 0, currentNeedleAngle);
        }*/
    }

    public void StopSpin(bool fireCallback = false)
    {
        spinTween?.Kill();
        isSpinning = false;
        StopSpinAudio();
        EndPerspectiveZoom();
    }

    private void OnDestroy()
    {
        spinTween?.Kill();
        spinAudioFadeTween?.Kill();
        zoomTween?.Kill();
        KillWinnerBlink();
        StopSpinAudio();
    }
}
