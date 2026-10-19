using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RealBingo.Localization;
using DG.Tweening;
using UnityEngine.Networking;
using System;
using Unity.VisualScripting;

/// <summary>
/// Professional Login Handler with full API integration, validation, and smooth UX
/// </summary>
public class LoginHandler : MonoBehaviour
{
    public static LoginHandler Instance { get; private set; }

    [Header("OTP Timer")]
    [SerializeField] private OTPTimerHandler otpTimerHandler;

    [Header("Loading Indicator")]
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private Image loadingSpinner;

    [Header("=== Login Screen ===")]
    [SerializeField] public TMP_InputField loginCredential_inputText;
    [SerializeField] public TMP_InputField loginPass_inputText;
    [SerializeField] public Button loginButton;
    [SerializeField] public Button forgotPasswordButton;
    [SerializeField] public Button registerButton;

    [Header("=== Register Screen ===")]
    [SerializeField] public TMP_InputField registerName_inputText;
    [SerializeField] public TMP_InputField registerMobile_inputText;
    [SerializeField] public TMP_InputField registerEmail_inputText;
    [SerializeField] public TMP_InputField registerPassword_inputText;
    [SerializeField] public TMP_InputField registerConfirmPassword_inputText;
    [SerializeField] public TMP_InputField registerOTP_inputText;
    [SerializeField] public TMP_InputField registerReferral_inputText;
    [SerializeField] public TMP_Text registerOTPTimerText;
    [SerializeField] public Button registerSendOTPButton;
    [SerializeField] public Button registerSubmitButton;
    [SerializeField] public Button registerBackButton;

    [Header("=== Username Availability (optional wiring) ===")]
    [Tooltip("Small label under the username box: 'Username available!' / 'Already taken'. " +
             "Leave empty to skip the inline message — the pre-OTP gate still runs either way.")]
    [SerializeField] public TMP_Text registerUsernameStatusText;
    [SerializeField] private Color usernameAvailableColor = new Color(0.25f, 0.80f, 0.40f);
    [SerializeField] private Color usernameTakenColor = new Color(0.90f, 0.30f, 0.30f);
    [SerializeField] private Color usernameCheckingColor = new Color(0.70f, 0.70f, 0.70f);

    [Header("=== Forgot Password Screen ===")]
    [SerializeField] public TMP_InputField forgotMobile_inputText;
    [SerializeField] public TMP_InputField forgotOTP_inputText;
    [SerializeField] public Button forgotSendOTPButton;
    [SerializeField] public Button forgotVerifyButton;
    [SerializeField] public Button forgotBackButton;

    [Header("=== Reset Password Screen ===")]
    [SerializeField] public TMP_InputField resetNewPassword_inputText;
    [SerializeField] public TMP_InputField resetConfirmPassword_inputText;
    [SerializeField] public Button resetSubmitButton;
    [SerializeField] public Button resetCancelButton;

    /*[Header("=== OTP Screen ===")]
    [SerializeField] public TMP_InputField otp_inputText;
    [SerializeField] public Button verifyOTPButton;
    [SerializeField] public Button resendOTPButton;
    [SerializeField] public TMP_Text otpTimerText;*/

    [Header("Validation")]
    [SerializeField] public int minPasswordLength = 6;
    [SerializeField] public int mobileNumberLength = 10;
    [SerializeField] public int otpLength = 6;

    private string currentModule = "";
    private bool isProcessing = false;

