using UnityEngine;
using RealBingo.Localization;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using DG.Tweening;
using System;

public class UIManagerMenu : MonoBehaviour
{
    public static UIManagerMenu Instance { get; private set; }

    [Header("Screen References")]
    [SerializeField] private CanvasGroup splashScreen;
    [SerializeField] private CanvasGroup loginScreen;
    [SerializeField] private CanvasGroup createAccountScreen;
    //[SerializeField] private CanvasGroup otpScreen;
    [SerializeField] private CanvasGroup changePasswordScreen;
    [SerializeField] private CanvasGroup forgerPasswordScreen;

    [Header("Pop-up References")]
    [SerializeField] private RectTransform popupPanel;
    [SerializeField] public TMP_Text popupText;

    [Header("Localization")]
    [Tooltip("First-run 'Choose your language' panel. Leave empty to skip the prompt " +
             "and start in the saved language (English for a new install).")]
    [SerializeField] private LanguageSelectPopup languageSelectPopup;

    public const string FIRST_LOGIN_KEY = "FirstLogin";
    public const string Mobile_Number = "mobileNumber";
    [SerializeField] public bool isFirstTime; //= !PlayerPrefs.HasKey(FIRST_LOGIN_KEY) || PlayerPrefs.GetInt(FIRST_LOGIN_KEY) == 1;
    public bool IsForgetPasswordActive = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        //DontDestroyOnLoad(gameObject);

