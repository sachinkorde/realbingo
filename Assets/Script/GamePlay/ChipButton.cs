using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

/// <summary>
/// Professional chip button with glow effect, pulsing lights, and clear selection feedback
/// </summary>
public class ChipButton : MonoBehaviour
{
    [Header("Chip Settings")]
    public long chipValue;
    public Button myButton;

    [Header("Visual References")]
    public Transform contentTransform;
    public Image chipImage;
    
    [Header("Glow (PRD Topic 5.4)")]
    [Tooltip("Selected chip pulses a halo, so the active chip reads at a glance.")]
    public bool useGlow = true;
    [Tooltip("Assign to pulse your own halo image. Left empty, the chip's own light object is pulsed - its Image if it has one, otherwise its Light2D.")]
    public Image glowImage;
    [Range(0f, 1f)] public float glowMinAlpha = 0.35f;
    [Range(0f, 1f)] public float glowMaxAlpha = 0.9f;
    [Tooltip("Seconds for one half of the pulse.")]
    public float glowPulseDuration = 0.7f;
    
    //[Header("Selection Ring")]
    //[Tooltip("Ring/border that appears around selected chip")]
    //public Image selectionRing;
    //public Color selectedRingColor = new Color(1f, 0.9f, 0f, 1f);
    
    //[Header("Light Rays")]
    //[Tooltip("Light rays behind chip that rotate when selected")]
    //public Transform lightRaysTransform;
    //public float lightRaysRotationSpeed = 30f;
    
    [Header("Animation Settings")]
    public float selectScale = 1.15f;
    public float deselectScale = 1f;
    public float scaleAnimDuration = 0.25f;
    public Ease selectEase = Ease.OutBack;
    public Ease deselectEase = Ease.OutQuad;

    [Header("Float / Bob (PRD Topic 6.1)")]
    [Tooltip("Selected chip gently bobs up and down like a gaming power-up item.")]
    public bool enableFloat = true;
    [Tooltip("How far the chip rises and falls, in local units.")]
    public float floatAmplitude = 8f;
    [Tooltip("Seconds for one half of the bob (up OR down).")]
    public float floatDuration = 0.75f;
    [Tooltip("Optional explicit object to bob. Leave empty and the chip works it out automatically (see note below).")]
    public RectTransform floatTarget;

    [Header("Dim State (PRD Topic 5.4)")]
    [Tooltip("Non-selected chips tint down so the active chip clearly stands out.")]
    public bool useDimState = true;
    [Tooltip("Tint applied to unselected chips. Set to white to disable tinting without turning the feature off.")]
    public Color dimColor = new Color(0.72f, 0.72f, 0.72f, 1f);
    public float dimFadeDuration = 0.2f;

    [Header("Audio")]
    public string selectSoundKey = "ChipSelect";
    public string amountClickSoundKey = "UIButtonClick";

    public static event System.Action<long> OnChipSelected;

    private bool isSelected;
    private Tween scaleTween;
    private Tween floatTween;
    private Tween dimTween;
    private Vector3 contentBasePos;
    private bool baseCaptured;
    private RectTransform[] floatChildren;
    private Vector2[] floatBasePositions;
    private bool floatTargetsResolved;
    private Tween glowTween;
    private Color glowBaseColor = Color.white;
    private Light2D glowLight;
    private float glowBaseIntensity = 1f;
    private bool glowResolved;
    //private Tween ringTween;
    //private Coroutine lightRaysCoroutine;
    private Color originalChipColor = Color.white;

    public GameObject lightObject;

    private void Start()
    {
        if (myButton == null) 
        { 
            myButton = GetComponent<Button>();
        }

        // 9 of the 10 chips in the scene never had this reference assigned, and
        // every tint path is guarded by "chipImage != null" — so the dim state
        // silently did nothing on them. Resolve it from the chip's own art
        // instead of relying on the Inspector.
        if (chipImage == null) chipImage = ResolveChipImage();

        if (chipImage != null)
        {
            // Captured before the dim below overwrites it: Select() used to
            // brighten to hardcoded white, which washes out a coloured chip.
            originalChipColor = chipImage.color;
        }

        CaptureContentBasePosition();

        // Start dimmed: nothing is selected yet, so no chip should look active.
        if (useDimState && chipImage != null) chipImage.color = dimColor;

        // Initialize glow as hidden
        /*if (glowImage != null)
        {
            glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.gameObject.SetActive(false);
        }

        // Initialize selection ring as hidden
        if (selectionRing != null)
        {
            selectionRing.gameObject.SetActive(false);
        }

        // Initialize light rays as hidden
        if (lightRaysTransform != null)
        {
            lightRaysTransform.gameObject.SetActive(false);
        }*/

        myButton.onClick.AddListener(OnChipClicked);
        OnChipSelected += HandleSelection;
    }

