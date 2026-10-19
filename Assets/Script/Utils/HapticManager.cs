using System.Runtime.InteropServices;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Strength / meaning of a haptic pulse. Kept small on purpose � these map
/// cleanly onto both Android amplitudes and iOS UIFeedbackGenerator styles.
/// </summary>
public enum HapticType
{
    Selection = 0,  // chip picked, tab switched � the lightest tap there is
    Light = 1,  // normal button press
    Medium = 2,  // confirm / panel open
    Heavy = 3,  // big commit (repeat bets, cash out)
    Success = 4,  // deposit ok, promo accepted
    Warning = 5,  // betting about to close, weak connection
    Failure = 6,  // rejected bet, insufficient balance
    Win = 7,  // round won  (celebratory rising triple-tap)
    Lose = 8,  // round lost (single soft fading buzz)

    // PRD Topic 7 additions
    Strike = 9,  // "Metallic Haptic Strike" � high amplitude, very low duration (OK / bet lock)
    DoubleTap = 10, // "Double Shot Sharp Haptic Tap" � wheel needle snapping into a pocket
    LongHeavy = 11, // "Heavy Long Vibe" � board lockdown at 0 seconds
    CountdownPulse = 12, // "Intense Pulsing Vibration" � the final 3-2-1 urgency window
}

/// <summary>
/// Cross-platform vibration / haptic feedback.
///
/// Usage anywhere in the game (no scene object, no Inspector wiring needed):
///
///     HapticManager.Light();                 // button tap
///     HapticManager.Selection();             // chip / toggle
///     HapticManager.Win();                   // round won
///     HapticManager.Strike();                // bet locked
///     HapticManager.Rumble(4f);              // 4-second jackpot rumble
///     HapticManager.Custom(40, 160);         // 40 ms at amplitude 160/255
///
/// Behaviour per platform:
///   � Android  � real Vibrator with per-pulse duration AND amplitude
///                (VibrationEffect on API 26+, VibratorManager on API 31+).
///                Falls back to plain vibrate(ms) on older devices.
///                Needs android.permission.VIBRATE � added automatically by
///                Assets/Editor/RBAndroidVibratePermission.cs at build time.
///   � iOS      � UISelection/UIImpact/UINotificationFeedbackGenerator through
///                Assets/Plugins/iOS/RBHaptics.mm. Patterns that iOS has no
///                native equivalent for (double tap, rumble) are composed here
///                from scheduled single impacts.
///   � Editor / other � no-op (safe to call from anywhere).
///
/// The player toggle is persisted in PlayerPrefs under "HapticsEnabled", so it
/// survives app restarts just like the sound toggle in AudioManager.
/// </summary>
public static class HapticManager
{
    public const string EnabledPrefKey = "HapticsEnabled";

    /// <summary>
    /// Guard against buzz-spam: rapid taps (or a stray double event) must not
    /// stack into one long ugly rumble. Light taps are throttled hard; the
    /// meaningful pulses (money moved, round result) get their own, looser
    /// budget so a press-tap can never swallow the pulse that actually matters.
    /// </summary>
    private const float MinTapInterval = 0.035f;
    private const float MinStrongInterval = 0.12f;

    /// <summary>Hard ceiling on Rumble() so a bad parameter can never lock the motor on.</summary>
    private const float MaxRumbleSeconds = 6f;

    private static bool initialized;
    private static bool hapticsEnabled = true;
    private static bool deviceSupported;
    private static float lastTapTime = -999f;
    private static float lastStrongTime = -999f;
    private static Sequence composedPattern;

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject vibrator;
    private static AndroidJavaClass vibrationEffectClass;
    private static int sdkInt;
    private static bool amplitudeSupported;
#endif

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void RBHapticsPlay(int type);
    [DllImport("__Internal")] private static extern bool RBHapticsSupported();
#endif

    // �� Public API ������������������������������������������������������������

    /// <summary>Player-facing on/off switch. Saved to PlayerPrefs immediately.</summary>
    public static bool Enabled
    {
        get { EnsureInitialized(); return hapticsEnabled; }
        set
        {
            EnsureInitialized();
            if (hapticsEnabled == value) return;

            hapticsEnabled = value;
            PlayerPrefs.SetInt(EnabledPrefKey, value ? 1 : 0);
            PlayerPrefs.Save();

            if (!value) Cancel();
            else Play(HapticType.Medium);   // confirm the "on" state with a pulse
        }
    }

    /// <summary>True when this device actually has a working vibrator.</summary>
    public static bool IsSupported { get { EnsureInitialized(); return deviceSupported; } }

    public static void Toggle() => Enabled = !Enabled;

