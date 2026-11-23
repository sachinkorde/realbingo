using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using RealBingo.Localization;
using static UnityEngine.AudioSettings;
using Debug = UnityEngine.Debug;

/// <summary>
/// Centralized API Manager for all backend communications
/// Handles: Auth, Profile, Wallet, and Game APIs
/// </summary>
/// 

/// <summary>
/// Forces Unity to accept the SSL certificate, bypassing the Curl 60 error.
/// WARNING: Use for development/debugging, or if you strictly trust your server.
/// </summary>
public class BypassCertificateHandler : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData)
    {
        // Returning true tells Unity "Yes, this certificate is fine, keep going!"
        return true;
    }
}

public class APIManager : MonoBehaviour
{
    public static APIManager Instance { get; private set; }

    [Header("API Configuration")]
    // OLD API (kept for reference — do not delete):
    // [SerializeField] private string baseUrl = "https://admin.therealbingo.com/api";
    //
    // Backend (Laravel, source at D:\commercialProject\bingo-backend).
    // NOTE: this is only the default for NEW instances — the value actually
    // used at runtime is the one serialised in the scene's Inspector.
    // Set it there for your test target:
    //   • Live (Render)          -> https://bingo-backend-bjqq.onrender.com/api
    //   • Unity Editor (PC)      -> http://127.0.0.1:8000/api
    //   • Android emulator       -> http://10.0.2.2:8000/api
    //   • Real device on LAN     -> http://<YOUR-PC-LAN-IP>:8000/api
    [SerializeField] private string baseUrl = "https://bingo-backend-bjqq.onrender.com/api";

    // 60s, not 30s: the free host spins down when idle and the first request
    // after a cold start can take ~50 seconds to answer.
    [SerializeField] private float requestTimeout = 60f;

    [Header("Auth Token")]
    [SerializeField] private string authToken = "";
    
    private const string TOKEN_KEY = "auth_token";
    private const string USER_DATA_KEY = "user_data";

    // The LoginHandler lives in the Main scene, while this APIManager is
    // DontDestroyOnLoad and outlives it. A plain serialised reference therefore
    // dies the moment Main is unloaded, and logout -> LoadScene("Main") does NOT
    // restore it: the reloaded scene's own APIManager (which has the fresh
    // reference) is destroyed by the duplicate check in Awake, so the surviving
    // instance keeps pointing at the dead object forever.
    //
    // The Inspector value is therefore only a starting hint; every read goes
    // through the loginHandler property, which re-resolves against the live scene.
    [SerializeField, FormerlySerializedAs("loginHandler")]
    private LoginHandler loginHandlerRef;

    /// <summary>
    /// The LoginHandler in the currently loaded scene, or null when that scene
    /// has none (e.g. while GameScene is up). Survives scene reloads.
    /// </summary>
    public LoginHandler loginHandler
    {
        get
        {
            // Unity's == already reports null for a DESTROYED MonoBehaviour, so
            // this catches the stale-after-unload case too, not just "never set".
            if (loginHandlerRef == null)
            {
                loginHandlerRef = LoginHandler.Instance != null
                    ? LoginHandler.Instance
                    : FindAnyObjectByType<LoginHandler>(FindObjectsInactive.Include);
            }
            return loginHandlerRef;
        }
        set { loginHandlerRef = value; }
    }

    /// <summary>
    /// Called by LoginHandler.Awake, so the surviving APIManager is re-wired to
    /// whichever LoginHandler the freshly loaded scene created.
    /// </summary>
    public void RegisterLoginHandler(LoginHandler handler)
    {
        loginHandlerRef = handler;
    }

    /// <summary>
    /// Called by LoginHandler.OnDestroy, so we never keep a dead reference.
    /// </summary>
    public void UnregisterLoginHandler(LoginHandler handler)
    {
        if (loginHandlerRef == handler)
            loginHandlerRef = null;
    }

    #region Events
    
    public event Action<bool, string> OnLoginComplete;
    public event Action<bool, string> OnRegisterComplete;
    public event Action<bool, string> OnOTPSent;
    public event Action<bool, string> OnOTPVerified;
    public event Action<bool, string> OnPasswordReset;
    public event Action<bool, string> OnProfileFetched;
    public event Action<bool, string> OnProfileUpdated;
    public event Action<bool, string> OnLogoutComplete;

    public event Action<bool, string> OnBankDetailsFetched;

    #endregion

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Load saved token
        authToken = PlayerPrefs.GetString(TOKEN_KEY, "");

