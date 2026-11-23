using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using DG.Tweening;

[System.Serializable]
public struct SoundClip
{
    public string key;
    public AudioClip clip;
    [Range(0, 1)] public float volume;
    [Range(0.5f, 2f)] public float pitch;
    public bool loop;
}

/// <summary>
/// Complete Audio Manager for game sounds, music, and effects
/// Features: Fade in/out, pitch control, 3D positioning ready
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource spinLoopSource;
    [SerializeField] private AudioSource uiSource;
    [SerializeField] private AudioSource ambientSource;

    [Header("Sound Clips")]
    public List<SoundClip> clips = new List<SoundClip>();
    
    [Header("Settings")]
    [SerializeField] private float masterVolume = 1f;
    [SerializeField] private float musicVolume = 0.7f;
    [SerializeField] private float sfxVolume = 1f;
    [SerializeField] private bool soundEnabled = true;
    [SerializeField] private bool musicEnabled = true;

    [Header("Background Music")]
    [Tooltip("Scenes that own a looping BGM. Loading any scene outside this list " +
             "silences the music, so the menu stays quiet on its own instead of " +
             "relying on every exit path to remember.")]
    [SerializeField] private string[] scenesWithMusic = { "GameScene" };

    private Dictionary<string, SoundClip> clipDict = new Dictionary<string, SoundClip>();
    private Dictionary<string, AudioSource> activeSources = new Dictionary<string, AudioSource>();
    // Keys already complained about, so an unregistered clip is reported
    // once instead of on every frame a loop or a tick asks for it.
    private readonly HashSet<string> missingClipsReported = new HashSet<string>();
    private AudioSource timerTickSource;
    private float lastTimerTickTime = -999f;
    private int lastSecondsLeft = -1;

    // PRD Topic 7.3 "Dipping Threshold Rule": BGM attenuates while the wheel
    // spins and restores when the win/loss announcement ends. Kept as a separate
    // multiplier so it composes with musicVolume and masterVolume instead of
    // fighting them.
    private float musicDuckFactor = 1f;
    private Tween musicDuckTween;
    private Tween musicRestoreDelayTween;

    private const string SOUND_ENABLED_KEY = "SoundEnabled";