    private void OnDestroy()
    {
        OnChipSelected -= HandleSelection;
        KillAllTweens();
    }

    private void OnChipClicked()
    {
        // Set the chip value in bet manager
        if (GameManager.Instance && GameManager.Instance.betManager)
        {
            GameManager.Instance.betManager.currentChipValue = chipValue;
        }

        // Play selection sound
        if (AudioManager.Instance)
        {
            string soundKey = string.IsNullOrWhiteSpace(amountClickSoundKey) ? "UIButtonClick" : amountClickSoundKey;
            AudioManager.Instance.PlayOneShot(soundKey);
        }

        // Light "detent" tap — picking a chip is a selection, not a commitment.
        HapticManager.Selection();

        // Invoke event to notify all chips
        OnChipSelected?.Invoke(chipValue);
    }

    private void HandleSelection(long selectedValue)
    {
        bool shouldBeSelected = (chipValue == selectedValue);

        if (shouldBeSelected && !isSelected)
        {
            Select();
        }
        else if (!shouldBeSelected && isSelected)
        {
            Deselect();
        }
    }

    private void Select()
    {
        isSelected = true;
        KillAllTweens();
        if (lightObject != null) lightObject.SetActive(true);
        // Scale up with bounce
        if (contentTransform != null)
        {
            scaleTween = contentTransform
                .DOScale(selectScale, scaleAnimDuration)
                .SetEase(selectEase);
        }

        // Enable and animate glow
        /*if (glowImage != null)
        {
            glowImage.gameObject.SetActive(true);
            
            // Fade in glow
            glowImage.DOFade(glowMaxAlpha, 0.2f);
            
            // Start pulsing
            StartGlowPulse();
        }*/

        // Enable selection ring with animation
        /*if (selectionRing != null)
        {
            selectionRing.gameObject.SetActive(true);
            selectionRing.color = new Color(selectedRingColor.r, selectedRingColor.g, selectedRingColor.b, 0f);
            selectionRing.transform.localScale = Vector3.one * 0.8f;
            
            Sequence ringSeq = DOTween.Sequence();
            ringSeq.Append(selectionRing.DOFade(1f, 0.2f));
            ringSeq.Join(selectionRing.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack));
            ringTween = ringSeq;
        }*/

        // Enable and start rotating light rays
        /*if (lightRaysTransform != null)
        {
            lightRaysTransform.gameObject.SetActive(true);
            lightRaysTransform.localScale = Vector3.zero;
            lightRaysTransform.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
            
            if (lightRaysCoroutine != null)
                StopCoroutine(lightRaysCoroutine);
            lightRaysCoroutine = StartCoroutine(RotateLightRays());
        }*/

        StartFloat();

        // Brighten chip slightly
        if (chipImage != null)
        {
            dimTween?.Kill();
            dimTween = chipImage.DOColor(originalChipColor, dimFadeDuration);
        }

        StartGlow();
    }

    private void Deselect()
    {
        isSelected = false;
        KillAllTweens();
        if (lightObject != null) lightObject.SetActive(false);
        // Scale down
        if (contentTransform != null)
        {
            scaleTween = contentTransform
                .DOScale(deselectScale, scaleAnimDuration)
                .SetEase(deselectEase);
        }

        // Fade out glow
        /*if (glowImage != null)
        {
            glowImage.DOFade(0f, 0.2f).OnComplete(() => 
            {
                glowImage.gameObject.SetActive(false);
            });
        }*/

        // Hide selection ring
        /*if (selectionRing != null)
        {
            Sequence ringSeq = DOTween.Sequence();
            ringSeq.Append(selectionRing.DOFade(0f, 0.15f));
            ringSeq.Join(selectionRing.transform.DOScale(0.8f, 0.15f));
            ringSeq.OnComplete(() => selectionRing.gameObject.SetActive(false));
            ringTween = ringSeq;
        }*/

        // Hide light rays
        /*if (lightRaysTransform != null)
        {
            if (lightRaysCoroutine != null)
            {
                StopCoroutine(lightRaysCoroutine);
                lightRaysCoroutine = null;
            }
            
            lightRaysTransform.DOScale(0f, 0.2f).OnComplete(() => 
            {
                lightRaysTransform.gameObject.SetActive(false);
            });
        }*/

        StopFloat();
        StopGlow();

        // Dim so the currently selected chip is the only bright one on the bar.
        if (useDimState && chipImage != null)
        {
            dimTween?.Kill();
            dimTween = chipImage.DOColor(dimColor, dimFadeDuration);
        }
    }

