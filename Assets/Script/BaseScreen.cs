using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace UI.Navigation
{
    [DisallowMultipleComponent]
    public class BaseScreen : MonoBehaviour
    {
        [Header("Transition")]
        [SerializeField] float fadeDuration = 0.25f;
        [SerializeField] float moveDuration = 0.35f;
        [SerializeField] Vector2 enterFrom = new Vector2(0f, -140f);
        [SerializeField] Vector2 exitTo = new Vector2(0f, 120f);
        [SerializeField] float enterScalePunch = 0.02f; // subtle pop
        [SerializeField] bool destroyOnHide = false;

        [Header("Parallax (optional)")]
        [SerializeField] RectTransform[] parallaxLayers; // near to far (0..n)
        [SerializeField] float parallaxStrength = 0.25f; // 0..1

        CanvasGroup _group;
        RectTransform _rt;
        Vector2 _homePos;
        bool _visible;
        Sequence _seq;

        public bool DestroyOnHide => destroyOnHide;
        public bool IsVisible => _visible;

        void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (!_group) _group = gameObject.AddComponent<CanvasGroup>();
            _rt = (RectTransform)transform;
            _homePos = _rt.anchoredPosition;
            PrepareHiddenInstant();
        }

        void OnDisable() => _seq?.Kill(false);

        void PrepareHiddenInstant()
        {
            _seq?.Kill(false);
            _group.alpha = 0f;
            _rt.anchoredPosition = _homePos + enterFrom;
            _rt.localScale = Vector3.one;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _visible = false;
            gameObject.SetActive(false);

            if (parallaxLayers != null)
            {
                for (int i = 0; i < parallaxLayers.Length; i++)
                {
                    var layer = parallaxLayers[i];
                    if (!layer) continue;
                    var m = (i + 1f) / parallaxLayers.Length; // 0..1 depth
                    layer.anchoredPosition = _homePos + enterFrom * parallaxStrength * m;
                }
            }
        }

        public void Show(System.Action onComplete = null)
        {
            if (_visible) { onComplete?.Invoke(); return; }

            gameObject.SetActive(true);
            _seq?.Kill(false);

            _group.interactable = false;
            _group.blocksRaycasts = false;

            _rt.anchoredPosition = _homePos + enterFrom;
            _group.alpha = 0f;
            _rt.localScale = Vector3.one;

            // parallax reset
            if (parallaxLayers != null)
            {
                for (int i = 0; i < parallaxLayers.Length; i++)
                {
                    var l = parallaxLayers[i];
                    if (!l) continue;
                    var m = (i + 1f) / parallaxLayers.Length;
                    l.anchoredPosition = _homePos + enterFrom * parallaxStrength * m;
                }
            }

            _seq = DOTween.Sequence()
                .Join(_group.DOFade(1f, fadeDuration).SetEase(Ease.OutQuad))
                .Join(_rt.DOAnchorPos(_homePos, moveDuration).SetEase(Ease.OutCubic))
                .Join(_rt.DOPunchScale(Vector3.one * enterScalePunch, moveDuration * 0.8f, 1, 0.5f))
                .OnUpdate(() =>
                {
                    // chase parallax layers toward rest as the main panel moves
                    if (parallaxLayers == null) return;
                    for (int i = 0; i < parallaxLayers.Length; i++)
                    {
                        var l = parallaxLayers[i];
                        if (!l) continue;
                        l.anchoredPosition = Vector2.Lerp(l.anchoredPosition, _homePos, 0.25f);
                    }
                })
                .OnComplete(() =>
                {
                    _group.interactable = true;
                    _group.blocksRaycasts = true;
                    _visible = true;
                    onComplete?.Invoke();
                });
        }

        public void Hide(System.Action onComplete = null)
        {
            if (!_visible) { onComplete?.Invoke(); return; }

            _seq?.Kill(false);
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _seq = DOTween.Sequence()
                .Join(_group.DOFade(0f, fadeDuration).SetEase(Ease.OutQuad))
                .Join(_rt.DOAnchorPos(_homePos + exitTo, moveDuration).SetEase(Ease.InCubic))
                .OnComplete(() =>
                {
                    _visible = false;
                    gameObject.SetActive(false);
                    onComplete?.Invoke();
                });
        }
    }
}