        TouchScreenKeyboard.hideInput = false;
    }

    private void OnDestroy()
    {
        // C#'s ?. operator bypasses Unity's == null override, so a static left
        // pointing at a destroyed object gets called into. Drop it here.
        if (Instance == this) Instance = null;
    }

    #region Authentication APIs

    /// <summary>
    /// Login with mobile/email and password
    /// </summary>
    public void Login(string credential, string password, Action<bool, string, LoginData> callback = null)
    {
        Debug.Log($"Login called with credential: {credential}");
        StartCoroutine(LoginCoroutine(credential, password, callback));
    }

    private IEnumerator LoginCoroutine(string credential, string password, Action<bool, string, LoginData> callback)
    {
        Debug.Log($"Attempting login with credential: {credential}");
        string url = $"{baseUrl}/login";

        WWWForm form = new WWWForm();
        form.AddField("credential", credential);
        form.AddField("password", password);

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    LoginResponse response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
                    
                    if (!string.IsNullOrEmpty(response.data.token))
                    {
                        authToken = response.data.token;

                        Debug.Log(authToken);
                        PlayerPrefs.SetString(TOKEN_KEY, authToken);
                        PlayerPrefs.Save();

                        //PlayerPrefs.SetString(UIManagerMenu.Mobile_Number, response.data.mobile);

                        //Debug.Log(PlayerPrefs.GetString(UIManagerMenu.Mobile_Number) + " Registreted mobile number");

                        Debug.Log(PlayerPrefs.GetString(TOKEN_KEY));
                        callback?.Invoke(true, "Login successful!", response.data);
                        OnLoginComplete?.Invoke(true, "Login successful!");
                    }
                    else
                    {
                        string loginFailMsg = ParseErrorMessage(request);
                        callback?.Invoke(false, loginFailMsg, null);
                        OnLoginComplete?.Invoke(false, loginFailMsg);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Login parse error: {e.Message}");
                    callback?.Invoke(false, "Something went wrong. Please try again.", null);
                    OnLoginComplete?.Invoke(false, "Something went wrong. Please try again.");
                }
            }
            else
            {
                string errorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, errorMsg, null);
                OnLoginComplete?.Invoke(false, errorMsg);
            }
        }
    }

    /// <summary>
    /// Register new user
    /// </summary>
    public void Register(string mobile, string email, string password, string confirmPassword, string otp, string username,
                         Action<bool, string> callback = null, string referral = "")
    {
        StartCoroutine(RegisterCoroutine(mobile, email, password, confirmPassword, otp, username, callback, referral));
    }

    private IEnumerator RegisterCoroutine(string mobile, string email, string password,
                                           string confirmPassword, string otp, string username, Action<bool, string> callback,
                                           string referral = "")
    {
        string url = $"{baseUrl}/register";

        WWWForm form = new WWWForm();
        form.AddField("username", username);
        form.AddField("mobile", mobile);
        if (!string.IsNullOrWhiteSpace(referral)) form.AddField("referral", referral.Trim());
        form.AddField("email", email);
        form.AddField("password", password);
        form.AddField("confirm_password", confirmPassword);
        form.AddField("otp", otp);

        Debug.LogFormat("Register called with:", mobile, email, password.Length, confirmPassword.Length, otp);

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            yield return request.SendWebRequest();

            // Guard: parse defensively so a network failure / non-JSON body
            // (timeout, 500, HTML error page) can't throw a NullReferenceException.
            RegisterResponse response = null;
            try
            {
                response = JsonUtility.FromJson<RegisterResponse>(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"Register parse error: {e.Message}");
            }

            Debug.Log($"Register response: {request.downloadHandler.text}");

            if (response == null)
            {
                string parseErrorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, parseErrorMsg);
                OnRegisterComplete?.Invoke(false, parseErrorMsg);
                yield break;
            }

            if (response.success) // == UnityWebRequest.Result.Success)
            {
                try
                {
                    if (response.data != null && !string.IsNullOrEmpty(response.data.token) && response.success)
                    {
                        authToken = response.data.token;
                        PlayerPrefs.SetString(TOKEN_KEY, authToken);
                        PlayerPrefs.Save();
                    }
                    
                    callback?.Invoke(true, "Registration successful!");
                    OnRegisterComplete?.Invoke(true, "Registration successful!");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Register parse error: {e.Message}");
                    callback?.Invoke(false, "Something went wrong. Please try again.");
                    OnRegisterComplete?.Invoke(false, "Something went wrong. Please try again.");
                }
            }
            else
            {
                Debug.LogError($"Register request error: {request.error}, Response code: {request.responseCode}");
                string errorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, errorMsg);
                OnRegisterComplete?.Invoke(false, errorMsg);
            }
        }
    }

    /// <summary>
    /// Check whether a username is still free, BEFORE registering.
    ///
    /// GET /check-username?username=... — public, no auth token (the player has
    /// no account yet). The backend answers with the normal app envelope:
    ///     available -> {"success":true, "message":"Username available!", "data":null}
    ///     taken     -> {"success":false,"message":"<reason>", ...}
    ///
    /// callback: (requestOk, isAvailable, message)
    ///   requestOk   - false only when the SERVER was never reached / never
    ///                 answered. The caller must NOT treat this as "taken":
    ///                 a dead connection says nothing about the name. Let the
    ///                 player continue and rely on /register's own uniqueness
    ///                 check, which is the real gate.
    ///   isAvailable - meaningful only when requestOk is true.
    ///   message     - the server's own wording when it sent some, otherwise a
    ///                 player-readable failure from ParseErrorMessage.
    /// </summary>
    public void CheckUsername(string username, Action<bool, bool, string> callback)
    {
        StartCoroutine(CheckUsernameCoroutine(username, callback));
    }

    private IEnumerator CheckUsernameCoroutine(string username, Action<bool, bool, string> callback)
    {
        string url = $"{baseUrl}/check-username?username={UnityWebRequest.EscapeURL(username)}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;

            yield return request.SendWebRequest();

            CheckUsernameResponse resp = null;
            try { resp = JsonUtility.FromJson<CheckUsernameResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Check username parse error: {e.Message}"); }

            // A "taken" answer is a NORMAL, successful round trip that happens
            // to carry success:false — it arrives as 200 or as 422, so the HTTP
            // code alone cannot be used to tell it apart from a real outage.
            // The envelope check is what distinguishes them: only our backend
            // writes a "success" key, so if it is there the server spoke.
            bool serverAnswered = resp != null && IsAppEnvelope(request.downloadHandler?.text);

            if (!serverAnswered)
            {
                callback?.Invoke(false, false, ParseErrorMessage(request));
                yield break;
            }

            callback?.Invoke(true, resp.success,
                string.IsNullOrEmpty(resp.message)
                    ? (resp.success ? "Username available!" : "That username is already taken.")
                    : resp.message);
        }
    }

    /// <summary>
    /// Send OTP for various modules (register, forgot, verify)
    /// </summary>
    public void SendOTP(string mobile, string module, Action<bool, string> callback = null)
    {
        StartCoroutine(SendOTPCoroutine(mobile, module, callback));
    }

    private IEnumerator SendOTPCoroutine(string mobile, string module, Action<bool, string> callback)
    {
        string url = $"{baseUrl}/send-otp";

        WWWForm form = new WWWForm();
        form.AddField("mobile", mobile.Trim());
        form.AddField("module", module);

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            yield return request.SendWebRequest();

            // Guard: parse defensively so a network failure / non-JSON body
            // can't throw a NullReferenceException on response.success.
            OTPResponse response = null;
            try
            {
                response = JsonUtility.FromJson<OTPResponse>(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"OTP parse error: {e.Message}");
            }

            if (response == null)
            {
                string parseErrorMsg = request.responseCode == 422
                    ? "Mobile number not registered or invalid!"
                    : ParseErrorMessage(request);
                callback?.Invoke(false, parseErrorMsg);
                OnOTPSent?.Invoke(false, parseErrorMsg);
                yield break;
            }

            if (response.success)
            {
                Debug.Log($"OTP sent successfully to {mobile} for module {module}");
                //Debug.Log($"Server response: {request.downloadHandler.text}");
                Debug.Log($"Response code: {response.data}");


                // DEV CONVENIENCE: in non-production the backend returns the OTP in
                // `data`, so we log it to the Console AND auto-fill the OTP box.
                // In PRODUCTION the backend returns 0/null here, so nothing is
                // shown or filled — the user types the code they get by SMS.
                if (response.data > 0)
                {
                    Debug.Log($"[DEV] OTP for {mobile} ({module}): {response.data}");

                    switch (module)
                    {
                        case "register":
                            if (loginHandler != null && loginHandler.registerOTP_inputText != null)
                                loginHandler.registerOTP_inputText.text = response.data.ToString();
                            break;

                        case "forgot_password":
                            if (loginHandler != null && loginHandler.forgotOTP_inputText != null)
                                loginHandler.forgotOTP_inputText.text = response.data.ToString();
                            break;
                    }
                }

                callback?.Invoke(true, "OTP sent successfully!");
                OnOTPSent?.Invoke(true, "OTP sent successfully!");
            }
            else if (request.responseCode == 422)
            {
                string errorMsg = "Mobile number not registered or invalid!";
                callback?.Invoke(false, errorMsg);
                OnOTPSent?.Invoke(false, errorMsg);
            }
            else
            {
                string errorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, errorMsg);
                OnOTPSent?.Invoke(false, errorMsg);
            }
        }
    }

    /// <summary>
    /// Forgot password - verify OTP
    /// </summary>
    public void ForgotPassword(string mobile, string otp, Action<bool, string> callback = null)
    {
        StartCoroutine(ForgotPasswordCoroutine(mobile, otp, callback));
    }
    public string tempTokenforResetpassword;
    private IEnumerator ForgotPasswordCoroutine(string mobile, string otp, Action<bool, string> callback)
    {
        string url = $"{baseUrl}/forgot-password";

        WWWForm form = new WWWForm();
        form.AddField("mobile", mobile);
        form.AddField("otp", otp);

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            yield return request.SendWebRequest();
            PasswordResponse response = JsonUtility.FromJson<PasswordResponse>(request.downloadHandler.text);
            Debug.Log(response.data.token);
            tempTokenforResetpassword = response.data.token;
            
            
            if (request.result == UnityWebRequest.Result.Success && response.success)
            {
                
                try
                {
                    callback?.Invoke(true, "OTP verified!");
                    OnOTPVerified?.Invoke(true, "OTP verified!");
                }
                catch
                {
                    callback?.Invoke(true, "OTP verified!");
                    OnOTPVerified?.Invoke(true, "OTP verified!");
                }
            }
            else
            {
                string errorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, errorMsg);
                OnOTPVerified?.Invoke(false, errorMsg);
            }
        }
    }

    /// <summary>
    /// Reset password with new password
    /// </summary>
    public void ResetPassword(string password, string confirmPassword, Action<bool, string> callback = null)
    {
        StartCoroutine(ResetPasswordCoroutine(password, confirmPassword, callback));
    }

    private IEnumerator ResetPasswordCoroutine(string password, string confirmPassword, Action<bool, string> callback)
    {
        string url = $"{baseUrl}/reset-password";

        WWWForm form = new WWWForm();
        form.AddField("password", password);
        form.AddField("confirm_password", confirmPassword);

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            authToken = tempTokenforResetpassword;
            if (!string.IsNullOrEmpty(authToken))
            {
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");
            }
            
            yield return request.SendWebRequest();
            PasswordResponse response = JsonUtility.FromJson<PasswordResponse>(request.downloadHandler.text);
            if (request.result == UnityWebRequest.Result.Success && response.success)   
            {
                Debug.Log(response.data.token);
                callback?.Invoke(true, "Password reset successful!");
                OnPasswordReset?.Invoke(true, "Password reset successful!");
            }
            else
            {
                string errorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, errorMsg);
                OnPasswordReset?.Invoke(false, errorMsg);
            }
        }
    }

    /// <summary>
    /// Logout current session
    /// </summary>
    public void Logout(Action<bool, string> callback = null)
    {
        StartCoroutine(LogoutCoroutine(callback));
    }

    private IEnumerator LogoutCoroutine(Action<bool, string> callback)
    {
        string url = $"{baseUrl}/logout";

        using (UnityWebRequest request = UnityWebRequest.Post(url, new WWWForm()))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");
            
            yield return request.SendWebRequest();

            // Clear local data regardless of server response. The cached
            // balance goes first: its key is derived from the token.
            WalletManager.ForgetCachedBalance();
            authToken = "";
            PlayerPrefs.DeleteKey(TOKEN_KEY);
            PlayerPrefs.DeleteKey(USER_DATA_KEY);
            PlayerPrefs.Save();

            callback?.Invoke(true, "Logged out successfully!");
            OnLogoutComplete?.Invoke(true, "Logged out successfully!");
        }
    }

    /// <summary>
    /// Logout from all devices
    /// </summary>
    public void LogoutAll(Action<bool, string> callback = null)
    {
        StartCoroutine(LogoutAllCoroutine(callback));
    }

    private IEnumerator LogoutAllCoroutine(Action<bool, string> callback)
    {
        string url = $"{baseUrl}/logout-all";

        using (UnityWebRequest request = UnityWebRequest.Post(url, new WWWForm()))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");
            
            yield return request.SendWebRequest();

            // Clear local data. The cached balance goes first: its key is
            // derived from the token.
            WalletManager.ForgetCachedBalance();
            authToken = "";
            PlayerPrefs.DeleteKey(TOKEN_KEY);
            PlayerPrefs.DeleteKey(USER_DATA_KEY);
            PlayerPrefs.Save();

            callback?.Invoke(true, "Logged out from all devices!");
            OnLogoutComplete?.Invoke(true, "Logged out from all devices!");
        }
    }

    #endregion

    #region Profile APIs

    /// <summary>
    /// Get user profile
    /// </summary>
    /// <summary>
    /// callback: (ok, data, responseCode). responseCode lets the caller tell a
    /// genuinely dead token (401) apart from "the server didn't answer" (0, or
    /// a timeout during a free-tier cold start) — those need very different
    /// handling: one should log the player out, the other should not.
    /// </summary>
    public void GetProfile(Action<bool, GetProfileResponse, long> callback = null)
    {
        StartCoroutine(GetProfileCoroutine(callback));
    }

    private IEnumerator GetProfileCoroutine(Action<bool, GetProfileResponse, long> callback)
    {
        // Pass mobile as a query parameter for a GET request
        string url = $"{baseUrl}/profile?mobile=&module=register";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();


            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, null, request.responseCode);
                OnProfileFetched?.Invoke(false, errorMsg);
                yield break;
            }

            try
            {
                GetProfileResponse getProfileResponse = JsonUtility.FromJson<GetProfileResponse>(request.downloadHandler.text);

                if (getProfileResponse.success)
                {
                    Debug.Log($"Profile data: {request.downloadHandler.text}");

                    // Actually invoke the callback with the data
                    callback?.Invoke(true, getProfileResponse, request.responseCode);
                    OnProfileFetched?.Invoke(true, "Profile loaded!");
                }
                else
                {
                    callback?.Invoke(false, null, request.responseCode);
                    OnProfileFetched?.Invoke(false, "Profile request returned failure");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Profile parse error: {e.Message}");
                callback?.Invoke(false, null, request.responseCode);
                OnProfileFetched?.Invoke(false, "Failed to load profile");
            }
        }
    }

    /// <summary>
    /// Update user profile
    /// </summary>
    public void UpdateProfile(string name, string gender, string dateOfBirth, string address,
                              string adharNumber, string panNumber, byte[] profileImage = null,
                              byte[] adharDocument = null, byte[] panDocument = null,
                              Action<bool, string> callback = null)
    {
        StartCoroutine(UpdateProfileCoroutine(name, gender, dateOfBirth, address, 
                                               adharNumber, panNumber, profileImage, 
                                               adharDocument, panDocument, callback));
    }

    private IEnumerator UpdateProfileCoroutine(string name, string gender, string dateOfBirth, 
                                                string address, string adharNumber, string panNumber,
                                                byte[] profileImage, byte[] adharDocument, byte[] panDocument,
                                                Action<bool, string> callback)
    {
        string url = $"{baseUrl}/profile";

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
        
        if (!string.IsNullOrEmpty(name)) formData.Add(new MultipartFormDataSection("name", name));
        if (!string.IsNullOrEmpty(gender)) formData.Add(new MultipartFormDataSection("gender", gender));
        if (!string.IsNullOrEmpty(dateOfBirth)) formData.Add(new MultipartFormDataSection("date_of_birth", dateOfBirth));
        if (!string.IsNullOrEmpty(address)) formData.Add(new MultipartFormDataSection("address", address));
        if (!string.IsNullOrEmpty(adharNumber)) formData.Add(new MultipartFormDataSection("adhar_number", adharNumber));
        if (!string.IsNullOrEmpty(panNumber)) formData.Add(new MultipartFormDataSection("pan_number", panNumber));
        
        if (profileImage != null && profileImage.Length > 0)
            formData.Add(new MultipartFormFileSection("profile_image", profileImage, "profile.jpg", "image/jpeg"));
        if (adharDocument != null && adharDocument.Length > 0)
            formData.Add(new MultipartFormFileSection("adhar_document", adharDocument, "adhar.pdf", "application/pdf"));
        if (panDocument != null && panDocument.Length > 0)
            formData.Add(new MultipartFormFileSection("pan_document", panDocument, "pan.pdf", "application/pdf"));

        using (UnityWebRequest request = UnityWebRequest.Post(url, formData))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");
            
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                callback?.Invoke(true, "Profile updated successfully!");
                OnProfileUpdated?.Invoke(true, "Profile updated successfully!");
            }
            else
            {
                string errorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, errorMsg);
                OnProfileUpdated?.Invoke(false, errorMsg);
            }
        }
    }

    #endregion

    #region Bank APIs
    public void GetBankDetails(Action<bool, BankDetailsResponse> callback = null)
    {
        StartCoroutine(BankDetailsCoroutine(callback));
    }

    private IEnumerator BankDetailsCoroutine(Action<bool, BankDetailsResponse> callback)
    {
        // Pass mobile as a query parameter for a GET request
        string url = $"{baseUrl}/bank-detail?module=register";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();
            
            Debug.Log($"Bank details response: {request.downloadHandler.text}");

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorMsg = ParseErrorMessage(request);
                callback?.Invoke(false, null);
                OnBankDetailsFetched?.Invoke(false, errorMsg);
                yield break;
            }

            try
            {
                BankDetailsResponse bankDetailsResponse = JsonUtility.FromJson<BankDetailsResponse>(request.downloadHandler.text);

                if (bankDetailsResponse.success)
                {
                    Debug.Log($"Profile data: {request.downloadHandler.text}");

                    // Actually invoke the callback with the data
                    callback?.Invoke(true, bankDetailsResponse);
                    OnBankDetailsFetched?.Invoke(true, "Bank details loaded!");
                }
                else
                {
                    callback?.Invoke(false, null);
                    OnBankDetailsFetched?.Invoke(false, "Bank details load failure");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Details parse error: {e.Message}");
                callback?.Invoke(false, null);
                OnBankDetailsFetched?.Invoke(false, "Failed to load bank details");
            }
        }
    }

    /// <summary>
    /// Edit/Update bank details
    /// </summary>
    public void EditBankDetails(string bankName, string branch, string accountHolderName, string accountNumber,
                                string ifscCode, string upiId, byte[] document = null,
                                Action<bool, EditBankDetailsResponce> callback = null)
    {
        StartCoroutine(EditBankDetailsCoroutine(bankName, branch, accountHolderName, accountNumber,
                                                 ifscCode, upiId, document, callback));
    }

    private IEnumerator EditBankDetailsCoroutine(string bankName, string branch, string accountHolderName,
                                                  string accountNumber, string ifscCode,
                                                  string upiId, byte[] document,
                                                  Action<bool, EditBankDetailsResponce> callback)
    {
        string url = $"{baseUrl}/bank-detail";

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();

        if (!string.IsNullOrEmpty(bankName))
            formData.Add(new MultipartFormDataSection("name", bankName));
        if (!string.IsNullOrEmpty(branch))
            formData.Add(new MultipartFormDataSection("branch", branch));
        if (!string.IsNullOrEmpty(accountHolderName))
            formData.Add(new MultipartFormDataSection("account_holder_name", accountHolderName));
        if (!string.IsNullOrEmpty(accountNumber))
            formData.Add(new MultipartFormDataSection("account_number", accountNumber));
        if (!string.IsNullOrEmpty(ifscCode))
            formData.Add(new MultipartFormDataSection("ifsc_code", ifscCode));
        if (!string.IsNullOrEmpty(upiId))
            formData.Add(new MultipartFormDataSection("upi_id", upiId));

        if (document != null && document.Length > 0)
            formData.Add(new MultipartFormFileSection("document", document, "document.pdf", "application/pdf"));

        using (UnityWebRequest request = UnityWebRequest.Post(url, formData))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            Debug.Log($"Edit bank details response: {request.downloadHandler.text}");

            //if (request.result == UnityWebRequest.Result.Success)
            //{
                try
                {
                    EditBankDetailsResponce response =
                        JsonUtility.FromJson<EditBankDetailsResponce>(request.downloadHandler.text);

                    if (response.success)
                    {
                        callback?.Invoke(true, response);

                        Debug.Log("Success");
                    }
                    else
                    {
                        // response.message is only trustworthy when it truly
                        // came from our app's envelope — see ParseErrorMessage.
                        response.message = ParseErrorMessage(request);
                        callback?.Invoke(false, response);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Edit bank details parse error: {e.Message}");

                    callback?.Invoke(false, null);
                }
            //}
            //else
            /*{
                string errorMsg = ParseErrorMessage(request);
                Debug.LogError($"Edit bank details error: {errorMsg}");
                callback?.Invoke(false, null);
            }*/
        }
    }
    #endregion

    #region Add Amount API

    public void AddAmount(long amount, Action<bool, AddAmountResponse> callback = null)
    {
        StartCoroutine(AddAmountCoroutine(amount, callback));
    }

    private IEnumerator AddAmountCoroutine(long amount, Action<bool, AddAmountResponse> callback)
    {
        string url = $"{baseUrl}/add-amount";

        WWWForm form = new WWWForm();
        form.AddField("source", "bank");
        form.AddField("amount", amount.ToString());

        Debug.Log($"Add Amount called with amount: {amount}");

        Debug.Log(url);

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            Debug.Log($"Add Amount response: {request.downloadHandler.text}");

            // Guard: parse defensively so a network failure / non-JSON body
            // can't throw a NullReferenceException on response.success.
            AddAmountResponse response = null;
            try
            {
                response = JsonUtility.FromJson<AddAmountResponse>(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"Add Amount parse error: {e.Message}");
            }

            if (response == null || !response.success)
            {
                // response.message is only trustworthy when it genuinely came
                // from our app's envelope — a route missing on the deployed
                // server, a 5xx page, etc. would otherwise parse cleanly too
                // and hand WalletManager raw backend text to display verbatim.
                response ??= new AddAmountResponse();
                response.success = false;
                response.message = ParseErrorMessage(request);
                callback?.Invoke(false, response);
                yield break;
            }

            callback?.Invoke(true, response);

            /*if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    AddAmountResponse response = JsonUtility.FromJson<AddAmountResponse>(request.downloadHandler.text);

                    if (response.success)
                    {
                        callback?.Invoke(true, response);
                    }
                    else
                    {
                        callback?.Invoke(false, response);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Add Amount parse error: {e.Message}");
                    callback?.Invoke(false, null);
                }
            }
            else
            {
                string errorMsg = ParseErrorMessage(request);
                Debug.LogError($"Add Amount error: {errorMsg}");
                callback?.Invoke(false, null);
            }*/
        }
    }

    #endregion

    #region Game APIs (NEW backend — server-authoritative rounds, bets, wallet)

    /// <summary>GET the current synchronized round (timer + slot id) from the server.</summary>
    public void GetCurrentRound(Action<bool, RoundData> callback)
    {
        StartCoroutine(GetCurrentRoundCoroutine(callback));
    }

    private IEnumerator GetCurrentRoundCoroutine(Action<bool, RoundData> callback)
    {
        string url = $"{baseUrl}/round/current";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                callback?.Invoke(false, null);
                yield break;
            }

            RoundResponse resp = null;
            try { resp = JsonUtility.FromJson<RoundResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Round parse error: {e.Message}"); }

            if (resp == null || !resp.success) { callback?.Invoke(false, null); yield break; }
            callback?.Invoke(true, resp.data);
        }
    }

    /// <summary>POST the player's bets for a session. slotNo = clock-derived session number. bids = bet target -> amount, where BetManager.JokerNumber is sent as "j".</summary>
    public void PlaceBid(long slotNo, Dictionary<int, long> bids, Action<bool, string, PlaceBidData> callback)
    {
        StartCoroutine(PlaceBidCoroutine(slotNo, bids, callback));
    }

    private IEnumerator PlaceBidCoroutine(long slotNo, Dictionary<int, long> bids, Action<bool, string, PlaceBidData> callback)
    {
        string url = $"{baseUrl}/place-bid";

        // Build the bids payload the backend expects. Numbers go out under their
        // own digit; the Joker is the string "j", not its internal index:
        //     [{"j":100,"0":50,"1":20}]
        System.Text.StringBuilder sb = new System.Text.StringBuilder("[{");
        bool first = true;
        foreach (var kvp in bids)
        {
            if (kvp.Value <= 0) continue;
            if (!first) sb.Append(",");
            sb.Append($"\"{BetManager.WireKey(kvp.Key)}\":{kvp.Value}");
            first = false;
        }
        sb.Append("}]");

        WWWForm form = new WWWForm();
        form.AddField("slot_no", slotNo.ToString());
        form.AddField("bids", sb.ToString());

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            PlaceBidResponse resp = null;
            try { resp = JsonUtility.FromJson<PlaceBidResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"PlaceBid parse error: {e.Message}"); }

            // resp.message is only trustworthy on success — a route that
            // doesn't exist yet, a 5xx page, etc. would otherwise parse
            // cleanly here too and hand the player raw backend wording.
            if (resp == null || !resp.success)
            {
                callback?.Invoke(false, ParseErrorMessage(request), null);
                yield break;
            }
            callback?.Invoke(true, resp.message, resp.data);
        }
    }

    /// <summary>GET the result (winning number + your payout) of a settled round.</summary>
    public void GetRoundResult(int slotId, Action<bool, RoundResultData> callback)
    {
        StartCoroutine(GetRoundResultCoroutine(slotId, callback));
    }

    private IEnumerator GetRoundResultCoroutine(int slotId, Action<bool, RoundResultData> callback)
    {
        string url = $"{baseUrl}/round/{slotId}/result";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                callback?.Invoke(false, null);
                yield break;
            }

            RoundResultResponse resp = null;
            try { resp = JsonUtility.FromJson<RoundResultResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Round result parse error: {e.Message}"); }

            if (resp == null || !resp.success) { callback?.Invoke(false, null); yield break; }
            callback?.Invoke(true, resp.data);
        }
    }

    /// <summary>
    /// GET the latest published Android build. PUBLIC — deliberately does not
    /// send the auth token, because a build old enough to be blocked may not
    /// be able to log in at all, and the version gate has to run before that.
    /// </summary>
    public void GetAppVersion(Action<bool, AppVersionData> callback)
    {
        StartCoroutine(GetAppVersionCoroutine(callback));
    }

    private IEnumerator GetAppVersionCoroutine(Action<bool, AppVersionData> callback)
    {
        string url = $"{baseUrl}/app-version";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;

            yield return request.SendWebRequest();

            AppVersionResponse resp = null;
            try { resp = JsonUtility.FromJson<AppVersionResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"App version parse error: {e.Message}"); }

            if (resp == null || !resp.success)
            {
                callback?.Invoke(false, null);
                yield break;
            }
            callback?.Invoke(true, resp.data);
        }
    }

    /// <summary>GET the "Refer &amp; Earn" summary: code, bonus amount, totals.</summary>
    public void GetReferralSummary(Action<bool, string, ReferralSummaryData> callback)
    {
        StartCoroutine(GetReferralSummaryCoroutine(callback));
    }

    private IEnumerator GetReferralSummaryCoroutine(Action<bool, string, ReferralSummaryData> callback)
    {
        string url = $"{baseUrl}/referrals/summary";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            ReferralSummaryResponse resp = null;
            try { resp = JsonUtility.FromJson<ReferralSummaryResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Referral summary parse error: {e.Message}"); }

            if (resp == null || !resp.success)
            {
                callback?.Invoke(false, ParseErrorMessage(request), null);
                yield break;
            }
            callback?.Invoke(true, resp.message, resp.data);
        }
    }

    /// <summary>GET "My Referrals" — everyone who signed up with this player's code.</summary>
    public void GetReferralList(Action<bool, string, ReferralListData> callback)
    {
        StartCoroutine(GetReferralListCoroutine(callback));
    }

    private IEnumerator GetReferralListCoroutine(Action<bool, string, ReferralListData> callback)
    {
        string url = $"{baseUrl}/referrals";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            ReferralListResponse resp = null;
            try { resp = JsonUtility.FromJson<ReferralListResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Referral list parse error: {e.Message}"); }

            if (resp == null || !resp.success)
            {
                callback?.Invoke(false, ParseErrorMessage(request), null);
                yield break;
            }
            callback?.Invoke(true, resp.message, resp.data);
        }
    }

    /// <summary>GET the last 10 winning numbers (history strip).</summary>
    public void GetRoundHistory(Action<bool, int[]> callback)
    {
        StartCoroutine(GetRoundHistoryCoroutine(callback));
    }

    private IEnumerator GetRoundHistoryCoroutine(Action<bool, int[]> callback)
    {
        string url = $"{baseUrl}/round/history";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                callback?.Invoke(false, null);
                yield break;
            }

            RoundHistoryResponse resp = null;
            try { resp = JsonUtility.FromJson<RoundHistoryResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"History parse error: {e.Message}"); }

            if (resp == null || !resp.success || resp.data == null) { callback?.Invoke(false, null); yield break; }
            callback?.Invoke(true, resp.data.history);
        }
    }

    /// <summary>GET the authoritative wallet balance from the server.</summary>
    public void GetWalletBalance(Action<bool, long> callback)
    {
        StartCoroutine(GetWalletBalanceCoroutine(callback));
    }

    private IEnumerator GetWalletBalanceCoroutine(Action<bool, long> callback)
    {
        string url = $"{baseUrl}/wallet/balance";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                callback?.Invoke(false, 0);
                yield break;
            }

            BalanceResponse resp = null;
            try { resp = JsonUtility.FromJson<BalanceResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Balance parse error: {e.Message}"); }

            if (resp == null || !resp.success || resp.data == null) { callback?.Invoke(false, 0); yield break; }

            long balance = 0;
            if (decimal.TryParse(resp.data.balance, out decimal d)) balance = (long)d;
            callback?.Invoke(true, balance);
        }
    }

    /// <summary>POST a withdrawal request (requires verified KYC + bank details server-side).</summary>
    public void Withdraw(long amount, string method, Action<bool, string> callback)
    {
        StartCoroutine(WithdrawCoroutine(amount, method, callback));
    }

    private IEnumerator WithdrawCoroutine(long amount, string method, Action<bool, string> callback)
    {
        string url = $"{baseUrl}/withdraw";

        WWWForm form = new WWWForm();
        form.AddField("amount", amount.ToString());
        if (!string.IsNullOrEmpty(method)) form.AddField("method", method);

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            WithdrawResponse resp = null;
            try { resp = JsonUtility.FromJson<WithdrawResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Withdraw parse error: {e.Message}"); }

            if (resp == null || !resp.success)
            {
                callback?.Invoke(false, ParseErrorMessage(request));
                yield break;
            }
            callback?.Invoke(true, resp.message);
        }
    }

    /// <summary>
    /// Check WHO a mobile / email / username belongs to before sending them
    /// money. Moves nothing — call this first so the player can confirm the
    /// recipient, because a completed transfer cannot be undone.
    /// </summary>
    public void LookupRecipient(string recipient, Action<bool, string, RecipientData> callback)
    {
        StartCoroutine(LookupRecipientCoroutine(recipient, callback));
    }

    private IEnumerator LookupRecipientCoroutine(string recipient, Action<bool, string, RecipientData> callback)
    {
        string url = $"{baseUrl}/transfer/lookup?recipient={UnityWebRequest.EscapeURL(recipient)}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            RecipientResponse resp = null;
            try { resp = JsonUtility.FromJson<RecipientResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Lookup parse error: {e.Message}"); }

            if (resp == null || !resp.success)
            {
                callback?.Invoke(false, ParseErrorMessage(request), null);
                yield break;
            }
            callback?.Invoke(true, resp.message, resp.data);
        }
    }

    /// <summary>Send points to another player by mobile, email or username.</summary>
    public void TransferMoney(string recipient, long amount, Action<bool, string, TransferData> callback)
    {
        StartCoroutine(TransferCoroutine(recipient, amount, callback));
    }

    private IEnumerator TransferCoroutine(string recipient, long amount, Action<bool, string, TransferData> callback)
    {
        string url = $"{baseUrl}/transfer";

        WWWForm form = new WWWForm();
        form.AddField("recipient", recipient);
        form.AddField("amount", amount.ToString());

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.certificateHandler = new BypassCertificateHandler();
            request.timeout = (int)requestTimeout;
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            TransferResponse resp = null;
            try { resp = JsonUtility.FromJson<TransferResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"Transfer parse error: {e.Message}"); }

            if (resp == null || !resp.success)
            {
                callback?.Invoke(false, ParseErrorMessage(request), null);
                yield break;
            }
            callback?.Invoke(true, resp.message, resp.data);
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Turn a failed request into something a player can act on.
    ///
    /// The server's own message is always preferred when present — the backend
    /// already writes them for players ("Betting is closed for this round.",
    /// "Withdrawals require completed KYC verification."). This only fills the
    /// gap when the request never reached the server, or came back with nothing
    /// useful in it.
    /// </summary>
    public static string DescribeFailure(UnityWebRequest request, string serverMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(serverMessage)) return serverMessage;

        if (Application.internetReachability == NetworkReachability.NotReachable)
            return L.T("error.no_internet");

        long code = request != null ? request.responseCode : 0;

        if (code == 0) return L.T("error.unreachable");
        if (code == 401) return L.T("error.session_expired");
        if (code == 403) return L.T("error.forbidden");
        if (code == 404) return L.T("error.not_found");
        if (code == 429) return L.T("error.too_many");
        if (code >= 500) return L.T("error.server");

        return L.T("error.generic");
    }

    /// <summary>Show a failed request to the player as a red toast.</summary>
    public static void ReportFailure(UnityWebRequest request, string serverMessage = null)
    {
        Toast.Error(DescribeFailure(request, serverMessage));
    }

    public bool IsLoggedIn()
    {
        return !string.IsNullOrEmpty(authToken);
    }

    public string GetAuthToken()
    {
        return authToken;
    }

    /// <summary>
    /// Short, stable tag for the signed-in session, for PlayerPrefs keys that
    /// must not bleed from one account to the next on a shared device. Null
    /// when nobody is signed in, so callers skip their cache entirely instead
    /// of falling back to a key everyone shares.
    /// </summary>
    public string SessionFingerprint()
    {
        if (string.IsNullOrEmpty(authToken)) return null;

        // FNV-1a over the token. All we need is "different session => different
        // key"; the token itself never goes into a key or gets stored twice.
        uint hash = 2166136261u;
        foreach (char c in authToken)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash.ToString("x8");
    }

    public void SetAuthToken(string token)
    {
        authToken = token;
        PlayerPrefs.SetString(TOKEN_KEY, authToken);
        PlayerPrefs.Save();
    }

    /// <summary>Clear the local session (token) without calling the server.</summary>
    public void ClearSession()
    {
        WalletManager.ForgetCachedBalance();   // keyed off the token, so purge first
        authToken = "";
        PlayerPrefs.DeleteKey(TOKEN_KEY);
        PlayerPrefs.DeleteKey(USER_DATA_KEY);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// True only when this response body genuinely came from OUR backend.
    /// Every real response — success or failure — is written by
    /// Controller::ok()/fail() on the server, which ALWAYS includes a
    /// "success" key. A route that does not exist yet on the deployed server,
    /// a 500 crash page, a host/edge error page — none of those carry it.
    ///
    /// This matters because JsonUtility does not distinguish "this field was
    /// present in the JSON" from "this field was absent and defaulted" —
    /// {"message":"The route api/x could not be found."} parses cleanly into
    /// our response classes with no exception, so a raw framework error can
    /// otherwise slip straight through to a player's screen unchanged.
    /// </summary>
    private static bool IsAppEnvelope(string rawBody)
    {
        return !string.IsNullOrEmpty(rawBody) && rawBody.Contains("\"success\"");
    }

    /// <summary>
    /// Every coroutine's ONE source of player-facing failure text. Non-technical
    /// players must never see raw backend/framework wording ("route could not
    /// be found", a stack trace, "SQLSTATE…") — only our own app's messages,
    /// which are always written in plain language, or a generic fallback.
    /// </summary>
    private string ParseErrorMessage(UnityWebRequest request)
    {
        try
        {
            if (request.downloadHandler != null && IsAppEnvelope(request.downloadHandler.text))
            {
                ApiErrorBody body = JsonUtility.FromJson<ApiErrorBody>(request.downloadHandler.text);
                if (body != null && !string.IsNullOrEmpty(body.message))
                    return body.message;
            }
        }
        catch { /* fall through to generic messages */ }

        if (Application.internetReachability == NetworkReachability.NotReachable)
            return L.T("error.no_internet");
        if (request.responseCode == 401) return L.T("error.session_expired");
        if (request.responseCode == 404) return L.T("error.not_found");
        if (request.responseCode == 422) return L.T("error.validation");
        if (request.responseCode == 429) return L.T("error.too_many");
        if (request.responseCode >= 500) return L.T("error.server");

        return L.T("error.unreachable");
    }

    #endregion
}