    public static void Selection() => Play(HapticType.Selection);
    public static void Light() => Play(HapticType.Light);
    public static void Medium() => Play(HapticType.Medium);
    public static void Heavy() => Play(HapticType.Heavy);
    public static void Success() => Play(HapticType.Success);
    public static void Warning() => Play(HapticType.Warning);
    public static void Failure() => Play(HapticType.Failure);
    public static void Win() => Play(HapticType.Win);
    public static void Lose() => Play(HapticType.Lose);
    public static void Strike() => Play(HapticType.Strike);
    public static void DoubleTap() => Play(HapticType.DoubleTap);
    public static void LongHeavy() => Play(HapticType.LongHeavy);
    public static void CountdownPulse() => Play(HapticType.CountdownPulse);

    public static void Play(HapticType type)
    {
        EnsureInitialized();

        if (!hapticsEnabled || !deviceSupported) return;

        float now = Time.unscaledTime;

        // Win / Lose are the emotional beats of the round � never swallow them.
        bool alwaysPlay = type == HapticType.Win || type == HapticType.Lose;
        bool isTap = type == HapticType.Selection || type == HapticType.Light;

        if (!alwaysPlay)
        {
            if (isTap)
            {
                if (now - lastTapTime < MinTapInterval) return;
            }
            else if (now - lastStrongTime < MinStrongInterval)
            {
                return;
            }
        }

        lastTapTime = now;
        if (!isTap) lastStrongTime = now;

        Emit(type);
    }

    /// <summary>
    /// Sustained rumble � the PRD's 4-second jackpot wave. Android runs it as a
    /// single long high-amplitude pulse; iOS has no continuous-vibration API, so
    /// it is approximated with a train of heavy impacts.
    /// </summary>
    public static void Rumble(float seconds = 4f)
    {
        EnsureInitialized();

        if (!hapticsEnabled || !deviceSupported) return;

        seconds = Mathf.Clamp(seconds, 0.1f, MaxRumbleSeconds);
        lastStrongTime = Time.unscaledTime;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidOneShot((long)(seconds * 1000f), 255);
#else
        KillComposedPattern();

        composedPattern = DOTween.Sequence().SetUpdate(true);
        for (float t = 0f; t < seconds; t += 0.12f)
        {
            composedPattern.AppendCallback(() => Emit(HapticType.Heavy));
            composedPattern.AppendInterval(0.12f);
        }
#endif
    }

    /// <summary>
    /// Raw one-shot pulse. duration in milliseconds, amplitude 1�255
    /// (amplitude is ignored on iOS and on Android below API 26).
    /// </summary>
    public static void Custom(long milliseconds, int amplitude = 180)
    {
        EnsureInitialized();

        if (!hapticsEnabled || !deviceSupported) return;
        if (milliseconds <= 0) return;
        if (Time.unscaledTime - lastTapTime < MinTapInterval) return;
        lastTapTime = Time.unscaledTime;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidOneShot(milliseconds, amplitude);
#elif UNITY_IOS && !UNITY_EDITOR
        // iOS gives no duration control without CoreHaptics; pick the closest style.
        HapticType style = milliseconds >= 60 ? HapticType.Heavy
                         : milliseconds >= 25 ? HapticType.Medium
                                              : HapticType.Light;
        Emit(style);
#endif
    }

    /// <summary>Stops any vibration currently running.</summary>
    public static void Cancel()
    {
        KillComposedPattern();

#if UNITY_ANDROID && !UNITY_EDITOR
        try { vibrator?.Call("cancel"); } catch { /* device refused � nothing to do */ }
#endif
    }

    private static void KillComposedPattern()
    {
        if (composedPattern != null && composedPattern.IsActive()) composedPattern.Kill();
        composedPattern = null;
    }

    // �� Platform dispatch ����������������������������������������������������

    /// <summary>Fires a pattern without any throttling. Internal use only.</summary>
    private static void Emit(HapticType type)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(type);
#elif UNITY_IOS && !UNITY_EDITOR
        try
        {
            RBHapticsPlay((int)type);

            // iOS has no multi-pulse API � compose the patterns from single hits.
            if (type == HapticType.DoubleTap)
            {
                DOVirtual.DelayedCall(0.09f, () => { try { RBHapticsPlay((int)HapticType.Heavy); } catch { } })
                         .SetUpdate(true);
            }
            else if (type == HapticType.CountdownPulse)
            {
                DOVirtual.DelayedCall(0.10f, () => { try { RBHapticsPlay((int)HapticType.Medium); } catch { } })
                         .SetUpdate(true);
                DOVirtual.DelayedCall(0.20f, () => { try { RBHapticsPlay((int)HapticType.Heavy); } catch { } })
                         .SetUpdate(true);
            }
        }
        catch { deviceSupported = false; }
#endif
    }

    // �� Initialisation ��������������������������������������������������������

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize() => EnsureInitialized();

    private static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        hapticsEnabled = PlayerPrefs.GetInt(EnabledPrefKey, 1) == 1;
        deviceSupported = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        InitAndroid();
