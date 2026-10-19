using UnityEngine;
using DG.Tweening; // Assuming you have DOTween, if not, remove the punch line

public class NeedleController : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip tickSound;
    [Range(0.04f, 0.25f)]
    public float minTickInterval = 0.08f;

    [Header("Animation")]
    public Transform needleVisual; // The sprite part of the needle
    public float punchStrength = 15f;

    // To prevent double-clicks if physics glitches
    private float lastTickTime;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if the wheel is actually spinning fast enough to click
        // (Optional check, prevents clicks when stopped)

        // Use a Tag "Pin" or "Number" on your wheel colliders
        if (other.gameObject.CompareTag("Pin") || other.gameObject.CompareTag("Number"))
        {
            if (Time.time - lastTickTime >= minTickInterval)
            {
                PlayTick();
                lastTickTime = Time.time;
            }
        }
    }

    void PlayTick()
    {
        if (audioSource == null || tickSound == null) return;

        // Random pitch makes it sound organic/real
        audioSource.pitch = Random.Range(0.95f, 1.05f);
        audioSource.PlayOneShot(tickSound);

        // Visual "Kick" effect
        /*if (needleVisual != null)
        {
            // Reset rotation first to prevent drifting
            needleVisual.localRotation = Quaternion.identity;
            // Punch rotation (Requires DOTween)
            needleVisual.DOPunchRotation(new Vector3(0, 0, punchStrength), 0.1f, 10, 1);
        }*/
    }
}
