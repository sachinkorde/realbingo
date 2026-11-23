using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using RealBingo.Localization;
using UnityEngine.UI;

public class OTPTimerHandler : MonoBehaviour
{
    public static OTPTimerHandler Instance { get; private set; }

    public LoginHandler loginHandler;

    [Header("Timer UI References")]
    [SerializeField] private TMP_Text registertimerText;
    [SerializeField] private TMP_Text ForgetPasstimerText;

    [SerializeField] private Button resendOtpRegisterButton;
    [SerializeField] private Button resendOtpForgetAccButton;

    [SerializeField] private TMP_InputField otpCreateAccText;
    [SerializeField] private TMP_InputField otpForgetPassText;

    [Header("Timer Settings")]
    [SerializeField] private int totalTimeInSeconds = 120;

    private Coroutine timerCoroutine;

    // UIManagerMenu.Instance can be null this early in scene load — these are
    // validation nudges, so Warning severity; falls back to Toast either way
    // rather than throwing a NullReferenceException that silently drops the
    // message and skips the rest of the calling method.
    private void ShowPopup(string message)
    {
        if (UIManagerMenu.Instance != null)
            UIManagerMenu.Instance.ShowPopup(message, ToastType.Warning);
        else
            Toast.Warning(message);
    }

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

    private void OnDestroy()
    {
        // C#'s ?. operator bypasses Unity's == null override, so a static left
        // pointing at a destroyed object gets called into. Drop it here.
        if (Instance == this) Instance = null;
    }

    public void StartOTPTimerOnRegister()
    {
        // 1. Basic Empty Check
        if (string.IsNullOrEmpty(loginHandler.registerName_inputText.text) ||
            string.IsNullOrEmpty(loginHandler.registerMobile_inputText.text) ||
            string.IsNullOrEmpty(loginHandler.registerPassword_inputText.text) ||
            string.IsNullOrEmpty(loginHandler.registerConfirmPassword_inputText.text))
        {
            ShowPopup(L.T("register.fill_all"));
            return;
        }

        // 2. Mobile Number Validation (Optional but recommended)
        if (loginHandler.registerMobile_inputText.text.Length < 10)
        {
            ShowPopup(L.T("register.enter_mobile", 10));
            return;
        }

        // 4. Password Length Check (Security best practice)
        if (loginHandler.registerPassword_inputText.text.Length < 6)
        {
            ShowPopup(L.T("register.password_short", 6));
            return;
        }

        // 3. PASSWORD MATCH CHECK
        if (loginHandler.registerPassword_inputText.text != loginHandler.registerConfirmPassword_inputText.text)
        {
            ShowPopup(L.T("register.password_mismatch"));

            // Optional: Clear the confirm field so they can type it again
            loginHandler.registerConfirmPassword_inputText.text = "";
            return;
        }

        resendOtpRegisterButton.interactable = false;
        resendOtpRegisterButton.gameObject.SetActive(false);

        if (timerCoroutine != null)
        {
            StopCoroutine(timerCoroutine);
        }

        timerCoroutine = StartCoroutine(TimerRoutineOnRegister());
    }

    public void StartOTPTimerOnForgetPassword()
    {
        if (loginHandler.forgotMobile_inputText.text.Length < 10)
        {
            ShowPopup(L.T("register.enter_mobile", 10));
            return;
        }

        resendOtpRegisterButton.interactable = false;
        resendOtpRegisterButton.gameObject.SetActive(false);

        if (timerCoroutine != null)
        {
            StopCoroutine(timerCoroutine);
        }
        timerCoroutine = StartCoroutine(TimerRoutineOnForgetPassword());
    }

    private IEnumerator TimerRoutineOnRegister()
    {
        resendOtpRegisterButton.gameObject.SetActive(true);
        int remaining = totalTimeInSeconds;
        //GenerateOTP();
        while (remaining > 0)
        {
            int minutes = remaining / 60;
            int seconds = remaining % 60;
            registertimerText.text = $"{minutes:00}:{seconds:00}";

            yield return new WaitForSeconds(1f);
            remaining--;
        }

        // Timer finished. This is a resend COOLDOWN, not confirmed proof the
        // backend OTP itself has expired (that TTL is set separately, server
        // side) — so the message only says what is actually true here.
        registertimerText.text = "";
        resendOtpRegisterButton.gameObject.SetActive(true);
        resendOtpRegisterButton.interactable = true;

        ShowPopup(L.T("otp.resend_now"));
    }

    private IEnumerator TimerRoutineOnForgetPassword()
    {
        ForgetPasstimerText.gameObject.SetActive(true);
        int remaining = totalTimeInSeconds;
        //GenerateOTPOnForgetPassword();
        while (remaining > 0)
        {
            int minutes = remaining / 60;
            int seconds = remaining % 60;
            ForgetPasstimerText.text = $"{minutes:00}:{seconds:00}";
            yield return new WaitForSeconds(1f);
            remaining--;
        }
        // Timer finished — see the comment in TimerRoutineOnRegister above.
        ForgetPasstimerText.text = "";
        resendOtpForgetAccButton.gameObject.SetActive(true);
        resendOtpForgetAccButton.interactable = true;
        ShowPopup(L.T("otp.resend_now"));
    }

    /*void GenerateOTP()
    {
        List<int> uniqueNumbers = new List<int>();
        while (uniqueNumbers.Count < 6)
        {
            int num = Random.Range(0, 10);
            if (!uniqueNumbers.Contains(num))
                uniqueNumbers.Add(num);
        }

        string result = string.Join("", uniqueNumbers);
        Debug.Log("Unique 6 Numbers: " + result);

        otpCreateAccText.text = result;
    }*/

    /*public void ResendOTP()
    {
        if(loginHandler.registerMobile_inputText.text.Length < 10)
        {
            UIManagerMenu.Instance.ShowPopup(L.T("register.enter_mobile", 10));
            return;
        }

    }*/

    /*public void GenerateOTPOnForgetPassword()
    {
        List<int> uniqueNumbers = new List<int>();
        while (uniqueNumbers.Count < 6)
        {
            int num = Random.Range(0, 10);
            if (!uniqueNumbers.Contains(num))
                uniqueNumbers.Add(num);
        }

        string result = string.Join("", uniqueNumbers);
        Debug.Log("Unique 6 Numbers: " + result);

        otpForgetPassText.text = result;
    }*/

    /*public void OnClick_ResendOTP()
    {
        Debug.Log("Resend OTP clicked!");
        StartOTPTimerOnRegister();
        GenerateOTP();
    }

    public void OnClick_ResendOTPOnForgetPassword()
    {
        Debug.Log("Resend OTP clicked!");
        StartOTPTimerOnForgetPassword();
        //GenerateOTPOnForgetPassword();
    }*/
}
