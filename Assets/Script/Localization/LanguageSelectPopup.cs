using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealBingo.Localization
{
    /// <summary>
    /// "Choose your language" — shown once, before anything else, to a player
    /// who has never picked one.
    ///
    /// WHERE IT GOES
    ///   On a panel in the Main scene, above the splash/login. UIManagerMenu
    ///   asks it to run before the splash fades, so the login screen behind it
    ///   is already in the chosen language when it appears.
    ///
    /// WIRING (Inspector)
    ///   panelRoot       the object to show/hide (leave empty = this one)
    ///   buttonTemplate  ONE button prefab/child laid out how you want each row
    ///                   to look; it is cloned per language and hidden. It needs
    ///                   a TMP_Text child for the name.
    ///   buttonContainer parent for the clones (a Vertical Layout Group)
    ///   confirmButton   optional — with one, the player taps a language then
    ///                   confirms; without one, tapping a language proceeds
    ///                   immediately.
    ///
    /// Each row shows the language in ITS OWN script ("हिन्दी", not "Hindi"),
    /// with the English name underneath when a second label is wired. Someone
    /// who cannot read English must still be able to find their language — the
    /// whole point of showing this before anything else.
    ///
    /// Buttons are built from LocalizationManager.AvailableLanguages(), which
    /// lists only languages that actually have a column in the CSV. Adding
    /// Gujarati later means adding a column, not touching this screen.
    /// </summary>
    public class LanguageSelectPopup : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;

        [Header("Buttons")]
        [Tooltip("A single button laid out as one language row. Cloned per language, then hidden.")]
        [SerializeField] private Button buttonTemplate;
        [SerializeField] private Transform buttonContainer;

        [Tooltip("Optional. With a confirm button the player picks, then confirms. " +
                 "Without one, tapping a language selects and closes in a single step.")]
        [SerializeField] private Button confirmButton;

        [Tooltip("Optional close / back / X button. Shuts the picker leaving the language " +
                 "as it was. Worth wiring for the Settings route, where the player may have " +
                 "opened it just to look — on first run there is nothing to go back to.")]
        [SerializeField] private Button closeButton;

        [Header("Selected look")]
        [SerializeField] private Color selectedColor = new Color(0.25f, 0.80f, 0.40f);
        [SerializeField] private Color unselectedColor = Color.white;

        [Header("Animation")]
        [SerializeField] private float fadeSeconds = 0.25f;

        [Header("Testing")]
        [Tooltip("Show the picker on EVERY launch, ignoring the saved choice — for checking " +
                 "this screen without clearing PlayerPrefs each time.\n\n" +
                 "TURN OFF BEFORE BUILDING: players should be asked once, not every launch.")]
        [SerializeField] private bool alwaysShowForTesting;

        private readonly List<Button> spawned = new List<Button>();
        private string pendingCode;
        private System.Action onClosed;

        // What the language was when the picker opened, so Cancel can undo the
        // live preview. Empty on the first-run route, where there is nothing to
        // restore and no way to cancel.
        private string languageOnOpen;

        // Set once the popup is on screen (or on its way). EnsureInit must not
        // hide the panel after this, or showing it becomes self-cancelling.
        private bool showing;
        private bool initialized;

        private void Awake() => EnsureInit();

        /// <summary>
        /// One-time setup, callable before Awake has ever run.
        ///
        /// This panel starts INACTIVE in the scene, so Unity does not call
        /// Awake on it at load — it calls it at the moment something activates
        /// the object, which is the middle of Show(). The old code did its
        /// setup in Awake and ended with panelRoot.SetActive(false), so the
        /// sequence was:
        ///
        ///     Show() -> SetActive(true) -> Awake() -> SetActive(false)
        ///
        /// The popup hid itself a frame after appearing, onClosed never fired,
        /// and UIManagerMenu sat on WaitUntil(languageChosen) forever — the
        /// splash screen never faded and the game looked frozen on "Welcome".
        ///
        /// Doing the work here, guarded, means it happens exactly once whether
        /// Awake or Show gets there first, and the hide only applies when the
        /// popup is not deliberately being shown.
        /// </summary>
        private void EnsureInit()
        {
            if (initialized) return;
            initialized = true;

            if (panelRoot == null) panelRoot = gameObject;

            if (buttonTemplate != null) buttonTemplate.gameObject.SetActive(false);
            if (confirmButton != null) confirmButton.onClick.AddListener(ConfirmSelection);
            if (closeButton != null) closeButton.onClick.AddListener(CancelSelection);

            if (!showing) panelRoot.SetActive(false);
        }

        /// <summary>
        /// Shows the popup only if this player has never chosen a language.
        /// Calls onDone either way, so the caller can treat it as one step in a
        /// startup sequence without branching.
        /// </summary>
        public void ShowIfFirstRun(System.Action onDone)
        {
            // Nothing is wrong when a returning player never sees this screen —
            // they already chose. It surprises during testing, though, because
            // one run through the picker suppresses it on every later launch,
            // and so does using the editor's Switch Language menu.
            if (LocalizationManager.HasChosenLanguage && !alwaysShowForTesting)
            {
                onDone?.Invoke();
                return;
            }

            Show(onDone);
        }

        /// <summary>
        /// Opens the picker from the Settings panel. Parameterless on purpose:
        /// a Button's OnClick list can only call a method it can serialise, and
        /// Show takes a callback, so it cannot be wired in the Inspector.
        ///
        /// Remembers the language in force on open, so Cancel can put it back
        /// after the player has tapped through a few to see what they look like.
        /// </summary>
        public void OpenFromSettings()
        {
            languageOnOpen = LocalizationManager.CurrentLanguage;
            Show();
        }

        /// <summary>
        /// Close without changing anything: restores whatever was selected when
        /// the picker opened, because tapping a row applies it live as a preview.
        /// </summary>
        public void CancelSelection()
        {
            if (!string.IsNullOrEmpty(languageOnOpen) && languageOnOpen != LocalizationManager.CurrentLanguage)
                LocalizationManager.Instance?.SetLanguage(languageOnOpen, persist: false);

            AudioManager.Instance?.PlayOneShot("UIButtonClick");
            Close();
        }

        /// <summary>Always shows the popup — use this for a "Language" settings row.</summary>
        public void Show(System.Action onDone = null)
        {
            // Before EnsureInit, so the activation below cannot hide the panel.
            showing = true;
            EnsureInit();

            onClosed = onDone;
            pendingCode = LocalizationManager.CurrentLanguage;

            Build();

            panelRoot.SetActive(true);

            CanvasGroup group = panelRoot.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 0f;
                group.DOFade(1f, fadeSeconds).SetUpdate(true);
            }
        }

        private void Build()
        {
            foreach (Button b in spawned)
                if (b != null) Destroy(b.gameObject);
            spawned.Clear();

            if (buttonTemplate == null || buttonContainer == null)
            {
                Debug.LogError("[LanguageSelectPopup] buttonTemplate / buttonContainer not wired — cannot build the list.");
                return;
            }

            foreach (LanguageOption option in LocalizationManager.AvailableLanguages())
            {
                LanguageOption captured = option;

                Button button = Instantiate(buttonTemplate, buttonContainer);
                button.gameObject.SetActive(true);
                button.gameObject.name = $"Lang_{captured.code}";

                // First label = the native name, second (if present) = English.
                TMP_Text[] labels = button.GetComponentsInChildren<TMP_Text>(true);
                // These bypass LocalizationManager.Get (they are not sheet rows),
                // so they need the same reordering applied by hand — otherwise
                // "हिन्दी" draws its ि against the wrong letter on the one screen
                // whose whole job is being readable to someone who only reads
                // that script.
                if (labels.Length > 0) labels[0].text = DevanagariShaper.Fix(captured.nativeName);
                if (labels.Length > 1) labels[1].text = captured.englishName;

                button.onClick.AddListener(() => Select(captured.code));
                spawned.Add(button);
            }

            Paint();
        }

        private void Select(string code)
        {
            pendingCode = code;
            AudioManager.Instance?.PlayOneShot("UIButtonClick");
            HapticManager.Selection();

            // Apply straight away so the player SEES the language change on
            // this very screen — the strongest possible confirmation they
            // picked the right one, and it costs nothing to undo by tapping
            // another row.
            LocalizationManager.Instance?.SetLanguage(code, persist: false);
            Paint();

            // No confirm button = tapping is the decision.
            if (confirmButton == null) ConfirmSelection();
        }

        private void Paint()
        {
            foreach (Button b in spawned)
            {
                if (b == null) continue;

                bool isSelected = b.gameObject.name == $"Lang_{pendingCode}";
                Image bg = b.GetComponent<Image>();
                if (bg != null) bg.color = isSelected ? selectedColor : unselectedColor;
            }
        }

        private void ConfirmSelection()
        {
            // persist: true is what stops the popup coming back next launch.
            LocalizationManager.Instance?.SetLanguage(pendingCode, persist: true);

            AudioManager.Instance?.PlayOneShot("UIButtonClick");
            HapticManager.Medium();

            Close();
        }

        private void Close()
        {
            showing = false;

            // Capture and clear up front, so the callback runs exactly once no
            // matter which path below gets there.
            System.Action callback = onClosed;
            onClosed = null;

            CanvasGroup group = panelRoot.GetComponent<CanvasGroup>();

            if (group == null)
            {
                panelRoot.SetActive(false);
                callback?.Invoke();
                return;
            }

            // OnKill rather than OnComplete. A tween killed before it finishes
            // — a scene load, DOTween.KillAll, the object being disabled —
            // never raises OnComplete, and the startup sequence would wait on
            // a callback that can no longer arrive, freezing on the splash
            // screen. OnKill fires on normal completion too, so this covers
            // both without a second code path.
            group.DOFade(0f, fadeSeconds).SetUpdate(true).OnKill(() =>
            {
                if (panelRoot != null) panelRoot.SetActive(false);
                callback?.Invoke();
            });
        }
    }
}
