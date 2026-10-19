using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Settings popup: Music, Sound, Vibration and Volume.
///
/// Touches nothing but settings — no game flow, no betting, no round logic. It
/// reads and writes AudioManager and HapticManager, both of which already
/// persist their own PlayerPrefs, so there is no extra save code here.
///
/// SETUP
///   1. Put this on the Setting popup root.
///   2. Fill in the three rows (button + icon image + on/off sprites) and the
///      volume Slider.
///   3. Assign Close Button, and call Open() from your gear icon.
///
/// Every field is optional — assign only the rows your panel actually has.
///
/// NOTE: do NOT also keep the older SettingsPanel component on this same
/// object. Both would attach listeners to the same buttons and each tap would
/// toggle twice, cancelling itself out.
/// </summary>
public class SettingsPanelUI : MonoBehaviour
{
    /// <summary>One icon row: a button, its icon, and the two sprites it swaps between.</summary>
    [System.Serializable]
    public class ToggleRow
    {
        public Button button;
        public Image icon;

        [Tooltip("Shown when the option is ON. Leave both sprites empty to use colour tinting instead.")]
        public Sprite onSprite;
        public Sprite offSprite;

        [Tooltip("Used only when no sprites are assigned — the icon dims when OFF.")]
        public Color onColor = Color.white;
        public Color offColor = new Color(1f, 1f, 1f, 0.3f);

        /// <summary>Reflects state on the icon, whichever style is configured.</summary>
        public void Refresh(bool isOn)
        {
            if (icon == null) return;

            if (onSprite != null && offSprite != null)
            {
                icon.sprite = isOn ? onSprite : offSprite;
                icon.color = Color.white;
            }
            else
            {
                icon.color = isOn ? onColor : offColor;
            }
        }
    }

    [Header("Music  —  background theme")]
    public ToggleRow musicRow;

    [Header("Sound  —  effects + announcer voice")]
    [Tooltip("Your panel labels this row 'BGM'. See the note in the summary — 'Sound' is the accurate label.")]
    public ToggleRow soundRow;

    [Header("Vibration  —  haptics")]
    public ToggleRow vibrationRow;

    [Header("Volume")]
    public Slider volumeSlider;

    [Tooltip("Seconds of no movement before the volume is written to disk. Stops a drag hammering PlayerPrefs every frame.")]
    [Range(0.1f, 1.5f)] public float volumeSaveDelay = 0.4f;

    [Header("Panel")]
    [Tooltip("Object to show/hide. Leave empty to use this GameObject.")]
    public GameObject panelRoot;
    public Button closeButton;

    [Tooltip("Optional dark backdrop; tapping it closes the panel.")]
    public Button backdropButton;

    [Header("Open / Close Animation")]
    public bool animateOpenClose = true;
    public float openDuration = 0.25f;
    public float closeDuration = 0.18f;

    [Header("Optional")]
    [Tooltip("An icon elsewhere in the HUD that should mirror the sound state (e.g. the speaker on the game screen).")]
    public Image externalSoundIcon;
    public Sprite externalSoundOnSprite;
    public Sprite externalSoundOffSprite;

    private RectTransform panelRect;
    private Tween panelTween;
    private Tween volumeSaveTween;
    private Vector3 panelBaseScale = Vector3.one;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        panelRect = panelRoot.transform as RectTransform;

        if (panelRect != null) panelBaseScale = panelRect.localScale;

        if (musicRow?.button != null) musicRow.button.onClick.AddListener(ToggleMusic);
        if (soundRow?.button != null) soundRow.button.onClick.AddListener(ToggleSound);
        if (vibrationRow?.button != null) vibrationRow.button.onClick.AddListener(ToggleVibration);

        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (backdropButton != null) backdropButton.onClick.AddListener(Close);

