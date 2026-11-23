using UnityEngine;

public class UISelection : MonoBehaviour
{
    public static UISelection Instance;
    private void Awake() { Instance = this; }

    public long selectedChip = 0;
    public void SelectChip(long value) { selectedChip = value; }
    public long GetSelectedChipValue() { return selectedChip; }

    private void OnDestroy()
    {
        // C#'s ?. operator bypasses Unity's == null override, so a static left
        // pointing at a destroyed object gets called into. Drop it here.
        if (Instance == this) Instance = null;
    }
}