private const string MUSIC_ENABLED_KEY = "MusicEnabled";
    private const string MASTER_VOLUME_KEY = "MasterVolume";
    private const string MUSIC_VOLUME_KEY = "MusicVolume";
    private const string SFX_VOLUME_KEY = "SfxVolume";

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Build dictionary for fast lookup
        foreach (var c in clips)
        {
            if (!string.IsNullOrEmpty(c.key) && !clipDict.ContainsKey(c.key))
            {
                clipDict.Add(c.key, c);
            }
        }

        // Load saved settings
        LoadSettings();

        // A serialised component saved before this field existed deserialises it
        // as empty, and the C# initialiser above does not apply to it — so fall
        // back here rather than silently treating gameplay as a quiet scene.
        if (scenesWithMusic == null || scenesWithMusic.Length == 0)
        {
            scenesWithMusic = new[] { "GameScene" };
        }

        // This object outlives every scene, so its music would too: the game
        // BGM used to keep playing over the menu after logout.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        bool sceneHasMusic = scenesWithMusic != null
                             && System.Array.IndexOf(scenesWithMusic, scene.name) >= 0;

        if (!sceneHasMusic) ResetPlayback();
    }

    /// <summary>
    /// Silences everything that loops — music, the spin loop, the countdown tick
    /// and ambience — and clears any ducking left mid-fade. One-shot sounds
    /// (button clicks and the rest) are untouched, and no saved setting changes:
    /// this resets playback, not the player's preferences.
    /// </summary>
    public void ResetPlayback()
    {
        musicDuckTween?.Kill();
        musicDuckTween = null;
        musicRestoreDelayTween?.Kill();
        musicRestoreDelayTween = null;
        musicDuckFactor = 1f;

        if (musicSource != null)
        {
            musicSource.DOKill();          // drop any fade mid-flight
            musicSource.Stop();
            musicSource.clip = null;       // so EnsureLoopPlaying restarts cleanly
        }

        if (spinLoopSource != null)
        {
            spinLoopSource.DOKill();
            spinLoopSource.Stop();
        }

        timerTickSource?.Stop();
        ambientSource?.Stop();

        lastSecondsLeft = -1;
        lastTimerTickTime = -999f;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            // C#'s ?. operator bypasses Unity's == null override, so a static
            // left pointing at a destroyed object gets called into.
            Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    /// <summary>
    /// Clip lookup for the play paths: a plain dictionary hit, except it says
    /// something the first time a key is missing. A silent miss is how "Win",
    /// "Lose" and "BetClosed" went unnoticed for so long — those call sites read
    /// perfectly and simply never make a sound.
    /// </summary>
    private bool TryGetClip(string key, out SoundClip clip)
    {
        if (clipDict.TryGetValue(key, out clip)) return true;

        if (!string.IsNullOrEmpty(key) && missingClipsReported.Add(key))
        {
            Debug.LogWarning($"[AudioManager] No clip registered for \"{key}\" — nothing will " +
                             "play. Add it to the Clips list on the AudioManager object.");
        }

        return false;
    }

    #region Settings

    private void LoadSettings()
    {
        soundEnabled = PlayerPrefs.GetInt(SOUND_ENABLED_KEY, 1) == 1;
        musicEnabled = PlayerPrefs.GetInt(MUSIC_ENABLED_KEY, 1) == 1;
        masterVolume = PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 1f);

        // Music and effects are separate sliders in Settings, so they persist
        // separately too. The serialised values are the defaults for a device
        // that has never saved one.
        musicVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, musicVolume);
        sfxVolume = PlayerPrefs.GetFloat(SFX_VOLUME_KEY, sfxVolume);
        
        ApplySettings();
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetInt(SOUND_ENABLED_KEY, soundEnabled ? 1 : 0);
        PlayerPrefs.SetInt(MUSIC_ENABLED_KEY, musicEnabled ? 1 : 0);
        PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, masterVolume);
        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, musicVolume);
        PlayerPrefs.SetFloat(SFX_VOLUME_KEY, sfxVolume);
        PlayerPrefs.Save();
    }

    private void ApplySettings()
    {
        if (musicSource != null)
        {
            musicSource.mute = !musicEnabled;

            // Don't stomp an in-flight duck/restore fade with a settings change.
            if (musicDuckTween == null || !musicDuckTween.IsActive())
                musicSource.volume = GetTargetMusicVolume();
        }

        if (sfxSource != null) sfxSource.mute = !soundEnabled;
        if (spinLoopSource != null) spinLoopSource.mute = !soundEnabled;
        if (timerTickSource != null) timerTickSource.mute = !soundEnabled;
        if (uiSource != null) uiSource.mute = !soundEnabled;
    }

    public void SetMasterVolume(float volume)
    {
        SetMasterVolume(volume, true);
    }

    /// <summary>
    /// Volume with control over whether it is written to disk.
    ///
    /// A Slider fires OnValueChanged every frame while the finger is moving, and
    /// PlayerPrefs.Save() is a real disk write — saving on every frame of a drag
    /// stutters badly on mobile. Pass persist:false while dragging and call
    /// SaveSettings() once when the drag settles.
    /// </summary>
    public void SetMasterVolume(float volume, bool persist)
    {
        masterVolume = Mathf.Clamp01(volume);
        ApplySettings();

        if (persist) SaveSettings();
    }

    /// <summary>
    /// Background music level on its own, for the Music slider. Pass
    /// persist:false while a finger is dragging and call SaveSettings() once it
    /// settles — the same rule as SetMasterVolume.
    /// </summary>
    public void SetMusicVolume(float volume, bool persist = true)
    {
        musicVolume = Mathf.Clamp01(volume);
        ApplySettings();

        if (persist) SaveSettings();
    }

    /// <summary>Effects and announcer level on its own, for the Sound Fx slider.</summary>
    public void SetSfxVolume(float volume, bool persist = true)
    {
        sfxVolume = Mathf.Clamp01(volume);
        ApplySettings();

        if (persist) SaveSettings();
    }

    public float GetMusicVolume() => musicVolume;
    public float GetSfxVolume() => sfxVolume;

    public void SetMusicEnabled(bool enabled)
    {
        musicEnabled = enabled;
        ApplySettings();
        SaveSettings();
    }

    public void SetSoundEnabled(bool enabled)
    {
        soundEnabled = enabled;
        ApplySettings();
        SaveSettings();
    }

    public void ToggleMusic()
    {
        SetMusicEnabled(!musicEnabled);
    }

    public void ToggleSound()
    {
        SetSoundEnabled(!soundEnabled);
    }

    public bool IsMusicEnabled() => musicEnabled;
    public bool IsSoundEnabled() => soundEnabled;
    public float GetMasterVolume() => masterVolume;

    #endregion

    #region Play Methods

    /// <summary>
    /// Play a looping or long sound (music, ambient, spin loop)
    /// </summary>
    public void Play(string key)
    {
        if (!TryGetClip(key, out SoundClip clip)) return;

        AudioSource source = GetSourceForKey(key);
        if (source == null) return;

        bool isMusic = IsMusicKey(key);
        if (!isMusic && !soundEnabled) return;

        if (source == spinLoopSource)
        {
            source.DOKill();
        }

        source.clip = clip.clip;
        source.loop = key == "SpinLoop" || isMusic || clip.loop;
        source.volume = clip.volume * (isMusic ? musicVolume : sfxVolume) * masterVolume;
        source.pitch = clip.pitch > 0 ? clip.pitch : 1f;
        source.Play();
    }

    /// <summary>
    /// Play a one-shot sound effect
    /// </summary>
    public void PlayOneShot(string key)
    {
        if (!soundEnabled) return;
        if (!TryGetClip(key, out SoundClip clip)) return;

        AudioSource source = IsUIKey(key) ? uiSource : sfxSource;
        if (source != null && clip.clip != null)
        {
            float volume = clip.volume * sfxVolume * masterVolume;
            source.pitch = clip.pitch > 0 ? clip.pitch : 1f;
            source.PlayOneShot(clip.clip, volume);
        }
    }

    /// <summary>
    /// Play with random pitch variation for natural sound
    /// </summary>
    public void PlayOneShotRandomPitch(string key, float pitchVariation = 0.1f)
    {
        if (!soundEnabled) return;
        if (!TryGetClip(key, out SoundClip clip)) return;

        AudioSource source = IsUIKey(key) ? uiSource : sfxSource;
        if (source != null && clip.clip != null)
        {
            float basePitch = clip.pitch > 0 ? clip.pitch : 1f;
            source.pitch = basePitch + Random.Range(-pitchVariation, pitchVariation);
            source.PlayOneShot(clip.clip, clip.volume * sfxVolume * masterVolume);
        }
    }

    /// <summary>
    /// Stop a playing sound
    /// </summary>
    public void Stop(string key)
    {
        AudioSource source = GetSourceForKey(key);
        if (source != null)
        {
            source.Stop();
        }
    }

    /// <summary>
    /// Stop all sounds
    /// </summary>
    public void StopAll()
    {
        musicSource?.Stop();
        sfxSource?.Stop();
        spinLoopSource?.Stop();
        timerTickSource?.Stop();
        uiSource?.Stop();
        ambientSource?.Stop();
    }

    #endregion

    #region Music Ducking (PRD Topic 7.3)

    /// <summary>
    /// Attenuate the background music while something more important is playing
    /// (the wheel spin). toPercent 0.5 = the PRD's "reduce by exactly 50%".
    /// </summary>
    public void DuckMusic(float toPercent = 0.5f, float duration = 0.35f)
    {
        musicRestoreDelayTween?.Kill();
        musicRestoreDelayTween = null;

        musicDuckFactor = Mathf.Clamp01(toPercent);
        FadeMusicToTarget(duration);
    }

    /// <summary>Bring the background music back to its full configured level.</summary>
    public void RestoreMusic(float duration = 0.6f)
    {
        musicRestoreDelayTween?.Kill();
        musicRestoreDelayTween = null;

        musicDuckFactor = 1f;
        FadeMusicToTarget(duration);
    }

    /// <summary>
    /// Restore after a delay � used so the music comes back when the win/loss
    /// announcement finishes rather than the instant the wheel stops.
    /// </summary>
    public void RestoreMusicAfter(float delaySeconds, float duration = 0.6f)
    {
        musicRestoreDelayTween?.Kill();
        musicRestoreDelayTween = DOVirtual.DelayedCall(
            Mathf.Max(0f, delaySeconds), () => RestoreMusic(duration))
            .SetUpdate(true);
    }

    /// <summary>Current duck level: 1 = full volume, 0.5 = halved.</summary>
    public float GetMusicDuckFactor() => musicDuckFactor;

    private float GetTargetMusicVolume() => musicVolume * masterVolume * musicDuckFactor;

    private void FadeMusicToTarget(float duration)
    {
        if (musicSource == null) return;

        musicDuckTween?.Kill();

        float target = GetTargetMusicVolume();

        if (duration <= 0f || !musicSource.isPlaying)
        {
            musicSource.volume = target;
            return;
        }

        musicDuckTween = musicSource.DOFade(target, duration)
                                    .SetEase(Ease.OutQuad)
                                    .SetUpdate(true)
                                    .SetLink(gameObject);
    }

    #endregion

    #region Spin Audio Control

    public void SetSpinPitch(float pitch)
    {
        if (spinLoopSource != null)
        {
            spinLoopSource.pitch = Mathf.Clamp(pitch, 0.3f, 2f);
        }
    }

    public void SetSpinVolume(float volume)
    {
        if (spinLoopSource != null)
        {
            spinLoopSource.volume = soundEnabled ? volume * sfxVolume * masterVolume : 0f;
        }
    }

    public void FadeOutSpin(float duration)
    {
        if (spinLoopSource != null)
        {
            spinLoopSource.DOKill();

            if (duration <= 0f || !spinLoopSource.isPlaying)
            {
                spinLoopSource.volume = 0f;
                spinLoopSource.Stop();
                return;
            }

            spinLoopSource.DOFade(0f, duration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => spinLoopSource.Stop());
        }
    }

    #endregion

    #region Timer Audio Control

    public void PlayCountdownTick(string key, int secondsLeft, int warningStartSeconds)
    {
        if (!soundEnabled) return;
        if (!TryGetClip(key, out SoundClip clip) || clip.clip == null) return;

        if (secondsLeft == lastSecondsLeft) return;
        lastSecondsLeft = secondsLeft;

        const float minInterval = 0.75f;
if (Time.unscaledTime - lastTimerTickTime < minInterval) return;

        AudioSource source = GetTimerTickSource();
        if (source == null) return;

        int warningRange = Mathf.Max(1, warningStartSeconds);
        float urgency = 1f - Mathf.Clamp01((secondsLeft - 1f) / warningRange);

        source.Stop();
        source.clip = clip.clip;
        source.loop = false;
        source.pitch = Mathf.Lerp(0.9f, 1.12f, urgency);
        source.volume = Mathf.Lerp(0.45f, 0.85f, urgency) * sfxVolume * masterVolume;
        source.Play();

        lastTimerTickTime = Time.unscaledTime;
    }

    private AudioSource GetTimerTickSource()
    {
        if (timerTickSource != null) return timerTickSource;

        timerTickSource = gameObject.AddComponent<AudioSource>();
        timerTickSource.playOnAwake = false;
        timerTickSource.loop = false;
        timerTickSource.spatialBlend = 0f;
        timerTickSource.mute = !soundEnabled;

        return timerTickSource;
    }

    #endregion

    #region Fade Methods

    public void FadeInMusic(string key, float duration = 1f)
    {
        if (!musicEnabled) return;
        if (!TryGetClip(key, out SoundClip clip)) return;

        if (musicSource != null)
        {
            musicSource.clip = clip.clip;
            musicSource.loop = true;
            musicSource.volume = 0;
            musicSource.Play();
            musicSource.DOFade(clip.volume * musicVolume * masterVolume, duration);
        }
    }

    public void FadeOutMusic(float duration = 1f)
    {
        if (musicSource != null && musicSource.isPlaying)
        {
            musicSource.DOFade(0f, duration).OnComplete(() => musicSource.Stop());
        }
    }

    public void CrossfadeMusic(string newKey, float duration = 1f)
    {
        FadeOutMusic(duration * 0.5f);
        DOVirtual.DelayedCall(duration * 0.5f, () => FadeInMusic(newKey, duration * 0.5f));
    }

    public void EnsureLoopPlaying(string key)
    {
        if (!TryGetClip(key, out SoundClip clip) || clip.clip == null) return;

        AudioSource source = GetSourceForKey(key);
        if (source == null) return;

        if (source.clip != clip.clip || !source.isPlaying)
        {
            Play(key);
        }

        source.loop = true;
    }

    #endregion

    #region Helper Methods

    private AudioSource GetSourceForKey(string key)
    {
        if (key == "SpinLoop") return spinLoopSource;
        if (key.StartsWith("BGM") || key.Contains("Music")) return musicSource;
        if (key.StartsWith("Ambient")) return ambientSource;
        if (IsUIKey(key)) return uiSource;
        return sfxSource;
    }

    private bool IsMusicKey(string key)
    {
        return key.StartsWith("BGM") || key.Contains("Music") || key.StartsWith("Ambient") || key.Contains("Bg");
    }

    private bool IsUIKey(string key)
    {
        return key.StartsWith("UI") || key.Contains("Button") || key.Contains("Click") || 
               key.Contains("Panel") || key == "Error" || key == "Success";
    }

    /// <summary>
    /// Check if a clip exists
    /// </summary>
    public bool HasClip(string key)
    {
        return clipDict.ContainsKey(key);
    }

    public AudioClip GetClip(string key)
    {
        return clipDict.TryGetValue(key, out SoundClip clip) ? clip.clip : null;
    }

    /// <summary>
    /// Get clip duration
    /// </summary>
    public float GetClipDuration(string key)
    {
        if (clipDict.TryGetValue(key, out SoundClip clip) && clip.clip != null)
        {
            return clip.clip.length;
        }
        return 0f;
    }

    #endregion

    #region Default Sound Keys Reference
    /*
    Standard sound keys used in the game:
    
    Music:
    - BGM_Game: Main game scene background music
    - BGM_Menu: Menu/login scene background music
    
    UI Sounds:
    - UIButtonClick: General button click
    - Click: Alternative click sound
    - Error: Error/invalid action
    - Success: Success confirmation
    - PanelOpen: Panel opening
    - PanelClose: Panel closing
    
    Game Sounds:
    - SpinLoop: Wheel spinning loop
    - TimerTick: Timer countdown tick
    - ChipPlace: Chip placed on board
    - ChipSelect: Chip selection
    - BetClosed: Betting period closed
    - Win: Winning sound
    - Lose: Losing sound
    - CashCount: Money counting
    - Jackpot: Big win celebration
    
    Notifications:
    - Notification: General notification
    - Message: New message
    */
    #endregion
}
