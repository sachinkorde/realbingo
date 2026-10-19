using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings panel � audio and haptic controls.
///
/// FIX-SP-01: Original used AudioListener.volume = 0 to mute, but never
///            updated AudioManager.soundEnabled. This meant AudioManager
///            thought sounds were on while AudioListener silenced them �
///            the two systems were completely out of sync.
///
///            Now: AudioManager.SetSoundEnabled() and SetMusicEnabled() are
///            called so the entire audio system is in one consistent state.
///            AudioListener.volume is no longer touched here.
///
/// PRD Topic 16.F adds three independent switches plus a master volume slider:
///     1. Background Music (BGM)
///     2. Game SFX &amp; Voice
///     3. Haptic Vibrations
///     4. Master Volume (horizontal slider)
///
/// EVERY field below is optional. The original single mute button still works
/// exactly as before if that is all you assign; wire the new controls only on
/// the screens that have them.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Legacy Combined Mute (optional)")]
    [SerializeField] private Button settingBtn;
    [SerializeField] private GameObject _settingPanel;
    //[Tooltip("Original single button that mutes music AND sfx together.")]
    //[SerializeField] private Button soundButton;
    //[SerializeField] private Image soundIconImage;

    [Header("Sprites")]
    [SerializeField] private Sprite soundOnSprite;
    [SerializeField] private Sprite soundOffSprite;

    [Header("Background Music (optional)")]
    [SerializeField] private Button musicButton;
    [SerializeField] private Image musicIconImage;
    [SerializeField] private Sprite musicOnSprite;
    [SerializeField] private Sprite musicOffSprite;

    [Header("Game SFX & Voice (optional)")]
    [SerializeField] private Button sfxButton;
    [SerializeField] private Image sfxIconImage;
    [SerializeField] private Sprite sfxOnSprite;
    [SerializeField] private Sprite sfxOffSprite;

    [Header("Haptic Vibrations (optional)")]
    [SerializeField] private Button vibrationButton;
    [SerializeField] private Image vibrationIconImage;
    [SerializeField] private Sprite vibrationOnSprite;
    [SerializeField] private Sprite vibrationOffSprite;

    [Header("Master Volume (optional)")]
    [SerializeField] private Slider masterVolumeSlider;

    private bool isMuted = false;
    private const string MUTE_PREF_KEY = "GameMuted";

    private void Start()
    {
        isMuted = PlayerPrefs.GetInt(MUTE_PREF_KEY, 0) == 1;
        //ApplySoundState();

        //if (soundButton)
            //soundButton.onClick.AddListener(OnSoundToggleClicked);

        if (musicButton)
            musicButton.onClick.AddListener(OnMusicToggleClicked);

        if (sfxButton)
            sfxButton.onClick.AddListener(OnSfxToggleClicked);

        // Vibration is a separate preference from sound: players mute a game in
        // public but still want to feel the result.
        if (vibrationButton)
            vibrationButton.onClick.AddListener(OnVibrationToggleClicked);

        if (masterVolumeSlider)
        {
            // SetValueWithoutNotify so restoring the saved value doesn't fire
            // OnMasterVolumeChanged and re-save it during start-up.
            if (AudioManager.Instance)
                masterVolumeSlider.SetValueWithoutNotify(AudioManager.Instance.GetMasterVolume());

            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        }

        RefreshAllIcons();
    }

    public void OpenSettingPanel()
    {
        _settingPanel.SetActive(true);
    }

    public void CloseSettingPanel()
    {
        _settingPanel.SetActive(false);
    }

    private void OnEnable()
    {
        // The panel can be re-opened after the toggles were changed elsewhere.
        RefreshAllIcons();
    }

    // �� Combined mute (unchanged behaviour) ���������������������������������

    /*public void OnSoundToggleClicked()
    {
        isMuted = !isMuted;

        PlayerPrefs.SetInt(MUTE_PREF_KEY, isMuted ? 1 : 0);
        PlayerPrefs.Save();

        // Play click BEFORE muting so the toggle itself has audio feedback
        if (!isMuted && AudioManager.Instance)
            AudioManager.Instance.PlayOneShot("Click");

        ApplySoundState();
        RefreshAllIcons();
    }*/

    /*private void ApplySoundState()
    {
        // Update sprite
        if (soundIconImage)
            soundIconImage.sprite = isMuted ? soundOffSprite : soundOnSprite;

        // FIX-SP-01: Tell AudioManager about the new state so the whole
        // audio system is in sync. Do NOT use AudioListener.volume here �
        // it bypasses AudioManager's per-source volume and enabled checks.
        if (AudioManager.Instance)
        {
            AudioManager.Instance.SetSoundEnabled(!isMuted);
            AudioManager.Instance.SetMusicEnabled(!isMuted);
        }
    }*/

    // �� Individual switches (PRD Topic 16.F) ��������������������������������

    /// <summary>Background music only. Hook to a button's OnClick if you prefer.</summary>
    public void OnMusicToggleClicked()
    {
        if (!AudioManager.Instance) return;

        AudioManager.Instance.ToggleMusic();
        HapticManager.Selection();
        RefreshAllIcons();
    }

    /// <summary>All sound effects and announcer voice lines.</summary>
    public void OnSfxToggleClicked()
    {
        if (!AudioManager.Instance) return;

        bool turningOn = !AudioManager.Instance.IsSoundEnabled();

        AudioManager.Instance.ToggleSound();

        // Play the confirmation click only once sound is back on.
        if (turningOn) AudioManager.Instance.PlayOneShot("Click");

        HapticManager.Selection();
        RefreshAllIcons();
    }

    /// <summary>
    /// Hook this straight to a button's OnClick in the Inspector, or assign
    /// vibrationButton above and it wires itself.
    /// </summary>
    public void OnVibrationToggleClicked()
    {
        HapticManager.Toggle();   // pulses once when switched ON, as confirmation
        RefreshAllIcons();
    }

    /// <summary>Hook to a Slider's OnValueChanged (0 = silent, 1 = full).</summary>
    public void OnMasterVolumeChanged(float value)
    {
        if (!AudioManager.Instance) return;

        AudioManager.Instance.SetMasterVolume(value);
    }

    // �� Icon state ����������������������������������������������������������

    private void RefreshAllIcons()
    {
        bool musicOn = AudioManager.Instance == null || AudioManager.Instance.IsMusicEnabled();
        bool sfxOn = AudioManager.Instance == null || AudioManager.Instance.IsSoundEnabled();

        SetIcon(musicIconImage, musicOn, musicOnSprite, musicOffSprite);
        SetIcon(sfxIconImage, sfxOn, sfxOnSprite, sfxOffSprite);
        SetIcon(vibrationIconImage, HapticManager.Enabled, vibrationOnSprite, vibrationOffSprite);
        //SetIcon(soundIconImage, !isMuted, soundOnSprite, soundOffSprite);
    }

    /// <summary>Swaps a sprite only when both variants are actually assigned.</summary>
    private static void SetIcon(Image image, bool isOn, Sprite onSprite, Sprite offSprite)
    {
        if (image == null) return;

        Sprite target = isOn ? onSprite : offSprite;
        if (target != null) image.sprite = target;
    }
}