        if (volumeSlider != null)
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
    }

    private void OnEnable()
    {
        // The state can change from elsewhere between openings, so always
        // re-read it rather than trusting what the icons happen to show.
        SyncVolumeSlider();
        RefreshAll();
    }

    private void OnDestroy()
    {
        panelTween?.Kill();
        volumeSaveTween?.Kill();

        if (musicRow?.button != null) musicRow.button.onClick.RemoveListener(ToggleMusic);
        if (soundRow?.button != null) soundRow.button.onClick.RemoveListener(ToggleSound);
        if (vibrationRow?.button != null) vibrationRow.button.onClick.RemoveListener(ToggleVibration);
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (backdropButton != null) backdropButton.onClick.RemoveListener(Close);
        if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
    }

    // ── Open / close ──────────────────────────────────────────────────────────

    /// <summary>Hook this to the gear icon's OnClick.</summary>
    public void Open()
    {
        if (panelRoot == null) panelRoot = gameObject;

        panelRoot.SetActive(true);
        SyncVolumeSlider();
        RefreshAll();

        HapticManager.Light();
        AudioManager.Instance?.PlayOneShot("PanelOpen");

        if (!animateOpenClose || panelRect == null) return;

        panelTween?.Kill();
        panelRect.localScale = panelBaseScale * 0.85f;
        panelTween = panelRect.DOScale(panelBaseScale, openDuration)
                              .SetEase(Ease.OutBack)
                              .SetUpdate(true)
                              .SetLink(gameObject);
    }

    /// <summary>Hook this to the X button, or call it from anywhere.</summary>
    public void Close()
    {
        HapticManager.Selection();
        AudioManager.Instance?.PlayOneShot("PanelClose");

        // Any pending volume write must land before the panel goes away.
        FlushVolumeSave();

        if (!animateOpenClose || panelRect == null)
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            return;
        }

        panelTween?.Kill();
        panelTween = panelRect.DOScale(panelBaseScale * 0.85f, closeDuration)
                              .SetEase(Ease.InBack)
                              .SetUpdate(true)
                              .SetLink(gameObject)
                              .OnComplete(() =>
                              {
                                  panelRect.localScale = panelBaseScale;
                                  if (panelRoot != null) panelRoot.SetActive(false);
                              });
    }

    public void Toggle()
    {
        if (panelRoot == null) panelRoot = gameObject;

        if (panelRoot.activeSelf) Close();
        else Open();
    }

    // ── Toggles ───────────────────────────────────────────────────────────────

    public void ToggleMusic()
    {
        AudioManager.Instance?.ToggleMusic();
        HapticManager.Selection();
        RefreshAll();
    }

    public void ToggleSound()
    {
        if (AudioManager.Instance == null) return;

        bool turningOn = !AudioManager.Instance.IsSoundEnabled();
        AudioManager.Instance.ToggleSound();

        // Only audible once sound is back on — a confirmation click while muting
        // would be the last thing you hear, which reads as a bug.
        if (turningOn) AudioManager.Instance.PlayOneShot("Click");

        HapticManager.Selection();
        RefreshAll();
    }

    public void ToggleVibration()
    {
        HapticManager.Toggle();   // pulses once when switched ON, as confirmation
        RefreshAll();
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    private void OnVolumeChanged(float value)
    {
        if (AudioManager.Instance == null) return;

        // Apply immediately so the player hears it, but defer the disk write.
        AudioManager.Instance.SetMasterVolume(value, false);

        volumeSaveTween?.Kill();
        volumeSaveTween = DOVirtual.DelayedCall(volumeSaveDelay, FlushVolumeSave)
                                   .SetUpdate(true)
                                   .SetLink(gameObject);
    }

    /// <summary>Writes the volume to PlayerPrefs once the slider settles.</summary>
    private void FlushVolumeSave()
    {
        volumeSaveTween?.Kill();
        volumeSaveTween = null;

        AudioManager.Instance?.SaveSettings();
    }

    private void SyncVolumeSlider()
    {
        if (volumeSlider == null || AudioManager.Instance == null) return;

        // SetValueWithoutNotify, or restoring the saved value would fire
        // OnValueChanged and immediately re-save it during panel open.
        volumeSlider.SetValueWithoutNotify(AudioManager.Instance.GetMasterVolume());
    }

    // ── Icon state ────────────────────────────────────────────────────────────

    /// <summary>Re-reads every setting and repaints the icons. Safe to call any time.</summary>
    public void RefreshAll()
    {
        bool musicOn = AudioManager.Instance == null || AudioManager.Instance.IsMusicEnabled();
        bool soundOn = AudioManager.Instance == null || AudioManager.Instance.IsSoundEnabled();

        musicRow?.Refresh(musicOn);
        soundRow?.Refresh(soundOn);
        vibrationRow?.Refresh(HapticManager.Enabled);

        if (externalSoundIcon != null)
        {
            Sprite target = soundOn ? externalSoundOnSprite : externalSoundOffSprite;
            if (target != null) externalSoundIcon.sprite = target;
        }
    }
}
