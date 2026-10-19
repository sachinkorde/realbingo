using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RealBingo.Localization
{
    /// <summary>
    /// Translates the OPTIONS inside a TMP_Dropdown.
    ///
    /// LocalizedText cannot do this. A dropdown's options are plain strings held
    /// on the TMP_Dropdown component, not TMP_Text objects — the single label
    /// you can see is just whichever option is currently picked, and rewriting
    /// that label would be undone the moment the player opens the list.
    ///
    /// Keeps the player's SELECTION across a language change: the index is read
    /// before the rebuild and restored after, so switching to Hindi while
    /// "Female" is chosen leaves "महिला" chosen rather than snapping back to the
    /// first entry.
    ///
    /// USE
    ///   Put it on the same object as the TMP_Dropdown and list one key per
    ///   option, in the SAME ORDER as the dropdown's own options. The gender
    ///   dropdown in GameScene is:
    ///       common.select, common.male, common.female, common.other
    ///
    /// Code that reads the dropdown must keep using `value` (the index). Never
    /// compare the dropdown's visible text against an English string — that is
    /// exactly the comparison that breaks the moment someone picks Marathi.
    /// </summary>
    [AddComponentMenu("Real Bingo/Localized Dropdown")]
    [RequireComponent(typeof(TMP_Dropdown))]
    [DisallowMultipleComponent]
    public class LocalizedDropdown : MonoBehaviour
    {
        [Tooltip("One CSV key per option, in the same order as the dropdown's options.")]
        [SerializeField] private List<string> optionKeys = new List<string>();

        private TMP_Dropdown dropdown;

        private void Awake()
        {
            dropdown = GetComponent<TMP_Dropdown>();
            LocalizationManager.Ensure();
        }

        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= Refresh;
        }

        public void Refresh()
        {
            if (dropdown == null) dropdown = GetComponent<TMP_Dropdown>();
            if (dropdown == null || optionKeys == null || optionKeys.Count == 0) return;

            // A mismatch means the dropdown gained or lost an option after the
            // keys were set. Translating by position would then label the wrong
            // entries, so leave it in English and say which object to fix.
            if (optionKeys.Count != dropdown.options.Count)
            {
                Debug.LogWarning($"[Localization] '{name}' has {optionKeys.Count} keys for " +
                                 $"{dropdown.options.Count} dropdown options — skipping.", this);
                return;
            }

            int selected = dropdown.value;

            for (int i = 0; i < dropdown.options.Count; i++)
            {
                string key = optionKeys[i];
                if (!string.IsNullOrEmpty(key)) dropdown.options[i].text = L.T(key);
            }

            // RefreshShownValue redraws the closed-state label; without it the
            // dropdown keeps showing the old language until the player opens it.
            dropdown.value = selected;
            dropdown.RefreshShownValue();
        }

#if UNITY_EDITOR
        public void EditorSetKeys(List<string> keys) => optionKeys = new List<string>(keys);
        public int EditorKeyCount => optionKeys?.Count ?? 0;
#endif
    }
}