#elif UNITY_IOS && !UNITY_EDITOR
        try { deviceSupported = RBHapticsSupported(); } catch { deviceSupported = false; }
#endif
    }

    // �� Android ��������������������������������������������������������������

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void InitAndroid()
    {
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdkInt = version.GetStatic<int>("SDK_INT");

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null) return;

                // Android 12+ routes through VibratorManager; older APIs use the
                // Vibrator service directly.
                if (sdkInt >= 31)
                {
                    using (var manager = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                    {
                        if (manager != null)
                            vibrator = manager.Call<AndroidJavaObject>("getDefaultVibrator");
                    }
                }

                if (vibrator == null)
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            if (vibrator == null) return;

            deviceSupported = vibrator.Call<bool>("hasVibrator");
            if (!deviceSupported) return;

            if (sdkInt >= 26)
            {
                vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                amplitudeSupported = vibrator.Call<bool>("hasAmplitudeControl");
            }
        }
        catch (System.Exception e)
        {
            // A missing VIBRATE permission or an exotic ROM must never take the
            // game down � haptics simply switch themselves off.
            Debug.LogWarning($"[HapticManager] Android init failed, haptics disabled: {e.Message}");
            deviceSupported = false;
        }
    }

    private static void PlayAndroid(HapticType type)
    {
        switch (type)
        {
            case HapticType.Selection: AndroidOneShot(10, 60); break;
            case HapticType.Light:     AndroidOneShot(18, 110); break;
            case HapticType.Medium:    AndroidOneShot(32, 170); break;
            case HapticType.Heavy:     AndroidOneShot(55, 230); break;

            // Metallic strike: maximum amplitude, minimum duration � reads as a
            // hard "clack" rather than a buzz.
            case HapticType.Strike:    AndroidOneShot(22, 255); break;

            case HapticType.LongHeavy: AndroidOneShot(320, 240); break;

            case HapticType.DoubleTap:
                AndroidWaveform(new long[] { 0, 18, 70, 18 }, new int[] { 0, 255, 0, 255 });
                break;

            case HapticType.CountdownPulse:
                AndroidWaveform(new long[] { 0, 40, 60, 40, 60, 55 }, new int[] { 0, 190, 0, 220, 0, 255 });
                break;

            case HapticType.Success:
                AndroidWaveform(new long[] { 0, 22, 55, 45 }, new int[] { 0, 140, 0, 210 });
                break;

            case HapticType.Warning:
                AndroidWaveform(new long[] { 0, 35, 70, 35 }, new int[] { 0, 170, 0, 170 });
                break;

            case HapticType.Failure:
                AndroidWaveform(new long[] { 0, 45, 45, 45, 45, 45 }, new int[] { 0, 200, 0, 200, 0, 200 });
                break;

            case HapticType.Win:
                // Rising celebratory triple-tap.
                AndroidWaveform(
                    new long[] { 0, 40, 50, 55, 50, 90 },
                    new int[] { 0, 120, 0, 180, 0, 255 });
                break;

            case HapticType.Lose:
                // One low, soft, fading buzz � acknowledges the loss without nagging.
                AndroidWaveform(new long[] { 0, 120, 60, 70 }, new int[] { 0, 110, 0, 60 });
                break;
        }
    }

    private static void AndroidOneShot(long milliseconds, int amplitude)
    {
        if (vibrator == null) return;

        try
        {
            if (sdkInt >= 26 && vibrationEffectClass != null)
            {
                // -1 == VibrationEffect.DEFAULT_AMPLITUDE for devices without
                // amplitude control.
                int amp = amplitudeSupported ? Mathf.Clamp(amplitude, 1, 255) : -1;

                using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amp))
                    vibrator.Call("vibrate", effect);
            }
            else
            {
                vibrator.Call("vibrate", milliseconds);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[HapticManager] vibrate failed: {e.Message}");
            deviceSupported = false;
        }
    }

    private static void AndroidWaveform(long[] timings, int[] amplitudes)
    {
        if (vibrator == null) return;

        try
        {
            if (sdkInt >= 26 && vibrationEffectClass != null)
            {
                AndroidJavaObject effect = amplitudeSupported
                    ? vibrationEffectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1)
                    : vibrationEffectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, -1);

                using (effect) vibrator.Call("vibrate", effect);
            }
            else
            {
                vibrator.Call("vibrate", timings, -1);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[HapticManager] waveform failed: {e.Message}");
            deviceSupported = false;
        }
    }
#endif
}
