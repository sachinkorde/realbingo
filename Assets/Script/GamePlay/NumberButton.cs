using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NumberButton : MonoBehaviour
{
    public int myNumber;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Image buttonBackground; // Assign the button's background image here
    private Button btn;

    // Define Colors
    private Color defaultColor = Color.white; // #FFFFFF
    private Color activeColor; // Will be set to #F53B3B

    private void Start()
    {
        // Parse the hex color #F53B3B
        ColorUtility.TryParseHtmlString("#F53B3B", out activeColor);

        btn = GetComponent<Button>();

        // Auto-get image if not assigned
        if (buttonBackground == null) buttonBackground = GetComponent<Image>();

        btn.onClick.AddListener(() => GameManager.Instance.betManager.PlaceBet(myNumber));

        // Initial State (Reset to white)
        if (buttonBackground) buttonBackground.color = defaultColor;

        // Manual listener for direct updates
        GameManager.Instance.betManager.OnBetPlaced += HandleBetUpdate;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance && GameManager.Instance.betManager)
            GameManager.Instance.betManager.OnBetPlaced -= HandleBetUpdate;
    }

    private void HandleBetUpdate(int numberChanged)
    {
        if (numberChanged == myNumber)
        {
            long amt = GameManager.Instance.betManager.GetTotalBetOnNumber(myNumber);
            SetAmount(amt);
            SetHighlight(amt > 0);
        }
    }

    public void SetAmount(long amt)
    {
        amountText.text = amt > 0 ? amt.ToString() : "";
    }

    /// <summary>
    /// Locks/unlocks this slot. Called by UIManager when the round is committed,
    /// so the grid greys out with the rest of the betting controls.
    /// </summary>
    public void SetInteractable(bool state)
    {
        if (btn == null) btn = GetComponent<Button>();
        if (btn != null) btn.interactable = state;
    }

    public void SetHighlight(bool isActive)
    {
        if (buttonBackground)
        {
            // If active -> Red (#F53B3B), else -> White (#FFFFFF)
            buttonBackground.color = isActive ? activeColor : defaultColor;
        }
    }
}