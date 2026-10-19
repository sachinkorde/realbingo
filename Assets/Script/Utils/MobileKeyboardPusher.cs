using UnityEngine;
using TMPro;

public class MobileKeyboardPusher : MonoBehaviour
{
    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private Canvas parentCanvas;
    private TMP_InputField inputField;

    [Header("Adjustments")]
    public float smoothSpeed = 15f;
    public float verticalOffset = 100f; // Increase this if it's still hidden

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        originalPosition = rectTransform.anchoredPosition;
        parentCanvas = GetComponentInParent<Canvas>();

        // Find the input field inside this container (Mobile Number or Password)
        inputField = GetComponentInChildren<TMP_InputField>();
    }

    void Update()
    {
        // 1. Only move if THIS specific input field is being touched
        if (inputField != null && inputField.isFocused && TouchScreenKeyboard.visible)
        {
            float keyboardHeight = TouchScreenKeyboard.area.height;

            if (keyboardHeight > 0)
            {
                // 2. Calculate the scale factor (Handling Canvas Scaler)
                float screenHeight = Screen.height;
                float canvasHeight = parentCanvas.GetComponent<RectTransform>().rect.height;
                float ratio = canvasHeight / screenHeight;

                // 3. Move the container high enough to be above the keyboard
                // In Landscape, the keyboard is usually 50-70% of the screen.
                float targetY = (keyboardHeight * ratio) - (canvasHeight / 2) + verticalOffset;

                Vector2 targetPos = new Vector2(originalPosition.x, targetY);
                rectTransform.anchoredPosition = Vector2.Lerp(rectTransform.anchoredPosition, targetPos, Time.deltaTime * smoothSpeed);
            }
        }
        else
        {
            // 4. Return to original spot when not typing
            rectTransform.anchoredPosition = Vector2.Lerp(rectTransform.anchoredPosition, originalPosition, Time.deltaTime * smoothSpeed);
        }
    }
}