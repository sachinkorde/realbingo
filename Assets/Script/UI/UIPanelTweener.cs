using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Professional UI Panel Tweener with multiple animation styles
/// Features: Slide, Scale, Fade, and combination animations
/// </summary>
public class UIPanelTweener : MonoBehaviour
{
    [Header("Animation Settings")]
    //[SerializeField] private AnimationType animationType = AnimationType.SlideFromRight;
    [SerializeField] private float animationDuration = 0.0f;
    [SerializeField] private Ease openEase = Ease.OutBack;
    [SerializeField] private Ease closeEase = Ease.InBack;
    
    [Header("Slide Settings")]
    [SerializeField] private float slideDistance = 400f;
    
    [Header("Overlay Settings")]
    [SerializeField] private bool useOverlay = true;
    [SerializeField] private Image overlayImage;
    [SerializeField] private Color overlayColor = new Color(0, 0, 0, 0.6f);
    [SerializeField] private bool closeOnOverlayClick = true;

    [Header("State")]
    //[SerializeField] private bool startOpen = false;
    
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Vector2 closedPosition;
    [SerializeField] private Vector2 openPosition;
    [SerializeField] private bool isOpen = false;
    [SerializeField] private Tween currentTween;

    public bool IsOpen => isOpen;

    // Events
    public System.Action OnPanelOpened;
    public System.Action OnPanelClosed;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        // Setup overlay
        if (useOverlay && overlayImage != null)
        {
            overlayImage.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, 0);
            overlayImage.gameObject.SetActive(false);
            
            if (closeOnOverlayClick)
            {
                Button overlayButton = overlayImage.GetComponent<Button>();
                if (overlayButton == null)
                {
                    overlayButton = overlayImage.gameObject.AddComponent<Button>();
                    overlayButton.transition = Selectable.Transition.None;
                }
                overlayButton.onClick.AddListener(Close);
            }
        }
    }

    #region Public Methods

    public void Open()
    {
        if (isOpen) return;
        
        isOpen = true;
        gameObject.SetActive(true);
        currentTween?.Kill();

        PlayOpenSound();
        ShowOverlay();
        AnimateSlideOpen();
    }

    public void Close()
    {
        if (!isOpen) return;

        isOpen = false;
        currentTween?.Kill();

        PlayCloseSound();
        HideOverlay();
        AnimateSlideClose();
    }

    public void Toggle()
    {
        if (isOpen)
            Close();
        else
            Open();
    }

    #endregion

    #region Animation Methods

    private void AnimateSlideOpen()
    {
        rectTransform.anchoredPosition = closedPosition;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        
        currentTween = rectTransform
            .DOAnchorPos(openPosition, animationDuration)
            .SetEase(openEase)
            .OnComplete(() => OnPanelOpened?.Invoke());
    }

    private void AnimateSlideClose()
    {
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        
        currentTween = rectTransform
            .DOAnchorPos(closedPosition, animationDuration * 0.1f)
            .SetEase(closeEase)
            .OnComplete(() => 
            {
                gameObject.SetActive(false);
                OnPanelClosed?.Invoke();
            });
    }

    #endregion

    #region Overlay

    private void ShowOverlay()
    {
        if (!useOverlay || overlayImage == null) return;
        
        overlayImage.gameObject.SetActive(true);
        overlayImage.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, 0);
        overlayImage.DOFade(overlayColor.a, animationDuration * 0.5f);
    }

    private void HideOverlay()
    {
        if (!useOverlay || overlayImage == null) return;
        
        overlayImage.DOFade(0f, animationDuration * 0.3f)
            .OnComplete(() => overlayImage.gameObject.SetActive(false));
    }

    #endregion

    #region Audio

    private void PlayOpenSound()
    {
        AudioManager.Instance?.PlayOneShot("PanelOpen");
        HapticManager.Light();
    }

    private void PlayCloseSound()
    {
        AudioManager.Instance?.PlayOneShot("PanelClose");
        HapticManager.Selection();
    }

    #endregion

    private void OnDestroy()
    {
        currentTween?.Kill();
    }
}
