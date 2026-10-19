using UnityEngine;

public class UISelection : MonoBehaviour
{
    public static UISelection Instance;
    private void Awake() { Instance = this; }
    public long selectedChip = 0;
    public void SelectChip(long value) { selectedChip = value; }
    public long GetSelectedChipValue() { return selectedChip; }
}