    /// <summary>
    /// Works out what this chip pulses when selected, once.
    ///
    /// Order matters. An explicit reference wins. Otherwise the chip's own light
    /// object is used — in this scene that is a "Triangle" carrying a URP
    /// Light2D, so the pulse rides its intensity and no new object is needed.
    /// A halo is only generated when the chip has no light at all.
    /// </summary>
    private void ResolveGlow()
    {
        if (glowResolved) return;
        glowResolved = true;

        if (glowImage != null)
        {
            glowBaseColor = glowImage.color;
            return;
        }

        if (lightObject != null)
        {
            Image fromLight = lightObject.GetComponent<Image>();
            if (fromLight == null) fromLight = lightObject.GetComponentInChildren<Image>(true);

            if (fromLight != null)
            {
                glowImage = fromLight;
                glowBaseColor = fromLight.color;
                return;
            }

            glowLight = lightObject.GetComponent<Light2D>();
            if (glowLight == null) glowLight = lightObject.GetComponentInChildren<Light2D>(true);

            if (glowLight != null)
            {
                glowBaseIntensity = glowLight.intensity;
                return;
            }
        }

        // Nothing to pulse. Say so once rather than failing silently — a chip
        // added without the light object its siblings have (the Joker chip, for
        // one) would otherwise just quietly never glow.
        Debug.LogWarning($"[ChipButton] '{name}' has no glow source: assign Glow Image, " +
                         "or give it a light object with a Light2D like the other chips.", this);
    }

    private void StartGlow()
    {
        if (!useGlow) return;

        ResolveGlow();
        glowTween?.Kill();

        float low = Mathf.Min(glowMinAlpha, glowMaxAlpha);
        float high = Mathf.Max(glowMinAlpha, glowMaxAlpha);
        float duration = Mathf.Max(0.05f, glowPulseDuration);

        if (glowLight != null)
        {
            glowLight.intensity = glowBaseIntensity * low;

            glowTween = DOTween.To(() => glowLight.intensity,
                                   value => glowLight.intensity = value,
                                   glowBaseIntensity * high, duration)
                               .SetEase(Ease.InOutSine)
                               .SetLoops(-1, LoopType.Yoyo)
                               .SetLink(gameObject);
            return;
        }

        if (glowImage == null) return;

        // Scaled by the image's own alpha, so a deliberately faint halo stays
        // faint instead of being forced up to full opacity.
        Color dim = glowBaseColor;
        Color bright = glowBaseColor;
        dim.a = glowBaseColor.a * low;
        bright.a = glowBaseColor.a * high;

        glowImage.color = dim;
        glowTween = glowImage.DOColor(bright, duration)
                             .SetEase(Ease.InOutSine)
                             .SetLoops(-1, LoopType.Yoyo)
                             .SetLink(glowImage.gameObject);
    }

    private void StopGlow()
    {
        glowTween?.Kill();
        glowTween = null;

        if (glowLight != null)
        {
            // Deselect() switches the light object off anyway; just hand its
            // authored intensity back rather than leaving it mid-pulse.
            glowLight.intensity = glowBaseIntensity;
            return;
        }

        if (glowImage == null) return;

        glowImage.color = glowBaseColor;
    }

    /// <summary>
    /// The chip's visible art, for the dim/brighten tint and for the sprite the
    /// flying chip borrows. Prefers the content object that already scales and
    /// floats, since that is the chip face rather than the button's hit area.
    /// Assigning chipImage in the Inspector overrides all of this.
    /// </summary>
    private Image ResolveChipImage()
    {
        if (contentTransform != null)
        {
            Image onContent = contentTransform.GetComponent<Image>();
            if (onContent != null) return onContent;

            Image inContent = contentTransform.GetComponentInChildren<Image>(true);
            if (inContent != null) return inContent;
        }

        Image onSelf = GetComponent<Image>();
        if (onSelf != null) return onSelf;

        return GetComponentInChildren<Image>(true);
    }

    // �� Float / bob ���������������────────────────────────────────────────────

    // IMPORTANT: the chip buttons sit directly inside a HorizontalLayoutGroup,
    // which OWNS their position and rewrites it on every layout rebuild. Moving
    // the button itself makes the chip jump out of the row.
    // (Scaling is fine — the group has Child Scale Width/Height off, so it
    // ignores scale. That is why the existing select-scale never misbehaved.)
    //
    // So the bob is applied to the button's CHILDREN instead. Children of the
    // button are not layout-controlled — only the button itself is — so they
    // move freely while the layout slot stays exactly where the group put it.

