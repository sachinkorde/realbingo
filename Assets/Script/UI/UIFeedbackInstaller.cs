using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gives EVERY Button and Toggle in the game a press animation + vibration
/// without touching a single scene or prefab.
///
/// It boots itself when the game starts (no GameObject to place, no Inspector
/// wiring), attaches <see cref="ButtonFeedback"/> to every control it finds,
/// and re-scans periodically so controls that are spawned at runtime
/// (notification rows, referral rows, popups instantiated from prefabs) get
/// the same feel.
///
/// Cost control � this runs on a phone, so:
///   � the scan only ADDS a missing component, it never touches existing ones,
///   � the interval backs off from 2s up to 8s while nothing new appears, and
///     snaps back to 2s the moment a new control is found or a scene loads.
///
/// If you would rather bake the components into the scenes permanently, run
/// Tools ? Real Bingo ? UI Feedback ? Add To Open Scenes, then set
/// <see cref="AutoInstall"/> to false.
/// </summary>
public class UIFeedbackInstaller : MonoBehaviour
{
    /// <summary>Set to false before the first scene loads to disable auto-attaching.</summary>
    public static bool AutoInstall = true;

    /// <summary>Haptic used for normal buttons.</summary>
    public static HapticType ButtonHaptic = HapticType.Light;

    /// <summary>Haptic used for toggles / checkboxes (lighter than a button).</summary>
    public static HapticType ToggleHaptic = HapticType.Selection;

    private const float MinRescanInterval = 2f;
    private const float MaxRescanInterval = 8f;

    private static UIFeedbackInstaller instance;

    private float rescanInterval = MinRescanInterval;
    private float nextScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!AutoInstall || instance != null) return;

        var host = new GameObject("~UIFeedback");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<UIFeedbackInstaller>();

        SceneManager.sceneLoaded += OnSceneLoaded;
        InstallAll();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (instance == null) return;

        // A fresh scene is full of new controls � scan now and go back to the
        // fast interval for a while.
        instance.rescanInterval = MinRescanInterval;
        instance.nextScanTime = 0f;
        InstallAll();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScanTime) return;

        int added = InstallAll();

        // Quiet UI ? scan less often. Something new ? go back to reacting fast.
        rescanInterval = added > 0
            ? MinRescanInterval
            : Mathf.Min(rescanInterval * 1.5f, MaxRescanInterval);

        nextScanTime = Time.unscaledTime + rescanInterval;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }
    }

    /// <summary>
    /// Attaches ButtonFeedback to every Button/Toggle that does not have one.
    /// Safe to call at any time � call it yourself right after instantiating a
    /// batch of UI if you want the feedback active on the very first frame.
    /// Returns how many controls were newly equipped.
    /// </summary>
    public static int InstallAll()
    {
        var selectables = Object.FindObjectsByType<Selectable>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int added = 0;

        for (int i = 0; i < selectables.Length; i++)
        {
            Selectable s = selectables[i];
            if (s == null) continue;

            bool isButton = s is Button;
            bool isToggle = s is Toggle;
            if (!isButton && !isToggle) continue;                 // sliders/inputs stay untouched

            if (s.GetComponent<ButtonFeedback>() != null) continue;

            var feedback = s.gameObject.AddComponent<ButtonFeedback>();
            feedback.hapticType = isToggle ? ToggleHaptic : ButtonHaptic;

            // Chips and board numbers already fire their own, more meaningful
            // pulse from BetManager / ChipButton the moment the action lands.
            // Adding a press tap on top would read as one mushy double-buzz.
            bool isChip = s.GetComponent<ChipButton>() != null;
            if (isChip || s.GetComponent<NumberButton>() != null)
                feedback.hapticOnPress = false;

            // ChipButton already tweens localScale on the SAME transform for its
            // select/deselect states. Two tweens writing one property fight each
            // other and the chip can settle at the wrong size, so the press
            // animation is left to ChipButton alone. (NumberButton does not
            // scale itself, so it keeps the press squash.)
            if (isChip) feedback.animateOnPress = false;

            added++;
        }

        return added;
    }
}