#region Data Models

[Serializable]
public class ApiErrorBody
{
    public string message;   // server's specific error message (validation, etc.)
}

[Serializable]
public class LoginResponse
{
    public bool success;
    public string message;

    public LoginData data;
    //public ProfileData profile;
}

[Serializable]
public class LoginData
{
    public string token;
}

[Serializable]
public class  RegisterResponse
{
    public bool success;
    public string message;
    public RegisterData data;
}

/// <summary>
/// /check-username. "data" is always null on this route, so only success +
/// message carry information.
/// </summary>
[Serializable]
public class CheckUsernameResponse
{
    public bool success;
    public string message;
}

[Serializable]
public class RegisterData
{
    public string token;
}

[Serializable]
public class PasswordResponse
{
    public bool success;
    public string message;
    public OTPData data;
}

[System.Serializable]
public class OTPData
{
    public string token;
}

[Serializable]
public class OTPResponse
{
    public bool success;
    public string message;
    public int data;
}


[Serializable]
public class GetProfileResponse
{
    public bool success;
    public string message;
    public UserData data;
    public bool is_disabled;
    public bool is_deleted;
}

[Serializable]
public class UserData
{
    public int id;
    public string role;
    public string mobile;
    public string email;
    public string status;
    public string referral_code;   // the user's own code to share ("refer a friend")
    public Gamer gamer;
}