    private void CaptureContentBasePosition()
    {
        if (baseCaptured || contentTransform == null) return;

        contentBasePos = contentTransform.localPosition;
        baseCaptured = true;
    }

    /// <summary>True when a LayoutGroup on the parent is driving this transform's position.</summary>
    private static bool IsLayoutDriven(Transform t)
    {
        if (t == null || t.parent == null) return false;

        LayoutGroup group = t.parent.GetComponent<LayoutGroup>();
        return group != null && group.enabled;
    }

    /// <summary>
    /// Works out what can safely be moved: an explicit floatTarget wins,
    /// otherwise the chip itself if nothing is driving it, otherwise its
    /// children.
    /// </summary>
    private void ResolveFloatTargets()
    {
        if (floatTargetsResolved) return;
        floatTargetsResolved = true;

        if (floatTarget != null)
        {
            floatChildren = new RectTransform[] { floatTarget };
            floatBasePositions = new Vector2[] { floatTarget.anchoredPosition };
            return;
        }

        Transform root = contentTransform != null ? contentTransform : transform;

        if (!IsLayoutDriven(root))
        {
            // Nothing is fighting us — move the chip itself.
            if (root is RectTransform rootRect)
            {
                floatChildren = new RectTransform[] { rootRect };
                floatBasePositions = new Vector2[] { rootRect.anchoredPosition };
            }
            return;
        }

        List<RectTransform> found = new List<RectTransform>();
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i) is RectTransform child) found.Add(child);
        }

        if (found.Count == 0)
        {
            Debug.LogWarning(
                $"[ChipButton] '{name}' sits in a LayoutGroup and has no child objects to bob. " +
                "Assign Float Target, or turn Enable Float off.", this);
            return;
        }

        floatChildren = found.ToArray();
        floatBasePositions = new Vector2[floatChildren.Length];
        for (int i = 0; i < floatChildren.Length; i++)
            floatBasePositions[i] = floatChildren[i].anchoredPosition;
    }

    private void ApplyFloatOffset(float yOffset)
    {
        if (floatChildren == null) return;

        for (int i = 0; i < floatChildren.Length; i++)
        {
            if (floatChildren[i] == null) continue;
            floatChildren[i].anchoredPosition = floatBasePositions[i] + new Vector2(0f, yOffset);
        }
    }

    private void StartFloat()
    {
        if (!enableFloat) return;

        ResolveFloatTargets();
        if (floatChildren == null || floatChildren.Length == 0) return;

        floatTween?.Kill();

        // Always restart from rest so repeated select/deselect cannot drift.
        ApplyFloatOffset(0f);

        // SetLink: this is an INFINITE loop. Without it, destroying the chip
        // (scene change, panel rebuild) leaves the tween running against a dead
        // target. SetLink hands DOTween the lifetime so it kills itself.
        floatTween = DOVirtual.Float(0f, floatAmplitude, floatDuration, ApplyFloatOffset)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(gameObject);
    }

    private void StopFloat()
    {
        floatTween?.Kill();
        floatTween = null;

        ApplyFloatOffset(0f);
    }

    /*private void StartGlowPulse()
    {
        if (glowImage == null) return;
        
        glowTween = glowImage
            .DOFade(glowMinAlpha, glowPulseSpeed / 2f)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.InOutSine);
    }*/

    /*private IEnumerator RotateLightRays()
    {
        while (isSelected && lightRaysTransform != null)
        {
            lightRaysTransform.Rotate(0f, 0f, lightRaysRotationSpeed * Time.deltaTime);
            yield return null;
        }
    }*/

    private void KillAllTweens()
    {
        scaleTween?.Kill();
        floatTween?.Kill();
        dimTween?.Kill();
        glowTween?.Kill();
        //ringTween?.Kill();
        
        /*if (lightRaysCoroutine != null)
        {
            StopCoroutine(lightRaysCoroutine);
            lightRaysCoroutine = null;
        }*/
    }

    // Public method to check if this chip is selected
    public bool IsSelected() => isSelected;

    // Public method to get chip value
    public long GetChipValue() => chipValue;

    public void SetInteractable(bool state)
    {
        if (myButton == null)
        {
            myButton = GetComponent<Button>();
        }

        if (myButton != null)
        {
            myButton.interactable = state;
        }
    }

    // Force selection (for initial setup)
    public void ForceSelect()
    {
        OnChipSelected?.Invoke(chipValue);
    }

    // Add this to ChipButton.cs
    public static void ClearAllSelections()
    {
        // Passing -1 ensures no chip will match the value, so all chips run Deselect()
        OnChipSelected?.Invoke(-1);
    }
}
