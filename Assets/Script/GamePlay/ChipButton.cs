using UnityEngine;
using UnityEngine.UI;
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
    
    //[Header("Glow Effect")]
    //[Tooltip("Image behind chip that glows when selected")]
    //public Image glowImage;
    //public Color glowColor = new Color(1f, 0.85f, 0f, 0.8f); // Golden glow
    //public float glowPulseSpeed = 1.5f;
    //public float glowMinAlpha = 0.4f;
    //public float glowMaxAlpha = 1f;
    
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
    //private Tween glowTween;
    //private Tween ringTween;
    //private Coroutine lightRaysCoroutine;
    //private Color originalChipColor;

    public GameObject lightObject;

    private void Start()
    {
        if (myButton == null) 
        { 
            myButton = GetComponent<Button>();
        }

        if (chipImage != null)
        {
            //originalChipColor = chipImage.color;
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
        lightObject.SetActive(true);
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
            dimTween = chipImage.DOColor(Color.white, dimFadeDuration);
        }
    }

    private void Deselect()
    {
        isSelected = false;
        KillAllTweens();
        lightObject.SetActive(false);
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

        // Dim so the currently selected chip is the only bright one on the bar.
        if (useDimState && chipImage != null)
        {
            dimTween?.Kill();
            dimTween = chipImage.DOColor(dimColor, dimFadeDuration);
        }
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
        //glowTween?.Kill();
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
