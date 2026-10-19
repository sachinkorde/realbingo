using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Press animation + vibration for any UI Selectable (Button, Toggle, �).
///
/// On finger-down the control squashes slightly and the phone taps; on
/// finger-up it springs back with a small overshoot, so every control in the
/// game feels physical instead of static.
///
/// It is added automatically to every Button / Toggle at runtime by
/// <see cref="UIFeedbackInstaller"/>, so nothing has to be wired by hand. You
/// can still add it manually to a control first to give that one control
/// different settings (e.g. a Heavy haptic on the CASH OUT button) � the
/// installer never overwrites a component that is already there.
///
/// Notes:
///  � Only this component's own tween is killed on release, never
///    transform.DOKill(), so other scripts tweening the same object
///    (ChipButton, UIManager punch effects) are left alone.
///  � A control that is not interactable gives no animation and no vibration,
///    which is exactly the feedback the player needs: "this is not tappable".
/// </summary>
[DisallowMultipleComponent]
public class ButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Press Animation")]
    [Tooltip("Scale/spring the control on press. Turn off for controls that are already animated elsewhere.")]
    public bool animateOnPress = true;

    [Tooltip("Transform to scale. Leave empty to use this object's own transform.")]
    public Transform target;

    [Range(0.7f, 1f)]
    [Tooltip("How far the control squashes down while held.")]
    public float pressedScale = 0.93f;

    [Tooltip("Seconds to reach the pressed size.")]
    public float pressDuration = 0.07f;

    [Tooltip("Seconds to spring back to normal size.")]
    public float releaseDuration = 0.18f;

    [Header("Vibration")]
    [Tooltip("Vibrate the phone when this control is pressed.")]
    public bool hapticOnPress = true;

    [Tooltip("Strength of the vibration. 'Light' suits normal buttons, 'Selection' suits chips/toggles.")]
    public HapticType hapticType = HapticType.Light;

    private Selectable selectable;
    private Transform scaleTarget;
    private Vector3 baseScale = Vector3.one;
    private Tween activeTween;
    private bool isPressed;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        scaleTarget = target != null ? target : transform;
        baseScale = scaleTarget.localScale;
    }

    private void OnDisable()
    {
        // A control hidden mid-press must not come back squashed next time.
        RestoreInstantly();
    }

    private void OnDestroy()
    {
        activeTween?.Kill();
        activeTween = null;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsInteractable()) return;

        isPressed = true;

        if (hapticOnPress) HapticManager.Play(hapticType);

        if (!animateOnPress || scaleTarget == null) return;

        activeTween?.Kill();
        activeTween = scaleTarget
            .DOScale(baseScale * pressedScale, pressDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)    // still animates if the game is paused
            .SetLink(gameObject);
    }

    public void OnPointerUp(PointerEventData eventData) => Release();

    public void OnPointerExit(PointerEventData eventData) => Release();

    private void Release()
    {
        if (!isPressed) return;
        isPressed = false;

        if (!animateOnPress || scaleTarget == null) return;

        activeTween?.Kill();
        activeTween = scaleTarget
            .DOScale(baseScale, releaseDuration)
            .SetEase(Ease.OutBack)
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private void RestoreInstantly()
    {
        isPressed = false;
        activeTween?.Kill();
        activeTween = null;

        if (scaleTarget != null) scaleTarget.localScale = baseScale;
    }

    private bool IsInteractable()
    {
        return selectable == null || selectable.IsInteractable();
    }

    /// <summary>
    /// Re-reads the control's current scale as the "normal" size. Call this if
    /// a layout script resizes the control after start-up.
    /// </summary>
    public void RecaptureBaseScale()
    {
        if (scaleTarget == null) scaleTarget = target != null ? target : transform;
        baseScale = scaleTarget.localScale;
    }
}
