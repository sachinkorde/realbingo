using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PasswordToggle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Image buttonImage;

    [Header("Icons")]
    [SerializeField] private Sprite eyeOpenIcon;   // Drag "Eye Open" sprite here
    [SerializeField] private Sprite eyeClosedIcon; // Drag "Eye Closed" sprite here

    private bool isPasswordVisible = false;

    private void Start()
    {
        // Ensure the input starts hidden
        if (passwordInput != null)
        {
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            passwordInput.ForceLabelUpdate();
        }

        // Setup Button Click
        GetComponent<Button>().onClick.AddListener(ToggleVisibility);
    }

    public void ToggleVisibility()
    {
        isPasswordVisible = !isPasswordVisible;

        if (isPasswordVisible)
        {
            // Show Password
            passwordInput.contentType = TMP_InputField.ContentType.Standard;
            if (buttonImage && eyeOpenIcon) buttonImage.sprite = eyeOpenIcon;
        }
        else
        {
            // Hide Password
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            if (buttonImage && eyeClosedIcon) buttonImage.sprite = eyeClosedIcon;
        }

        // Refresh the InputField to apply changes immediately
        passwordInput.ForceLabelUpdate();
    }
}