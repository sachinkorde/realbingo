using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Attach this to ANY TMP_InputField in your game. 
/// It intercepts the click and opens the Overlay instead.
/// </summary>
[RequireComponent(typeof(TMP_InputField))]
public class InputFieldTrigger : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("What should the overlay title say? (e.g. 'Enter Mobile Number')")]
    public string fieldTitle = "Enter Detail";

    private TMP_InputField myInput;

    private void Start()
    {
        myInput = GetComponent<TMP_InputField>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // Don't let the normal input field open the keyboard
        eventData.Use();

        // Open our foolproof overlay instead
        /*if (MobileKeyboardOverlay.Instance != null)
        {
            MobileKeyboardOverlay.Instance.OpenOverlayFor(myInput, fieldTitle);
        }*/
    }
}