using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach this to ANY Button to automatically play a UI click sound.
///
/// How to use:
///   1. Add this component to any Button GameObject in the Inspector.
///   2. Make sure AudioManager has a clip registered under that key
///      (or whatever you set in soundKey).
///   3. Done — every click on that button will play the sound.
///
/// The sound uses the uiSource AudioSource in AudioManager because
/// AudioManager.IsUIKey("Click") returns true.
///
/// FIX-BS-01: Added configurable soundKey so different buttons can have
///            different sounds (e.g. "Click", "ChipSelect", "Error").
/// FIX-BS-02: Null guard on AudioManager so no error in scenes without it.
/// FIX-BS-03: Registers listener in Start, not Awake, so Button is ready.
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonSound : MonoBehaviour
{
    [Tooltip("Key registered in AudioManager's clip list.")]
    public string soundKey = "UIButtonClick";

    private Button btn;

    private void Start()
    {
        btn = GetComponent<Button>();
        btn.onClick.AddListener(PlayClick);
    }

    private void OnDestroy()
    {
        if (btn != null)
            btn.onClick.RemoveListener(PlayClick);
    }

    private void PlayClick()
    {
        if (AudioManager.Instance == null) return;

        // Try the assigned key first; fall back to a key that is actually
        // registered. The old fallback was "Click", which is not in the clip
        // list either â€” so a wrong key here used to mean silence, twice over.
        string key = AudioManager.Instance.HasClip(soundKey) ? soundKey : "UIButtonClick";
        AudioManager.Instance.PlayOneShot(key);
    }
}