        isFirstTime = !PlayerPrefs.HasKey(FIRST_LOGIN_KEY) || PlayerPrefs.GetInt(FIRST_LOGIN_KEY) == 1;
        HideAllScreens();
    }

    private void OnDestroy()
    {
        // C#'s ?. operator bypasses Unity's == null override, so a static left
        // pointing at a destroyed object gets called into. Drop it here.
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        StartCoroutine(SplashScreenRoutine());
    }

    private IEnumerator SplashScreenRoutine()
    {
        SetActiveCanvasGroup(splashScreen, true);
        splashScreen.DOFade(1, 0.5f);

        yield return new WaitForSeconds(2f);

        // Ask for a language BEFORE anything else is decided, so the login
        // screen (or the game) is already in the player's language the first
        // time they see it — not switched out from under them a moment later.
        //
        // Returning players skip this: ShowIfFirstRun calls straight back when
        // a language has already been saved.
        if (languageSelectPopup != null)
        {
            bool languageChosen = false;
            languageSelectPopup.ShowIfFirstRun(() => languageChosen = true);
            yield return new WaitUntil(() => languageChosen);
        }

        // Decide based on a VALID server session, not just a local flag.
        // This keeps the user logged in across launches, BUT a stale/expired/
        // wrong-server token correctly sends them back to login.
        bool goToGame = false;

        if (APIManager.Instance != null && APIManager.Instance.IsLoggedIn())
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                // Offline but has a token -> keep them logged in; the game shows
                // its "connect internet" popup. Don't wrongly force a login.
                goToGame = true;
            }
            else
            {
                // Verify the saved token with the server.
                bool done = false;
                long responseCode = 0;
                APIManager.Instance.GetProfile((ok, data, code) => { done = true; goToGame = ok; responseCode = code; });
                yield return new WaitUntil(() => done);

                if (!goToGame)
                {
                    // Only clear the token on an ACTUAL auth rejection (401).
                    // Anything else — a timeout, a dropped connection, the
                    // server still waking up from a free-tier cold start —
                    // is not proof the token is bad. Clearing it there would
                    // force a fully valid session back through login for no
                    // reason. The unverified token is kept so the next launch
                    // can simply try again.
                    if (responseCode == 401)
                    {
                        APIManager.Instance.ClearSession();
                        Toast.Warning(L.T("error.session_expired"));
                    }
                    else
                    {
                        Toast.Warning(L.T("error.login_again"));
                    }
                }
            }
        }

        splashScreen.DOFade(0, 0.8f).OnComplete(() =>
        {
            splashScreen.gameObject.SetActive(false);

            if (goToGame)
                LoadGameScene();
            else
                ShowLoginScreen();
        });
    }

    #region === SCREEN HANDLERS ===

    public void ShowLoginScreen()
    {
        ShowScreen(loginScreen);
    }

    public void ShowCreateAccountScreen()
    {
        AudioManager.Instance.PlayOneShot("UIButtonClick");
        ShowScreen(createAccountScreen);
    }

    internal void ShowForgetScreen()
    {
        ShowScreen(forgerPasswordScreen);
    }

    public void ShowChangePasswordScreen()
    {
        ShowScreen(changePasswordScreen);
    }

    private void ShowScreen(CanvasGroup target)
    {
        HideAllScreens();
        target.gameObject.SetActive(true);
        target.alpha = 0;
        target.DOFade(1, 0.5f);
    }

    private void HideAllScreens()
    {
        CanvasGroup[] screens = { splashScreen, loginScreen, createAccountScreen, changePasswordScreen, forgerPasswordScreen };
        foreach (var s in screens)
        {
            if (s != null) s.gameObject.SetActive(false);
        }
    }

    #endregion

    #region === POPUP HANDLER ===

    private static readonly Color InfoTextColor = Color.white;
    private static readonly Color SuccessTextColor = new(0.55f, 1f, 0.65f);
    private static readonly Color ErrorTextColor = new(1f, 0.55f, 0.55f);
    private static readonly Color WarningTextColor = new(1f, 0.85f, 0.4f);

    public void ShowPopup(string message) => ShowPopup(message, ToastType.Info);

    /// <summary>
    /// Toast ALWAYS fires here — unconditionally, every call, every severity —
    /// not just as a fallback when the styled popup happens to be unavailable.
    /// That "only if unwired" version was itself the bug: whenever popupText/
    /// popupPanel WERE assigned, messages went out through the styled popup
    /// only and Toast silently never ran, so anything that depended on Toast
    /// being the guaranteed channel could still end up invisible somewhere.
    /// </summary>
    public void ShowPopup(string message, ToastType type)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        switch (type)
        {
            case ToastType.Success: Toast.Success(message); break;
            case ToastType.Error: Toast.Error(message); break;
            case ToastType.Warning: Toast.Warning(message); break;
            default: Toast.Info(message); break;
        }

        // popupText/popupPanel are Inspector-assigned; if either was left
        // unwired on some screen, the switch above already showed the message,
        // so it is safe to simply stop here instead of throwing.
        if (popupText == null || popupPanel == null) return;

        popupText.text = message;
        popupText.color = type switch
        {
            ToastType.Success => SuccessTextColor,
            ToastType.Error => ErrorTextColor,
            ToastType.Warning => WarningTextColor,
            _ => InfoTextColor,
        };
        popupPanel.gameObject.SetActive(true);
        popupPanel.localScale = Vector3.zero;
        popupPanel.DOScale(1, 0.3f).SetEase(Ease.OutBack);
    }

    public void HidePopup()
    {
        popupPanel.DOScale(0, 0.2f).OnComplete(() => popupPanel.gameObject.SetActive(false));
    }

    #endregion

    #region === LOGIN & SCENE HANDLER ===

    public void OnLoginSuccess()
    {
        PlayerPrefs.SetInt(FIRST_LOGIN_KEY, 0);
        PlayerPrefs.Save();
        LoadGameScene();
    }

    public void OnFirstTimeLoginDetected()
    {
        PlayerPrefs.SetInt(FIRST_LOGIN_KEY, 1);
        PlayerPrefs.Save();
    }

    public void LoadGameScene()
    {
        SceneManager.LoadScene("GameScene");
    }

    #endregion

    private void SetActiveCanvasGroup(CanvasGroup cg, bool active)
    {
        cg.gameObject.SetActive(active);
        cg.alpha = active ? 1 : 0;
        cg.interactable = active;
        cg.blocksRaycasts = active;
    }
}
