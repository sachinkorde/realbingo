using UnityEngine;
using TMPro;

/// <summary>
/// Dynamically calculates exact keyboard height and ONLY slides the form 
/// up if the specific active input field is hidden. 
/// Works on any aspect ratio, tablet, or folding phone.
/// </summary>
public class DynamicFieldLifter : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The container holding ONLY the Input Fields. Attach this script to it!")]
    public RectTransform formContainer;
    public Canvas parentCanvas;

    [Header("Settings")]
    [Tooltip("How smoothly the form glides up and down")]
    public float smoothSpeed = 10f;
    [Tooltip("How much space to leave between the keyboard and the text box")]
    public float paddingAboveKeyboard = 50f;

    private Vector2 originalPosition;
    private TMP_InputField activeField;
    private float targetY;
    private RectTransform canvasRect;

    public GameObject logo;
    public GameObject parentPanel;

    private void Start()
    {
        if (formContainer == null) formContainer = GetComponent<RectTransform>();
        if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();

        canvasRect = parentCanvas.GetComponent<RectTransform>();
        originalPosition = formContainer.anchoredPosition;
        targetY = originalPosition.y;

        // Automatically find all input fields inside this form
        TMP_InputField[] fields = formContainer.GetComponentsInChildren<TMP_InputField>(true);
        foreach (TMP_InputField field in fields)
        {
            field.onSelect.AddListener((_) => activeField = field);
            field.onDeselect.AddListener((_) => { if (activeField == field) activeField = null; });
        }
    }

    private void LateUpdate()
    {
        if (activeField != null && TouchScreenKeyboard.visible)
        {
            // 1. Get exact keyboard size (With a safe fallback for Unity Android bugs)
            float keyboardHeight = TouchScreenKeyboard.area.height;
            if (keyboardHeight <= 0) keyboardHeight = Screen.height * 0.55f; // Assume 55% in landscape

            // 2. Find the exact bottom edge of the specific field the user tapped
            Vector3[] corners = new Vector3[4];
            activeField.GetComponent<RectTransform>().GetWorldCorners(corners);

            Camera cam = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
            float fieldBottomScreenY = RectTransformUtility.WorldToScreenPoint(cam, corners[0]).y;

            // 3. Mathematical check: Is it hiding behind the keyboard?
            float desiredScreenY = keyboardHeight + paddingAboveKeyboard;
            float screenDelta = desiredScreenY - fieldBottomScreenY;

            // Only calculate a new push if the delta is noticeable (stops micro-jitters)
            if (Mathf.Abs(screenDelta) > 1f)
            {
                // Convert screen pixels to Canvas UI units
                float canvasDelta = screenDelta * (canvasRect.rect.height / Screen.height);

                targetY = formContainer.anchoredPosition.y + canvasDelta;
                parentPanel.SetActive(false);
                logo.SetActive(false);
                // NEVER push the form down below its original starting design
                targetY = Mathf.Max(targetY, originalPosition.y);
            }
        }
        else
        {
            // Keyboard is closed, glide back to original layout
            targetY = originalPosition.y;
            parentPanel.SetActive(true);
            logo.SetActive(true);
        }

        // 4. Smoothly glide the container to the calculated target
        Vector2 pos = formContainer.anchoredPosition;
        pos.y = Mathf.Lerp(pos.y, targetY, Time.deltaTime * smoothSpeed);
        formContainer.anchoredPosition = pos;
    }
}