    // Username availability — remembers the LAST name the server actually
    // answered about, so tapping Send OTP straight after an inline check does
    // not pay for a second identical round trip.
    private string lastCheckedUsername = "";
    private bool lastCheckedUsernameAvailable;
    // The server's own wording for the rejection. A success:false answer is not
    // always "taken" — /check-username also rejects on validation ("The
    // username field is required.") — so saying "already taken" everywhere
    // would tell the player something untrue and unfixable.
    private string lastCheckedUsernameMessage = "";
    private bool usernameCheckRunning;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        //DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        SetupButtonListeners();
        HideLoading();
    }

    private void SetupButtonListeners()
    {
        // Login Screen
        loginButton?.onClick.AddListener(OnLoginClicked);
        forgotPasswordButton?.onClick.AddListener(OnForgotPasswordClicked);
        registerButton?.onClick.AddListener(OnRegisterClicked);

        // Register Screen
        registerSendOTPButton?.onClick.AddListener(OnRegisterSendOTPClicked);
        registerSubmitButton?.onClick.AddListener(OnRegisterSubmitClicked);
        registerBackButton?.onClick.AddListener(OnBackToLoginClicked);

        // Check the username the moment the player leaves the field, so a
        // clash is shown while they are still on that box — not after they
        // have filled in five more fields and waited for an SMS.
        // onEndEdit (not onValueChanged) = one request per finished name,
        // instead of one per keystroke.
        if (registerName_inputText != null)
        {
            registerName_inputText.onEndEdit.AddListener(OnRegisterUsernameEndEdit);
            registerName_inputText.onValueChanged.AddListener(OnRegisterUsernameValueChanged);
        }

        // Forgot Password Screen
        forgotSendOTPButton?.onClick.AddListener(OnForgotSendOTPClicked);
        forgotVerifyButton?.onClick.AddListener(OnForgotVerifyClicked);
        forgotBackButton?.onClick.AddListener(OnBackToLoginClicked);

        // Reset Password Screen
        resetSubmitButton?.onClick.AddListener(OnResetPasswordClicked);
        resetCancelButton?.onClick.AddListener(OnBackToLoginClicked);

        // OTP Screen
        //verifyOTPButton?.onClick.AddListener(OnVerifyOTPClicked);
        //resendOTPButton?.onClick.AddListener(OnResendOTPClicked);
    }

    #region Login Flow
    private void OnLoginClicked()
    {
        Debug.Log("Login button clicked");

        PlayButtonSound();
        if (isProcessing) return;

        string credential = loginCredential_inputText?.text?.Trim() ?? "";
        string password = loginPass_inputText?.text ?? "";
        
        // Validation
        if (string.IsNullOrEmpty(credential))
        {
            ShowPopup(L.T("login.enter_credential"));
            ShakeInput(loginCredential_inputText);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowPopup(L.T("login.enter_password"));
            ShakeInput(loginPass_inputText);
            return;
        }

        if (password.Length < minPasswordLength)
        {
            ShowPopup(L.T("register.password_short", minPasswordLength));
            return;
        }

        // API Call
        ShowLoading();
        //UIManagerMenu.Instance?.OnLoginSuccess();
        //StartCoroutine(LoginCoroutine(credential, password));
        APIManager.Instance?.Login(credential, password, (success, message, userData) =>
        {
            HideLoading();

            if (success)
            {
                ShowPopup(L.T("login.success"), ToastType.Success);
                DOVirtual.DelayedCall(1f, () =>
                {
                    UIManagerMenu.Instance?.OnLoginSuccess();
                });
            }
            else
            {
                ShowPopup(message, ToastType.Error);

                Debug.LogError($"Login failed: {message}");
            }
        });
    }

    #endregion

    #region Registration Flow
    private void OnRegisterClicked()
    {
        PlayButtonSound();
        ClearRegisterFields();
        UIManagerMenu.Instance?.ShowCreateAccountScreen();
    }

    #region Username availability

    /// <summary>
    /// Typing again invalidates the previous verdict — the cached answer belongs
    /// to the OLD name, and leaving a green "available!" under a name the server
    /// has never seen is worse than showing nothing.
    /// </summary>
    private void OnRegisterUsernameValueChanged(string value)
    {
        if (value?.Trim() != lastCheckedUsername)
            SetUsernameStatus("", usernameCheckingColor);
    }

    /// <summary>Fired when the player leaves the username box.</summary>
    private void OnRegisterUsernameEndEdit(string value)
    {
        string username = value?.Trim() ?? "";
        if (string.IsNullOrEmpty(username)) { SetUsernameStatus("", usernameCheckingColor); return; }

        // Already answered for exactly this name — don't ask twice.
        if (username == lastCheckedUsername)
        {
            SetUsernameStatus(UsernameVerdictMessage(),
                              lastCheckedUsernameAvailable ? usernameAvailableColor : usernameTakenColor);
            return;
        }

        CheckUsernameAvailability(username, null);
    }

    /// <summary>
    /// Asks the server about <paramref name="username"/> and updates the inline
    /// label. onDone(available) ALWAYS runs exactly once — callers put their
    /// spinner teardown in it, so it must never be skipped.
    ///
    /// onDone receives true whenever the answer is "free" OR no answer could be
    /// obtained (no connection, no APIManager, a check already in flight). None
    /// of those is evidence the name is taken, and blocking registration on one
    /// would lock a player out over a dropped packet.
    ///
    /// This is advisory only. /register performs the real uniqueness check
    /// server-side, so a name claimed in the seconds between this call and
    /// submit is still caught there.
    /// </summary>
    private void CheckUsernameAvailability(string username, Action<bool> onDone)
    {
        if (usernameCheckRunning || APIManager.Instance == null) { onDone?.Invoke(true); return; }

        usernameCheckRunning = true;
        SetUsernameStatus(L.T("username.checking"), usernameCheckingColor);

        APIManager.Instance.CheckUsername(username, (requestOk, available, message) =>
        {
            usernameCheckRunning = false;

            if (!requestOk)
            {
                // Couldn't reach the server. Say nothing under the field and
                // let the player carry on — /register is the real gate.
                SetUsernameStatus("", usernameCheckingColor);
                Debug.LogWarning($"[Register] username check failed: {message}");
                onDone?.Invoke(true);
                return;
            }

            lastCheckedUsername = username;
            lastCheckedUsernameAvailable = available;
            lastCheckedUsernameMessage = message;

            SetUsernameStatus(message, available ? usernameAvailableColor : usernameTakenColor);
            onDone?.Invoke(available);
        });
    }

    /// <summary>
    /// What to tell the player about the last checked name — the server's own
    /// wording when it sent some, otherwise a plain fallback.
    /// </summary>
    private string UsernameVerdictMessage()
    {
        if (!string.IsNullOrEmpty(lastCheckedUsernameMessage))
            return lastCheckedUsernameMessage;

        return lastCheckedUsernameAvailable
            ? L.T("username.available")
            : L.T("username.taken");
    }

    private void SetUsernameStatus(string message, Color color)
    {
        if (registerUsernameStatusText == null) return;
        registerUsernameStatusText.text = message;
        registerUsernameStatusText.color = color;
    }

    #endregion

    public void OnRegisterSendOTPClicked()
    {
        PlayButtonSound();
        if (isProcessing) return;

        string mobile = registerMobile_inputText?.text?.Trim() ?? "";

        if (string.IsNullOrEmpty(registerName_inputText?.text?.Trim()))
        {
            ShowPopup(L.T("register.enter_name"));
            ShakeInput(registerName_inputText);
            return;
        }

        if (!ValidateMobile(mobile))
        {
            ShowPopup(L.T("register.enter_mobile", mobileNumberLength));
            ShakeInput(registerMobile_inputText);
            return;
        }

        if(registerConfirmPassword_inputText.text != registerPassword_inputText.text)
        {
            ShowPopup(L.T("register.password_mismatch"));
            ShakeInput(registerPassword_inputText);
            ShakeInput(registerConfirmPassword_inputText);
            return;
        }

        if (registerPassword_inputText == null || registerConfirmPassword_inputText == null ||
            string.IsNullOrEmpty(registerPassword_inputText.text) || string.IsNullOrEmpty(registerConfirmPassword_inputText.text))
        {
            ShowPopup(L.T("register.enter_both_passwords"));
            ShakeInput(registerPassword_inputText);
            ShakeInput(registerConfirmPassword_inputText);
            return;
        }


        if(registerPassword_inputText.text.Length < minPasswordLength || registerConfirmPassword_inputText.text.Length < minPasswordLength)
        {
            ShowPopup(L.T("register.password_short", minPasswordLength));
            ShakeInput(registerPassword_inputText);
            ShakeInput(registerConfirmPassword_inputText);
            return;
        }

        if (string.IsNullOrEmpty(registerEmail_inputText?.text?.Trim()) || !registerEmail_inputText.text.Contains("@"))
        {
            ShowPopup(L.T("register.enter_email"));
            ShakeInput(registerEmail_inputText);
            return;
        }

        string username = registerName_inputText?.text?.Trim() ?? "";

        // Gate the OTP behind the username check: sending an SMS for a name
        // that cannot be registered wastes the message and the player's time,
        // and they would only find out after typing the code.
        //
        // Already confirmed free for this exact name -> go straight on.
        if (username == lastCheckedUsername && lastCheckedUsernameAvailable)
        {
            SendRegisterOTP(mobile);
            return;
        }

        ShowLoading();
        CheckUsernameAvailability(username, available =>
        {
            HideLoading();

            if (!available)
            {
                ShowPopup(UsernameVerdictMessage());
                ShakeInput(registerName_inputText);
                registerName_inputText?.Select();
                return;
            }

            SendRegisterOTP(mobile);
        });
    }

    private void SendRegisterOTP(string mobile)
    {
        currentModule = "register";
        ShowLoading();

        APIManager.Instance?.SendOTP(mobile, "register", (success, message) =>
        {
            HideLoading();

            if (success)
            {
                ShowPopup(L.T("otp.sent"), ToastType.Success);
                otpTimerHandler?.StartOTPTimerOnRegister();

                // Enable OTP input
                if (registerOTP_inputText != null)
                {
                    registerOTP_inputText.interactable = true;
                    registerOTP_inputText.Select();

                    //registerOTP_inputText.text =
                }
            }
            else
            {
                ShowPopup(message, ToastType.Error);
            }
        });
    }

    private void OnRegisterSubmitClicked()
    {
        PlayButtonSound();
        if (isProcessing) return;

        string username = registerName_inputText?.text?.Trim() ?? "";
        string mobile = registerMobile_inputText?.text?.Trim() ?? "";
        string email = registerEmail_inputText?.text?.Trim() ?? "";
        string password = registerPassword_inputText?.text ?? "";
        string confirmPassword = registerConfirmPassword_inputText?.text ?? "";
        string otp = registerOTP_inputText?.text?.Trim() ?? "";
        string referral = registerReferral_inputText?.text?.Trim() ?? "";   // optional invite code

        // Full Validation
        // NOTE: this read `string.IsNullOrEmpty(name)` — MonoBehaviour.name,
        // the GameObject's own name, which is never empty. The guard could not
        // fire, so an empty username reached /register. It now checks the
        // username the player actually typed.
        if (string.IsNullOrEmpty(username))
        {
            ShowPopup(L.T("register.enter_name"));
            ShakeInput(registerName_inputText);
            return;
        }

        // Free check: if the server already told us THIS name is taken, stop
        // here instead of spending a /register round trip to be told again.
        if (username == lastCheckedUsername && !lastCheckedUsernameAvailable)
        {
            ShowPopup(UsernameVerdictMessage());
            ShakeInput(registerName_inputText);
            return;
        }

        if (!ValidateMobile(mobile))
        {
            ShowPopup(L.T("register.enter_mobile", mobileNumberLength));
            ShakeInput(registerMobile_inputText);
            return;
        }

        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
        {
            ShowPopup(L.T("register.enter_email"));
            ShakeInput(registerEmail_inputText);
            return;
        }

        if (password.Length < minPasswordLength)
        {
            ShowPopup(L.T("register.password_short", minPasswordLength));
            ShakeInput(registerPassword_inputText);
            return;
        }

        if (password != confirmPassword)
        {
            ShowPopup(L.T("register.password_mismatch"));
            ShakeInput(registerConfirmPassword_inputText);
            registerConfirmPassword_inputText.text = "";
            return;
        }

        if (otp.Length != otpLength)
        {
            ShowPopup(L.T("register.enter_otp", otpLength));
            ShakeInput(registerOTP_inputText);
            return;
        }

        // API Call
        ShowLoading();
        APIManager.Instance?.Register(mobile, email, password, confirmPassword, otp, username, (success, message) =>
        {
            HideLoading();
            
            if (success)
            {
                ShowPopup(L.T("register.success"), ToastType.Success);
                PlayerPrefs.SetInt(UIManagerMenu.FIRST_LOGIN_KEY, 0);
                PlayerPrefs.SetString(UIManagerMenu.Mobile_Number, mobile);

                Debug.Log(PlayerPrefs.GetString(UIManagerMenu.Mobile_Number) + " Registreted mobile number");

                PlayerPrefs.Save();

                DOVirtual.DelayedCall(1.5f, () => {
                    UIManagerMenu.Instance?.LoadGameScene();
                });
            }
            else
            {
                ShowPopup(message, ToastType.Error);
            }
        }, referral);
    }

    #endregion

    #region Forgot Password Flow

    private void OnForgotPasswordClicked()
    {
        PlayButtonSound();
        ClearForgotFields();
        UIManagerMenu.Instance?.ShowForgetScreen();
    }

    public void OnForgotSendOTPClicked()
    {
        if (isProcessing) return;

        string mobile = forgotMobile_inputText?.text?.Trim() ?? "";

        if (!ValidateMobile(mobile))
        {
            ShowPopup(L.T("register.enter_mobile", mobileNumberLength));
            ShakeInput(forgotMobile_inputText);
            return;
        }

        currentModule = "forgot";
        ShowLoading();
        
        APIManager.Instance?.SendOTP(mobile, "forgot_password", (success, message) =>
        {
            HideLoading();
            
            if (success)
            {
                ShowPopup(L.T("otp.sent"), ToastType.Success);
                otpTimerHandler?.StartOTPTimerOnForgetPassword();

                if (forgotOTP_inputText != null)
                {
                    forgotOTP_inputText.interactable = true;
                    forgotOTP_inputText.Select();
                }
            }
            else
            {
                ShowPopup(message, ToastType.Error);
            }
        });
    }

    private void OnForgotVerifyClicked()
    {
        if (isProcessing) return;

        string mobile = forgotMobile_inputText?.text?.Trim() ?? "";
        string otp = forgotOTP_inputText?.text?.Trim() ?? "";

        if (!ValidateMobile(mobile))
        {
            ShowPopup(L.T("register.enter_mobile", mobileNumberLength));
            return;
        }

        if (otp.Length != otpLength)
        {
            ShowPopup(L.T("register.enter_otp", otpLength));
            ShakeInput(forgotOTP_inputText);
            return;
        }

        ShowLoading();
        APIManager.Instance?.ForgotPassword(mobile, otp, (success, message) =>
        {
            HideLoading();
            
            if (success)
            {
                ShowPopup(L.T("otp.verified"), ToastType.Success);
                UIManagerMenu.Instance?.ShowChangePasswordScreen();
            }
            else
            {
                ShowPopup(message, ToastType.Error);
            }
        });
    }

    private void OnResetPasswordClicked()
    {
        PlayButtonSound();
        if (isProcessing) return;

        string password = resetNewPassword_inputText?.text ?? "";
        string confirmPassword = resetConfirmPassword_inputText?.text ?? "";

        if (password.Length < minPasswordLength)
        {
            ShowPopup(L.T("register.password_short", minPasswordLength));
            ShakeInput(resetNewPassword_inputText);
            return;
        }

        if (password != confirmPassword)
        {
            ShowPopup(L.T("register.password_mismatch"));
            ShakeInput(resetConfirmPassword_inputText);
            resetConfirmPassword_inputText.text = "";
            return;
        }

        ShowLoading();
        APIManager.Instance?.ResetPassword(password, confirmPassword, (success, message) =>
        {
            HideLoading();
            
            if (success)
            {
                ShowPopup(L.T("reset.success"), ToastType.Success);
                DOVirtual.DelayedCall(1.5f, () => {
                    UIManagerMenu.Instance?.ShowLoginScreen();
                });
            }
            else
            {
                ShowPopup(message, ToastType.Error);
            }
        });
    }

    #endregion

    #region OTP Handling

   /* private void OnVerifyOTPClicked()
    {
        if (isProcessing) return;

        string otp = otp_inputText?.text?.Trim() ?? "";

        if (otp.Length != otpLength)
        {
            ShowPopup(L.T("register.enter_otp", otpLength));
            ShakeInput(otp_inputText);
            return;
        }

        if (currentModule == "forgot")
        {
            UIManagerMenu.Instance?.ShowChangePasswordScreen();
        }
        else
        {
            // For registration, handled in register submit
            UIManagerMenu.Instance?.OnLoginSuccess();
        }
    }

    private void OnResendOTPClicked()
    {
        if (currentModule == "register" && registerMobile_inputText != null)
        {
            OnRegisterSendOTPClicked();
        }
        else if (currentModule == "forgot" && forgotMobile_inputText != null)
        {
            OnForgotSendOTPClicked();
        }
    }*/

    #endregion

    #region Navigation

    private void OnBackToLoginClicked()
    {
        PlayButtonSound();
        UIManagerMenu.Instance?.ShowLoginScreen();
    }

    #endregion

    #region Helpers

    private bool ValidateMobile(string mobile)
    {
        if (string.IsNullOrEmpty(mobile)) return false;
        if (mobile.Length != mobileNumberLength) return false;
        
        foreach (char c in mobile)
        {
            if (!char.IsDigit(c)) return false;
        }
        
        return true;
    }

    // Default Warning: most calls here are "fix this field" validation nudges.
    // Success and hard API failures pass their own type explicitly below.
    private void ShowPopup(string message, ToastType type = ToastType.Warning)
    {
        // UIManagerMenu is scene-scoped; Toast works anywhere, so a failed
        // login/OTP is never swallowed just because the popup host is absent.
        if (UIManagerMenu.Instance != null)
            UIManagerMenu.Instance.ShowPopup(message, type);
        else
        {
            switch (type)
            {
                case ToastType.Success: Toast.Success(message); break;
                case ToastType.Error: Toast.Error(message); break;
                case ToastType.Warning: Toast.Warning(message); break;
                default: Toast.Info(message); break;
            }
        }
    }

    private void PlayButtonSound()
    {
        AudioManager.Instance?.PlayOneShot("UIButtonClick");
    }

    private void ShakeInput(TMP_InputField input)
    {
        if (input != null)
        {
            input.transform.DOShakePosition(0.3f, 10f, 20, 90f);
        }
    }

    private void ShowLoading()
    {
        isProcessing = true;
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
            
            if (loadingSpinner != null)
            {
                loadingSpinner.transform.DORotate(new Vector3(0, 0, -360), 1f, RotateMode.FastBeyond360)
                    .SetLoops(-1, LoopType.Restart)
                    .SetEase(Ease.Linear);
            }
        }
    }

    private void HideLoading()
    {
        isProcessing = false;
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
            
            if (loadingSpinner != null)
            {
                loadingSpinner.transform.DOKill();
            }
        }
    }

    private void ClearRegisterFields()
    {
        if (registerName_inputText != null) registerName_inputText.text = "";
        if (registerMobile_inputText != null) registerMobile_inputText.text = "";
        if (registerEmail_inputText != null) registerEmail_inputText.text = "";
        if (registerPassword_inputText != null) registerPassword_inputText.text = "";
        if (registerConfirmPassword_inputText != null) registerConfirmPassword_inputText.text = "";
        if (registerOTP_inputText != null) registerOTP_inputText.text = "";
        if (registerReferral_inputText != null) registerReferral_inputText.text = "";

        // Drop the previous verdict with the fields it belonged to, or a stale
        // "already taken" would greet the next person on a blank form.
        lastCheckedUsername = "";
        lastCheckedUsernameAvailable = false;
        lastCheckedUsernameMessage = "";
        SetUsernameStatus("", usernameCheckingColor);
    }

    private void ClearForgotFields()
    {
        if (forgotMobile_inputText != null) forgotMobile_inputText.text = "";
        if (forgotOTP_inputText != null) forgotOTP_inputText.text = "";
    }

    public void ClearAllInputFields()
    {
        ClearRegisterFields();
        ClearForgotFields();
        
        if (loginCredential_inputText != null) loginCredential_inputText.text = "";
        if (loginPass_inputText != null) loginPass_inputText.text = "";
        if (resetNewPassword_inputText != null) resetNewPassword_inputText.text = "";
        if (resetConfirmPassword_inputText != null) resetConfirmPassword_inputText.text = "";
        //if (otp_inputText != null) otp_inputText.text = "";
    }

    #endregion
}