[Serializable]
public class Gamer
{
    public int id;
    public int user_id;
    public string name;
    public string gender;
    public string date_of_birth;
    public string address;
    public string adhar_number;
    public string pan_number;
    public string adhar_document;
    public string pan_document;
    public string profile_image;
    public string kyc_status;   // "pending" | "verified" | "rejected"
    public string total_cash;
    public string total_bonus;
}

[Serializable]
public class BankDetailsResponse
{
    public bool success;
    public string message;
    public BankDetails data;
    public bool is_disabled;
    public bool is_deleted;
}

[Serializable]
public class BankDetails
{
    public int id;
    public int user_id;
    public string name;
    public string branch;
    public string account_holder_name;
    public string account_number;
    public string ifsc_code;
    public string upi_id;
    public string document;
    public string status;
    //public Gamer gamer;
}

[Serializable]
public class EditBankDetailsResponce
{
    public bool success;
    public string message;
    public EditNamkDetailsData data;
    public bool is_disabled;
    public bool is_deleted;
}

public class EditNamkDetailsData
{
    public string upi_id;
    public string ifsc_code;
    public string account_number;
}

[Serializable]
public class AddAmountResponse
{
    public bool success;
    public string message;
    public AddAmountResponseData data;
    public bool is_disabled;
    public bool is_deleted;
}

