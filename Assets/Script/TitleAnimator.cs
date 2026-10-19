using UnityEngine;
using DG.Tweening;

public class TitleAnimator : MonoBehaviour
{
    private RectTransform rectTransform;

    [Header("Animation Settings")]
    public float slideDuration = 0.5f;
    public Ease slideEase = Ease.OutExpo; // Great for dramatic entry

    // The starting Y position (how far above the mask the text begins)
    public float startOffsetY = 100f;

    private Vector3 initialPosition;

    //public UIManager uiManager;

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        initialPosition = rectTransform.localPosition;

        // 1. Set the starting position (above the mask container)
        Vector3 startPosition = initialPosition;
        startPosition.y += startOffsetY;
        rectTransform.localPosition = startPosition;

        // 2. Start the animation
        SlideTextIn();
    }

    private void SlideTextIn()
    {
        // Kill any existing tween on this transform to allow restarting
        rectTransform.DOKill();

        // Tween the local Y position from the start position back to the initial/target position
        rectTransform.DOLocalMoveY(initialPosition.y, slideDuration)
            .SetEase(slideEase); //.OnComplete(LoadLoginSystem);
        // You can add .OnComplete(() => { /* do something else */ }); here
    }

    private void LoadLoginSystem()
    {
        //UIManager.Instance.spalshscreen.SetActive(false);
        //UIManager.Instance.SetScreenActive(AuthFlowState.Login);
    }

    // Call this function externally if you need to replay the animation later
    public void ResetAndAnimate()
    {
        // Resets the text position back to the start offset
        rectTransform.localPosition = initialPosition + new Vector3(0, startOffsetY, 0);
        SlideTextIn();
    }
}
