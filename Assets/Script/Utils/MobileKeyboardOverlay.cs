using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// A foolproof solution for Landscape typing.
/// Opens a dedicated, highly visible input field at the top of the screen.
/// </summary>
public class MobileKeyboardOverlay : MonoBehaviour
{
    public static MobileKeyboardOverlay Instance;

    [Header("Settings")]
    [Tooltip("How fast the screen slides up and down")]
    public float smoothSpeed = 15f;
    [Tooltip("How much extra space to leave below the input field so the keyboard doesn't touch it")]
    public float extraPadding = 100f;

    private RectTransform containerRect;
    private Vector2 originalPosition;
    private TMP_InputField activeField;

    private void Start()
    {
        containerRect = GetComponent<RectTransform>();
        originalPosition = containerRect.anchoredPosition;

        // Automatically find ALL input fields inside this screen (Name, Mobile, Password, etc.)
        TMP_InputField[] fields = GetComponentsInChildren<TMP_InputField>(true);
        foreach (TMP_InputField field in fields)
        {
            // When the user taps a field, remember it
            field.onSelect.AddListener((_) => activeField = field);
            // When they stop typing, clear it
            field.onDeselect.AddListener((_) => { if (activeField == field) activeField = null; });
        }
    }

    private void Update()
    {
        Vector2 targetPosition = originalPosition;

        // Only move the screen if the user is typing AND the keyboard is open
        if (activeField != null && TouchScreenKeyboard.visible)
        {
            // Find exactly where this specific input field is on the panel
            float fieldY = containerRect.InverseTransformPoint(activeField.transform.position).y;

            // If the field is in the lower half of the screen (negative Y), we push the container UP
            if (fieldY < 0)
            {
                // Move the container up by the exact distance the field is down, plus our padding
                targetPosition = new Vector2(originalPosition.x, originalPosition.y + Mathf.Abs(fieldY) + extraPadding);
            }
        }

        // Smoothly slide the container to the target position
        containerRect.anchoredPosition = Vector2.Lerp(containerRect.anchoredPosition, targetPosition, Time.deltaTime * smoothSpeed);
    }
}