[Serializable]
public class AddAmountResponseData
{
    public string source;
    public string amount;

    // "pending"  -> money has NOT been credited yet, an admin/gateway must confirm
    // "success"  -> wallet already credited
    public string status;

    // Authoritative wallet balance AFTER this call. Not the deposit amount.
    public string balance;
}

// ── Game / wallet models (NEW backend) ───────────────────────────────────
[Serializable]
public class RoundResponse
{
    public bool success;
    public string message;
    public RoundData data;
}

[Serializable]
public class RoundData
{
    public long server_time;         // ms epoch — client syncs ONCE and counts locally
    public int slot_id;              // DB id of the session row
    public long slot_no;             // clock-derived session number = floor(unixtime / session_seconds)
    public string status;            // betting, settled
    public string phase;             // "betting" or "result"
    public int seconds_left;         // synchronized countdown for the CURRENT phase
    public int session_seconds_left;
    public int winning_number;       // valid only when phase == "result"
    public string server_seed_hash;
    public int numbers;              // e.g. 10 (0..9)
    public int payout_multiplier;    // e.g. 9
    public int session_seconds;      // e.g. 60
    public int betting_seconds;      // e.g. 50
    public int spin_seconds;         // how long the client should animate the wheel
}

[Serializable]
public class PlaceBidResponse
{
    public bool success;
    public string message;
    public PlaceBidData data;
}

