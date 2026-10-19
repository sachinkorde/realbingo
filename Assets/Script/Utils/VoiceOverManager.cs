using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Announcer voice-over slangs (PRD Topic 7.1).
///
/// At the end of a round the game calls PlayWin() / PlayLose() / PlayJackpot()
/// and this picks one clip at random from the matching pool.
///
/// Selection uses a SHUFFLE BAG, not raw Random.Range: a plain random pick will
/// happily say "Big Win!" three rounds running, which sounds broken. A shuffle
/// bag plays every clip in the pool once (in random order) before any clip can
/// repeat, so the pool feels much larger than it is.
///
/// Setup:
///   1. Put this component on the same GameObject as AudioManager (or any
///      object that survives scene loads).
///   2. Drop the win clips into Win Clips and the loss clips into Lose Clips.
///      The PRD lists 18 win and 16 loss lines; any number works.
///   3. Optionally assign Jackpot Clip � the PRD makes this a hardcoded
///      priority asset played only when the wheel stops on 'J'.
///
/// With no clips assigned every call is a silent no-op, so it is safe to wire
/// the code first and add the audio later.
///
/// Respects the game's sound toggle through AudioManager.IsSoundEnabled().
/// </summary>
public class VoiceOverManager : MonoBehaviour
{
    public static VoiceOverManager Instance { get; private set; }

    [Header("Clip Pools")]
    [Tooltip("Win slangs: \"Big Win!\", \"Congratulations!\", \"You're on a roll!\" ...")]
    public List<AudioClip> winClips = new List<AudioClip>();

    [Tooltip("Loss slangs: \"Try again!\", \"Close One!\", \"Next Time!\" ...")]
    public List<AudioClip> loseClips = new List<AudioClip>();

    [Tooltip("Played instead of a win slang when the wheel stops on the 'J' slot.")]
    public AudioClip jackpotClip;

    [Header("Playback")]
    [Range(0f, 1f)]
    [Tooltip("Voice level relative to the master volume. Voice should sit above SFX.")]
    public float voiceVolume = 1f;

    [Tooltip("Seconds to wait before the line plays, so it lands after the win chime instead of on top of it.")]
    public float announceDelay = 0.35f;

    [Tooltip("Stop any line already playing before starting a new one. Two announcers talking over each other sounds broken.")]
    public bool interruptPrevious = true;

    private AudioSource voiceSource;
    private readonly List<int> winBag = new List<int>();
    private readonly List<int> loseBag = new List<int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        voiceSource = gameObject.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
        voiceSource.spatialBlend = 0f;
        voiceSource.priority = 32;          // above SFX: the announcer must not get culled
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // �� Public API ������������������������������������������������������������

    public void PlayWin() => PlayFromPool(winClips, winBag);

    public void PlayLose() => PlayFromPool(loseClips, loseBag);

    /// <summary>Hardcoded priority line for a 'J' result. Falls back to a win slang.</summary>
    public void PlayJackpot()
    {
        if (jackpotClip != null) PlayClip(jackpotClip);
        else PlayWin();
    }

    /// <summary>Stops the current announcement immediately.</summary>
    public void StopVoice()
    {
        if (voiceSource != null && voiceSource.isPlaying) voiceSource.Stop();
    }

    // �� Internals ������������������������������������������������������������

    private void PlayFromPool(List<AudioClip> pool, List<int> bag)
    {
        if (pool == null || pool.Count == 0) return;

        int index = DrawFromBag(pool.Count, bag);
        if (index < 0 || index >= pool.Count) return;

        PlayClip(pool[index]);
    }

    /// <summary>
    /// Returns an index that has not been used since the bag was last refilled.
    /// Refills and reshuffles once every index has been handed out.
    /// </summary>
    private int DrawFromBag(int poolSize, List<int> bag)
    {
        // Pool resized in the Inspector between rounds? Rebuild rather than
        // hand back an index that no longer exists.
        if (bag.Count == 0)
        {
            for (int i = 0; i < poolSize; i++) bag.Add(i);

            // Fisher-Yates
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
        }

        int last = bag.Count - 1;
        int value = bag[last];
        bag.RemoveAt(last);

        return value < poolSize ? value : 0;
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null || voiceSource == null) return;

        // One toggle covers "Game SFX & Voice" in the settings panel (PRD 16.F.2).
        if (AudioManager.Instance != null && !AudioManager.Instance.IsSoundEnabled()) return;

        if (interruptPrevious) StopVoice();

        float master = AudioManager.Instance != null ? AudioManager.Instance.GetMasterVolume() : 1f;

        voiceSource.clip = clip;
        voiceSource.volume = voiceVolume * master;

        if (announceDelay > 0f) voiceSource.PlayDelayed(announceDelay);
        else voiceSource.Play();
    }
}
