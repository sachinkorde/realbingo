using DG.Tweening;
using RealBingo.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Settings panel — language, audio, haptics and notifications.
///
/// What the four rows in the designer's mock-up actually control:
///
///   • Notification  → NotificationManager.Enabled   (in-game notification list + bell badge)
///   • Vibration     → HapticManager.Enabled         (all haptic feedback)
///   • Sound         → AudioManager soundEnabled     (button clicks, SFX, announcer)
///   • Music BGM     → AudioManager musicVolume      (background music level, a slider not a switch)
///
/// Naming note: the switch wired to Sound.fx_Btn used to be called "musicSwitch"
/// and the Music BGM slider used to be called "masterVolumeSlider". Both names
/// described the wrong thing, and the master-volume one actually behaved wrong —
/// dragging the BGM slider turned the button clicks down with it. They are now
/// named for what they drive; [FormerlySerializedAs] keeps the existing scene
/// wiring intact.
///
/// Every field is optional: a screen that only has some of these rows can leave
/// the rest empty.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("The panel GameObject this script shows and hides. The script itself lives on an always-active object, so it must not be this GameObject.")]
    [SerializeField] private GameObject _settingPanel;

    [Tooltip("Hide the panel when the scene starts. Leave on unless a designer deliberately wants Settings open on load.")]
    [SerializeField] private bool hidePanelOnStart = true;

    [Header("Music BGM Volume")]
    [Tooltip("Background music level only. It no longer touches master volume, so turning the music down leaves button clicks and the announcer alone.")]
    [FormerlySerializedAs("masterVolumeSlider")]
    [SerializeField] private Slider musicVolumeSlider;

    [Tooltip("Seconds of no movement before the volume is written to disk. A Slider fires every frame of a drag, and PlayerPrefs.Save() is a real disk write.")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float volumeSaveDelay = 0.4f;

    [Header("Language Row")]
    [Tooltip("One entry per language button you laid out, left to right. Spare buttons hide themselves when fewer languages are available than slots.")]
    [SerializeField] private LanguageButton[] languageButtons;

    [Header("Switches")]
    [Tooltip("In-game notifications: the bell list and its unread badge.")]
    [SerializeField] private SwitchRow notificationSwitch;

    [Tooltip("Button clicks, SFX and the announcer. Wired to Sound.fx_Btn in GameScene.")]
    [FormerlySerializedAs("musicSwitch")]
    [SerializeField] private SwitchRow soundSwitch;

    [Tooltip("Haptic feedback. A separate preference from sound: players mute a game in public but still want to feel the result.")]
    [SerializeField] private SwitchRow vibrationSwitch;

    private Tween volumeSaveTween;
    private bool wired;

    /// <summary>
    /// One language button: the native name on top, the English name beneath,
    /// as in the designer's mock-up.
    /// </summary>
    [System.Serializable]
    public class LanguageButton
    {
        public Button button;

        [Tooltip("Native script, e.g. मराठी. Filled from LanguageOption.nativeName.")]
        public TMP_Text nativeLabel;
        [Tooltip("English name, e.g. Marathi. Filled from LanguageOption.englishName.")]
        public TMP_Text englishLabel;

        [Tooltip("Optional background swapped for the chosen look.")]
        public Image background;
        public Sprite selectedSprite;
        public Sprite unselectedSprite;

        [HideInInspector] public string code;
    }

    /// <summary>
    /// An on/off switch: a pill that changes sprite and a knob that slides, which
    /// is how Notification, Vibration and Sound are drawn in the mock-up.
    /// </summary>
    [System.Serializable]
    public class SwitchRow
    {
        public Button button;

        public Image background;
        public Sprite onSprite;
        public Sprite offSprite;

        [Tooltip("Optional knob. Left empty, only the background changes.")]
        public RectTransform knob;
        public float knobOffX = -42f;
        public float knobOnX = 42f;
        [Range(0f, 0.6f)] public float slideSeconds = 0.15f;

        /// <summary>Puts the switch in the given state, sliding the knob if there is one.</summary>
        public void Refresh(bool isOn, bool animate)
        {
            if (background != null && onSprite != null && offSprite != null)
                background.sprite = isOn ? onSprite : offSprite;

            if (knob == null) return;

            float x = isOn ? knobOnX : knobOffX;
            knob.DOKill();

            // A hidden panel cannot tween: DOTween skips inactive objects, so the
            // knob would be left mid-slide. Snap whenever the row is not visible.
            bool canAnimate = animate && slideSeconds > 0f && knob.gameObject.activeInHierarchy;

            if (canAnimate)
                knob.DOAnchorPosX(x, slideSeconds).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(knob.gameObject);
            else
                knob.anchoredPosition = new Vector2(x, knob.anchoredPosition.y);
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        Wire();

        // Settings must not be sitting open over the table when the round starts.
        // The scene is saved with SettingPanel active so it can be laid out in the
        // editor, which is exactly why this has to be closed in code.
        if (hidePanelOnStart && _settingPanel != null && _settingPanel != gameObject)
            _settingPanel.SetActive(false);
    }

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += RefreshLanguageButtons;
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= RefreshLanguageButtons;
        FlushVolumeSave();
    }

    private void OnDestroy()
    {
        FlushVolumeSave();
    }

    /// <summary>
    /// Binds every control once. Guarded because Start is not the only way in:
    /// OpenSettingPanel calls it too, in case a button opens the panel before
    /// this script's Start has run.
    /// </summary>
    private void Wire()
    {
        if (wired) return;
        wired = true;

        if (musicVolumeSlider != null)
        {
            // SetValueWithoutNotify so restoring the saved value doesn't fire
            // OnMusicVolumeChanged and re-save it during start-up.
            if (AudioManager.Instance)
                musicVolumeSlider.SetValueWithoutNotify(AudioManager.Instance.GetMusicVolume());

            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }

        if (notificationSwitch?.button != null)
            notificationSwitch.button.onClick.AddListener(OnNotificationSwitchClicked);

        if (soundSwitch?.button != null)
            soundSwitch.button.onClick.AddListener(OnSoundSwitchClicked);

        if (vibrationSwitch?.button != null)
            vibrationSwitch.button.onClick.AddListener(OnVibrationSwitchClicked);

        BindLanguageButtons();
        RefreshSwitches(animate: false);
    }

    // ── Open / close ─────────────────────────────────────────────────────────

    public void OpenSettingPanel()
    {
        if (_settingPanel == null)
        {
            Debug.LogWarning("[SettingsPanel] Setting Panel is not assigned, so there is nothing to open.", this);
            return;
        }

        Wire();

        _settingPanel.SetActive(true);

        // Re-read every preference on open. These can change outside this panel
        // (another screen, a fresh install, a saved pref), and the knobs must
        // already be in the right place when the panel appears — hence snap, not
        // slide. This used to live in OnEnable, which never fired: this script
        // sits on an always-active object and only the panel below it toggles.
        BindLanguageButtons();
        SyncControls(animate: false);
    }

    public void CloseSettingPanel()
    {
        // Closing inside the debounce window would otherwise drop the volume the
        // player just set.
        FlushVolumeSave();

        if (_settingPanel != null) _settingPanel.SetActive(false);
    }

    // ── Switches ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Notifications on/off — the in-game bell list and badge, and the reminders
    /// the phone shows while the game is closed, together.
    /// </summary>
    public void OnNotificationSwitchClicked()
    {
        bool turningOn = !NotificationManager.Enabled;

        if (NotificationManager.Instance != null)
        {
            // Goes through the manager so the badge, the OS permission prompt and
            // anything already queued on the device are all dealt with.
            NotificationManager.Instance.SetEnabled(turningOn);
        }
        else
        {
            // No manager in this scene: the preference is still worth storing, and
            // whichever scene does have one will pick it up.
            NotificationManager.Enabled = turningOn;
        }

        AudioManager.Instance?.PlayOneShot("UIButtonClick");
        HapticManager.Selection();
        RefreshSwitches(animate: true);
    }

    /// <summary>
    /// Button clicks, SFX and the announcer. This row did nothing at all before —
    /// it refreshed the visuals without ever toggling the setting behind them.
    /// </summary>
    public void OnSoundSwitchClicked()
    {
        if (AudioManager.Instance)
        {
            bool turningOn = !AudioManager.Instance.IsSoundEnabled();

            AudioManager.Instance.SetSoundEnabled(turningOn);

            // The confirmation click only exists once sound is back on.
            if (turningOn) AudioManager.Instance.PlayOneShot("UIButtonClick");
        }

        HapticManager.Selection();
        RefreshSwitches(animate: true);
    }

    /// <summary>Haptic feedback. Toggle() pulses once when switched ON, as confirmation.</summary>
    public void OnVibrationSwitchClicked()
    {
        HapticManager.Toggle();

        AudioManager.Instance?.PlayOneShot("UIButtonClick");
        RefreshSwitches(animate: true);
    }

    private void RefreshSwitches(bool animate)
    {
        notificationSwitch?.Refresh(NotificationManager.Enabled, animate);
        vibrationSwitch?.Refresh(HapticManager.Enabled, animate);

        // Was missing entirely, which is why the Sound pill never moved.
        soundSwitch?.Refresh(AudioManager.Instance == null || AudioManager.Instance.IsSoundEnabled(), animate);
    }

    /// <summary>Pulls every control back in line with the saved settings.</summary>
    private void SyncControls(bool animate)
    {
        RefreshSwitches(animate);

        if (musicVolumeSlider != null && AudioManager.Instance)
            musicVolumeSlider.SetValueWithoutNotify(AudioManager.Instance.GetMusicVolume());

        RefreshLanguageButtons();
    }

    // ── Music BGM volume ─────────────────────────────────────────────────────

    /// <summary>Hook to the Music BGM slider (0 = silent, 1 = full).</summary>
    public void OnMusicVolumeChanged(float value)
    {
        if (!AudioManager.Instance) return;

        // Music only. The old handler called SetMasterVolume, which scaled the
        // clicks and the announcer along with the BGM.
        //
        // persist:false, then one save once the drag settles. The persisting
        // overload writes PlayerPrefs on EVERY frame of a drag — the exact thing
        // AudioManager's own note warns against.
        AudioManager.Instance.SetMusicVolume(value, false);

        // A mute saved by an older build would leave this slider doing nothing
        // visible. Raising it off zero is an unambiguous "I want music".
        if (value > 0f && !AudioManager.Instance.IsMusicEnabled())
            AudioManager.Instance.SetMusicEnabled(true);

        QueueVolumeSave();
    }

    /// <summary>
    /// Schedules one save a moment after the last slider movement, so a drag
    /// writes to disk once rather than every frame.
    /// </summary>
    private void QueueVolumeSave()
    {
        volumeSaveTween?.Kill();
        volumeSaveTween = DOVirtual.DelayedCall(volumeSaveDelay, () =>
        {
            volumeSaveTween = null;
            if (AudioManager.Instance) AudioManager.Instance.SaveSettings();
        }).SetUpdate(true).SetLink(gameObject);   // unscaled: the game may be paused
    }

    /// <summary>Writes a pending volume change immediately.</summary>
    private void FlushVolumeSave()
    {
        if (volumeSaveTween == null) return;

        volumeSaveTween.Kill();
        volumeSaveTween = null;

        if (AudioManager.Instance) AudioManager.Instance.SaveSettings();
    }

    // ── Language row ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fills the language row from the languages that actually have a column in
    /// strings.csv, in that order, and hides any spare buttons.
    ///
    /// Data-driven on purpose: add a column to the sheet and its button appears
    /// here by itself, with no code change and nothing to re-wire.
    /// </summary>
    private void BindLanguageButtons()
    {
        if (languageButtons == null || languageButtons.Length == 0) return;

        // Without this, a Start that runs before the manager's Awake sees only
        // English available and permanently hides Hindi and Marathi.
        LocalizationManager.Ensure();

        var available = LocalizationManager.AvailableLanguages();

        for (int i = 0; i < languageButtons.Length; i++)
        {
            LanguageButton slot = languageButtons[i];
            if (slot == null || slot.button == null) continue;

            // More buttons than languages: the spares stay out of the way rather
            // than sitting there blank.
            if (i >= available.Count)
            {
                slot.code = null;
                slot.button.onClick.RemoveAllListeners();
                slot.button.gameObject.SetActive(false);
                continue;
            }

            LanguageOption option = available[i];
            slot.code = option.code;
            slot.button.gameObject.SetActive(true);

            // Fix() reorders the Devanagari matras TMP would otherwise draw wrong.
            if (slot.nativeLabel != null) slot.nativeLabel.text = DevanagariShaper.Fix(option.nativeName);
            if (slot.englishLabel != null) slot.englishLabel.text = option.englishName;

            string code = option.code;
            // RemoveAllListeners drops only runtime listeners, so re-binding cannot
            // stack duplicates. A lambda cannot be removed by value, which is why
            // this is not RemoveListener.
            slot.button.onClick.RemoveAllListeners();
            slot.button.onClick.AddListener(() => OnLanguageClicked(code));
        }

        if (available.Count > languageButtons.Length)
        {
            Debug.LogWarning($"[SettingsPanel] {available.Count} languages are available but the row " +
                             $"has {languageButtons.Length} buttons, so the last " +
                             $"{available.Count - languageButtons.Length} cannot be chosen.", this);
        }

        RefreshLanguageButtons();
    }

    private void OnLanguageClicked(string code)
    {
        if (string.IsNullOrEmpty(code)) return;

        AudioManager.Instance?.PlayOneShot("UIButtonClick");
        HapticManager.Selection();

        // persist: the player picked this deliberately, from Settings.
        LocalizationManager.Instance?.SetLanguage(code, persist: true);

        RefreshLanguageButtons();
    }

    /// <summary>Marks whichever button matches the live language as the chosen one.</summary>
    private void RefreshLanguageButtons()
    {
        if (languageButtons == null) return;

        string current = LocalizationManager.CurrentLanguage;

        foreach (LanguageButton slot in languageButtons)
        {
            if (slot == null || slot.button == null || string.IsNullOrEmpty(slot.code)) continue;

            bool chosen = string.Equals(slot.code, current, System.StringComparison.OrdinalIgnoreCase);

            if (slot.background == null) continue;

            if (slot.selectedSprite != null && slot.unselectedSprite != null)
                slot.background.sprite = chosen ? slot.selectedSprite : slot.unselectedSprite;
        }
    }
}