[Serializable]
public class PlaceBidData
{
    public int slot_id;
    public string total_bet;
    public string balance;
}

[Serializable]
public class RoundResultResponse
{
    public bool success;
    public string message;
    public RoundResultData data;
}

[Serializable]
public class RoundResultData
{
    public int slot_id;
    public string status;            // check == "settled" before trusting winning_number
    public int winning_number;
    public string server_seed;       // revealed after settlement (provably fair)
    public string server_seed_hash;
    public string your_payout;
}

[Serializable]
public class RoundHistoryResponse
{
    public bool success;
    public string message;
    public RoundHistoryData data;
}

[Serializable]
public class RoundHistoryData
{
    public int[] history;   // last 10 winning numbers, newest first
}

[Serializable]
public class AppVersionResponse
{
    public bool success;
    public string message;
    public AppVersionData data;
}

[Serializable]
public class AppVersionData
{
    public int version_code;      // compare against the build's own version code
    public string version_name;   // display only, e.g. "1.0.4"
    public string download_url;
    public string changelog;
    public bool is_mandatory;     // true = block play until updated
}

[Serializable]
public class ReferralSummaryResponse
{
    public bool success;
    public string message;
    public ReferralSummaryData data;
}

[Serializable]
public class ReferralSummaryData
{
    public string referral_code;
    public string referral_bonus;   // ₹ paid to YOU when your invitee first deposits
    public int total_referred;
    public string total_earned;
}

[Serializable]
public class ReferralListResponse
{
    public bool success;
    public string message;
    public ReferralListData data;
}

[Serializable]
public class ReferralListData
{
    public ReferralItem[] referrals;
}

[Serializable]
public class ReferralItem
{
    public string name;
    public string mobile_masked;   // e.g. 98••••3210
    public string joined_at;       // yyyy-MM-dd
    public string status;          // "pending" | "earned"
}

[Serializable]
public class BalanceResponse
{
    public bool success;
    public string message;
    public BalanceData data;
}

[Serializable]
public class BalanceData
{
    public string balance;
}

[Serializable]
public class WithdrawResponse
{
    public bool success;
    public string message;
    public WithdrawData data;
}

[Serializable]
public class WithdrawData
{
    public int withdrawal_id;
    public string status;
    public string balance;
}

[Serializable]
public class TransferResponse
{
    public bool success;
    public string message;
    public TransferData data;
}

[Serializable]
public class TransferData
{
    public int transfer_id;
    public string balance;   // sender's balance after the transfer
    public string amount;
    public string recipient_name;
    public string recipient_masked;
}

[Serializable]
public class RecipientResponse
{
    public bool success;
    public string message;
    public RecipientData data;
}

[Serializable]
public class RecipientData
{
    public string name;
    public string mobile_masked;   // e.g. 98••••3210
    public string username;        // referral code, e.g. TRB12345
}
#endregion