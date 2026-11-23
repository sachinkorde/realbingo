using DG.Tweening;
using System;
using System.Collections;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Profile Manager handling profile display, editing, and API sync
/// With sliding panels (DOTween) and field validations
/// </summary>
public class ProfileManager : MonoBehaviour
{
    public static ProfileManager Instance { get; private set; }
    public UIManager UIManager;

    [Header("Sliding Panels")]
    [SerializeField] private RectTransform panel1; // Edit Profile (Name, Gender, Mobile, DOB, Address, Adhar, Pan)
    [SerializeField] private RectTransform panel2; // Image Upload (Profile, Adhar Card, Pan Card)

    [Header("Panel Slide Settings")]
    [SerializeField] private float slideDuration = 0.4f;
    [SerializeField] private Ease slideEase = Ease.OutQuart;

    [Header("Profile Display")]
    [SerializeField] private TMP_Text displayName;
    [SerializeField] private TMP_Text displayMobile;
    [SerializeField] private TMP_Text displayEmail;
    [SerializeField] private TMP_Text displayBalance;
    [SerializeField] private TMP_Text displayReferralCode;   // the user's own code to share
    [SerializeField] private Image profileImage;
    [SerializeField] private Image verificationBadge;

    [Header("Panel 1 - Edit Fields")]
    [SerializeField] private TMP_InputField editName;
    [SerializeField] private TMP_InputField editMobile;
    [SerializeField] private TMP_InputField editDateOfBirth;
    [SerializeField] private TMP_InputField editAddress;
    [SerializeField] private TMP_InputField editAdharNumber;
    [SerializeField] private TMP_InputField editPanNumber;

    [Header("Gender Dropdown")]
    [SerializeField] private TMP_Dropdown genderDropdown;

    [Header("Panel 2 - Document Uploads")]
    [SerializeField] private Button uploadProfileImageBtn;
    [SerializeField] private Button uploadAdharBtn;
    [SerializeField] private Button uploadPanBtn;
    [SerializeField] private Image profileImagePreview;
    [SerializeField] private Image adharPreview;
    [SerializeField] private Image panPreview;

    [Header("Panel 1 Buttons")]
    [SerializeField] private Button nextButton;
    [SerializeField] private Button cancelButton1;

    [Header("Panel 2 Buttons")]
    [SerializeField] private Button saveButton;
    [SerializeField] private Button cancelButton2; // Back to Panel 1

    [Header("Other Buttons")]
    [SerializeField] private Button loadProfileButton;
    [SerializeField] private Button editButton;
    [SerializeField] private Button logoutButton;
    [SerializeField] private Button logoutAllButton;

    [Header("Panels")]
    [SerializeField] private GameObject viewModePanel;
    [SerializeField] private GameObject bankDetailsViewModePanel;
    [SerializeField] private GameObject addCashViewModePanel;
    [SerializeField] private GameObject editModePanel;
    [SerializeField] private GameObject editModeBankPanel;
    [SerializeField] private GameObject loadingPanel;

    // The enclosing modal frame for the WHOLE Profile screen — same role as
    // bankDetailsViewPanelParent below, and as addCashPanel's parent in
    // UIManager's OpenAddCashPanel/OpenWithdrawPanel/OpenTransferCashPanel.
    // This is what the main menu's "Profile" button opens.
    [SerializeField] private GameObject profilePanelParent;

    [Header("Notification")]
    [SerializeField] private TMP_Text notificationText;

    [Header("Verification Status")]
    [SerializeField] private TMP_Text verificationStatusText;
    [SerializeField] private Image verificationStatusIcon;
    [SerializeField] private Color verifiedColor = Color.green;
    [SerializeField] private Color unverifiedColor = Color.grey;

    [Header("Bank Details")]
    [SerializeField] private TMP_Text accountHolderNameText;
    [SerializeField] private TMP_Text accountNumberText;
    [SerializeField] private TMP_Text bankNameText;
    [SerializeField] private TMP_Text branchNameText;
    [SerializeField] private TMP_Text ifscCodeText;
    [SerializeField] private TMP_Text upi_id;

    [Space]
    [SerializeField] private Button bankDetailsButton;
    [SerializeField] private Button editBankDetailsButton;
    [SerializeField] private Button cancelBankDetailsButton;
    [SerializeField] private Button saveBankDetailsButton;

    [Space]
    [SerializeField] private TMP_InputField editAccountHolderName;
    [SerializeField] private TMP_InputField editAccountNumberInput;
    [SerializeField] private TMP_InputField editBankNameInput;
    [SerializeField] private TMP_InputField editBranchInput;
    [SerializeField] private TMP_InputField editIFSCInput;
    [SerializeField] private TMP_InputField editUPIInput;

    [Space]
    [SerializeField] private GameObject bankDetailsViewPanelParent;
    [SerializeField] private GameObject bankDetailsViewPanel;
    [SerializeField] private GameObject bankDetailsEditPanel;

    [Space]
    //[SerializeField] private Image bankDetailsLoadingIndicator;
    [SerializeField] private Image bankDetailsVerificationIcon;

    [Space]
    [SerializeField] private TMP_Text bankDetailsNotificationText;

    [Space]
    [SerializeField] private Color bankDetailsVerifiedColor = Color.green;
    [SerializeField] private Color bankDetailsUnverifiedColor = Color.yellow;

    [Header("File")]
    public TMP_Text fileNameText;
    private byte[] selectedDocumentBytes = null;
    private string selectedFilePath;

    [Header("File Buttons")]
    public Button uploadBtn;
    //public Button submitBtn;

    private GetProfileResponse currentProfile;
    private BankDetailsResponse currentBankDetails;
    private byte[] pendingProfileImage;
    private byte[] pendingAdharDoc;
    private byte[] pendingPanDoc;
    private bool isEditing = false;
    private bool isAnimating = false;
    private float panelWidth;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        SetupButtons();
        SetupInputRestrictions();
        SetupPanelPositions();
        SetViewMode();

        // Subscribe to API events
        if (APIManager.Instance != null)
        {
            APIManager.Instance.OnProfileFetched += OnProfileFetchedHandler;
            APIManager.Instance.OnProfileUpdated += OnProfileUpdatedHandler;
            APIManager.Instance.OnLogoutComplete += OnLogoutHandler;
            Debug.Log("Call in Start");
            RefreshProfile();
            LoadBankDetails();
        }

        saveBankDetailsButton.onClick.AddListener(SaveBankData);
        uploadBtn.onClick.AddListener(PickDocument);
        /*bankDetailsButton.onClick.AddListener(() =>
        {
            bankDetailsViewPanel.SetActive(true);
            bankDetailsEditPanel.SetActive(false);
        });*/
    }

    private void OnDestroy()
    {
        // C#'s ?. operator bypasses Unity's == null override, so a static left
        // pointing at a destroyed object gets called into. Drop it here.
        if (Instance == this) Instance = null;
        // Kill all DOTween animations on this object to prevent leaks
        DOTween.Kill(panel1);
        DOTween.Kill(panel2);
        if (notificationText != null) DOTween.Kill(notificationText);

        if (APIManager.Instance != null)
        {
            APIManager.Instance.OnProfileFetched -= OnProfileFetchedHandler;
            APIManager.Instance.OnProfileUpdated -= OnProfileUpdatedHandler;
            APIManager.Instance.OnLogoutComplete -= OnLogoutHandler;
        }
    }

    #region Setup

    private void SetupButtons()
    {
        // Panel 1 buttons
        nextButton?.onClick.AddListener(OnNextClicked);
        cancelButton1?.onClick.AddListener(OnCancel1Clicked);

        // Panel 2 buttons
        saveButton?.onClick.AddListener(OnSaveClicked);
        cancelButton2?.onClick.AddListener(OnBackToPanel1);

        // Other buttons
        editButton?.onClick.AddListener(OnEditClicked);
        logoutButton?.onClick.AddListener(OnLogoutClicked);
        logoutAllButton?.onClick.AddListener(OnLogoutAllClicked);

        // Image upload buttons
        uploadProfileImageBtn?.onClick.AddListener(() => PickImage(ImageType.Profile));
        uploadAdharBtn?.onClick.AddListener(() => PickImage(ImageType.Adhar));
        uploadPanBtn?.onClick.AddListener(() => PickImage(ImageType.Pan));
    }

    private void SetupInputRestrictions()
    {
        // Name: max 50 chars
        if (editName != null)
            editName.characterLimit = 50;

        // Mobile: digits only, max 10
        if (editMobile != null)
        {
            editMobile.characterLimit = 10;
            editMobile.contentType = TMP_InputField.ContentType.IntegerNumber;
        }

        // DOB: max 10 chars for DD/MM/YYYY
        if (editDateOfBirth != null)
            editDateOfBirth.characterLimit = 10;

        // Address: max 200 chars
        if (editAddress != null)
            editAddress.characterLimit = 200;

        // Aadhaar: digits only, max 12
        if (editAdharNumber != null)
        {
            editAdharNumber.characterLimit = 12;
            editAdharNumber.contentType = TMP_InputField.ContentType.IntegerNumber;
        }

        // PAN: max 10 chars, auto uppercase
        if (editPanNumber != null)
        {
            editPanNumber.characterLimit = 10;
            editPanNumber.onValueChanged.AddListener((val) =>
            {
                string upper = val.ToUpper();
                if (editPanNumber.text != upper)
                {
                    editPanNumber.text = upper;
                    editPanNumber.caretPosition = upper.Length;
                }
            });
        }
    }

    private void SetupPanelPositions()
    {
        if (panel1 == null || panel2 == null) return;

        // Calculate slide width from parent
        RectTransform parent = panel1.parent as RectTransform;
        panelWidth = parent != null ? parent.rect.width : Screen.width;
        if (panelWidth <= 0) panelWidth = Screen.width;

        // Panel 1 on screen, Panel 2 off-screen to the right
        panel1.anchoredPosition = Vector2.zero;
        panel2.anchoredPosition = new Vector2(panelWidth, 0);

        panel1.gameObject.SetActive(true);
        panel2.gameObject.SetActive(true);
    }

    #endregion

    #region Public Methods

    public void LoadProfile()
    {
        Debug.Log("In LoadProfile");
        ShowLoading();

        if (APIManager.Instance == null)
        {
            Debug.LogError("APIManager.Instance is NULL!");
            HideLoading();
            // "APIManager not found" meant nothing to a player.
            ShowNotification("Unable to connect. Please restart the app and try again.");
            return;
        }

        APIManager.Instance.GetProfile((success, profileData, responseCode) =>
        {
            if (this == null) return;   // scene unloaded while the request was in flight
            Debug.Log($"Callback received - Success: {success}");
            HideLoading();

            if (success && profileData != null)
            {
                currentProfile = profileData;
                UpdateUI();
            }
            else
            {
                ShowNotification(responseCode == 401
                    ? "Your session has expired. Please log in again."
                    : "Failed to load profile. Please check your connection.");
            }
        });
    }

    public void RefreshProfile()
    {
        LoadProfile();
    }

    #endregion

    public void PickDocument()
    {
#if UNITY_EDITOR
        // Editor: use Unity's file dialog
        selectedFilePath = UnityEditor.EditorUtility.OpenFilePanel("Select Document", "", "pdf,jpg,png");
        if (!string.IsNullOrEmpty(selectedFilePath))
        {
            selectedDocumentBytes = System.IO.File.ReadAllBytes(selectedFilePath);
            Debug.Log($"Document selected: {selectedFilePath}, Size: {selectedDocumentBytes.Length}");
            ShowNotification("Document selected!");
        }

#elif UNITY_ANDROID && !UNITY_EDITOR
    // Android: use a native file-picker plugin if available (recommended).
    // If you have a plugin that provides NativeFilePicker, define NATIVEFILEPICKER_AVAILABLE
    // in Player Settings -> Other Settings -> Scripting Define Symbols.
#if NATIVEFILEPICKER_AVAILABLE
        string[] allowedTypes = new string[]
        {
            "application/pdf",
            "image/jpeg",
            "image/png"
        };

        NativeFilePicker.PickFile((path) =>
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("File pick cancelled");
                return;
            }

            selectedFilePath = path;
            selectedDocumentBytes = System.IO.File.ReadAllBytes(path);
            Debug.Log($"Document selected: {path}, Size: {selectedDocumentBytes.Length}");
            ShowNotification("Document selected!");
        }, allowedTypes);
#else
        // Fallback: NativeGallery supports image picking on Android.
        // This will only allow image selection. To support PDFs you must install a file-picker plugin.
        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("Image pick cancelled");
                return;
            }

            selectedFilePath = path;
            selectedDocumentBytes = System.IO.File.ReadAllBytes(path);
            Debug.Log($"Image selected: {path}, Size: {selectedDocumentBytes.Length}");
            ShowNotification("Image selected! (PDF selection requires a file-picker plugin)");
        }, "Select Image", "image/*");

        ShowNotification("For PDF selection on Android install a file-picker plugin and define NATIVEFILEPICKER_AVAILABLE.");
#endif

#else
    // Other platforms: show friendly message
    ShowNotification("File picker not supported on this platform. Use Editor or install a mobile file-picker plugin.");
#endif
    }

    #region Open / Close Profile Screen

    /// <summary>
    /// Opens the whole Profile screen — wire this to the "Profile" button on
    /// the main menu. Same pattern as UIManager's OpenAddCashPanel /
    /// OpenWithdrawPanel / OpenTransferCashPanel and this class's own
    /// OpenBankPanel: close whatever else is open, activate the enclosing
    /// parent, then the panel itself.
    ///
    /// Always lands in VIEW mode (never opens straight into editing) and
    /// re-fetches from the server every time, so a stale KYC status or
    /// balance from a previous visit is never shown.
    /// </summary>
    public void OpenProfilePanel()
    {
        if (profilePanelParent == null) return;

        UIManager?.CloseAllPanels();

        profilePanelParent.SetActive(true);
        SetViewMode();
        LoadProfile();
    }

    /// <summary>
    /// Closes the whole Profile screen. If the player was mid-edit, the
    /// change is discarded — not silently kept and resumed next time the
    /// screen opens, which would look like an unrelated bug days later.
    /// </summary>
    public void CloseProfilePanel()
    {
        if (profilePanelParent == null) return;

        pendingProfileImage = null;
        pendingAdharDoc = null;
        pendingPanDoc = null;

        SetViewMode();
        profilePanelParent.SetActive(false);
    }

    #endregion

    #region Load Bank Details

    public void OpenBankPanel()
    {
        bankDetailsViewPanelParent.SetActive(true);
        bankDetailsViewPanel.SetActive(true);
        LoadBankDetails();
    }

    public void ClosebankPanel()
    {
        bankDetailsViewPanelParent.SetActive(false);
        bankDetailsViewPanel.SetActive(false);
        editModeBankPanel.SetActive(false);
        UIManager.addCashPanel.SetActive(false);
    }

    public void LoadBankDetails()
    {
        Debug.Log("In Load Bank details");
        ShowLoading();

        if (APIManager.Instance == null)
        {
            Debug.LogError("APIManager.Instance is NULL!");
            HideLoading();
            // "APIManager not found" meant nothing to a player.
            ShowNotification("Unable to connect. Please restart the app and try again.");
            return;
        }

        APIManager.Instance.GetBankDetails( (success, BankData) =>
        {
            if (this == null) return;   // scene unloaded while the request was in flight
            Debug.Log($"Callback received - Success: {success}");
            HideLoading();

            if (success && BankData != null)
            {
                currentBankDetails = BankData;
                UpdateBankUI();
            }
            else
            {
                ShowNotification("Failed to load profile");
            }
        });
    }

    public void UpdateBankDetails()
    {
        Debug.Log("In Update Bank details");
        
        if (APIManager.Instance == null)
        {
            ShowNotification("Something went wrong. Try again.");
            Debug.LogError("APIManager.Instance is NULL!");
            return;
        }

        string bankName = editBankNameInput?.text?.Trim() ?? "";
        string accountHolderName = editAccountHolderName?.text?.Trim() ?? "";
        string accountNumber = editAccountNumberInput?.text?.Trim() ?? "";
        string branchName = editBranchInput?.text?.Trim() ?? "";
        string ifscCode = editIFSCInput?.text?.Trim() ?? "";
        string upiId = editUPIInput?.text?.Trim() ?? "";
        //byte[] document = editPanNumber?.text?.Trim().ToUpper() ?? "";

        ShowLoading();

        APIManager.Instance.EditBankDetails(bankName, branchName, accountHolderName, accountNumber,
                                ifscCode, upiId, selectedDocumentBytes,
                                (success, message) =>
                                {
                                    HideLoading();

                                    if (success)
                                    {
                                        ShowNotification("Profile updated successfully!");
                                        //SetViewMode();
                                        //LoadBankDetails(); // Refresh data from server
                                        UpdateBankUI();
                                    }
                                    else
                                    {
                                        //ShowNotification(message);

                                        Debug.Log(message);
                                    }
                                });
    }

    public void SaveBankData()
    {
        UpdateBankDetails();
        //LoadBankDetails();
        ClosebankPanel();
    }
    #endregion

    #region Validation

    private bool ValidatePanel1()
    {
        // --- Name ---
        string name = editName?.text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            ShowNotification("Please enter your name");
            ShakeInput(editName);
            return false;
        }
        if (name.Length < 2)
        {
            ShowNotification("Name must be at least 2 characters");
            ShakeInput(editName);
            return false;
        }

        // --- Gender ---
        if (genderDropdown != null && genderDropdown.value == 0)
        {
            ShowNotification("Please select your gender");
            return false;
        }

        // --- Mobile ---
        string mobile = editMobile?.text?.Trim() ?? "";
        if (string.IsNullOrEmpty(mobile))
        {
            ShowNotification("Please enter your mobile number");
            ShakeInput(editMobile);
            return false;
        }
        if (!Regex.IsMatch(mobile, @"^[6-9]\d{9}$"))
        {
            ShowNotification("Enter a valid 10-digit mobile number");
            ShakeInput(editMobile);
            return false;
        }

        // --- Date of Birth ---
        string dob = editDateOfBirth?.text?.Trim() ?? "";
        if (string.IsNullOrEmpty(dob))
        {
            ShowNotification("Please enter your date of birth");
            ShakeInput(editDateOfBirth);
            return false;
        }
        /*if (!IsValidDOB(dob))
        {
            // Notification already shown inside IsValidDOB if age < 18
            return false;
        }*/

        // --- Address ---
        string address = editAddress?.text?.Trim() ?? "";
        if (string.IsNullOrEmpty(address))
        {
            ShowNotification("Please enter your address");
            ShakeInput(editAddress);
            return false;
        }
        if (address.Length < 10)
        {
            ShowNotification("Address must be at least 10 characters");
            ShakeInput(editAddress);
            return false;
        }

        // --- Aadhaar Number ---
        string adhar = editAdharNumber?.text?.Trim() ?? "";
        if (string.IsNullOrEmpty(adhar))
        {
            ShowNotification("Please enter your Aadhaar number");
            ShakeInput(editAdharNumber);
            return false;
        }
        if (adhar.Length != 12)
        {
            ShowNotification("Aadhaar number must be 12 digits");
            ShakeInput(editAdharNumber);
            return false;
        }

        // --- PAN Number ---
        string pan = editPanNumber?.text?.Trim().ToUpper() ?? "";
        if (string.IsNullOrEmpty(pan))
        {
            ShowNotification("Please enter your PAN number");
            ShakeInput(editPanNumber);
            return false;
        }
        if (!Regex.IsMatch(pan, @"^[A-Z]{5}\d{4}[A-Z]$"))
        {
            ShowNotification("Enter a valid PAN (e.g., ABCDE1234F)");
            ShakeInput(editPanNumber);
            return false;
        }

        return true;
    }

    /*private bool IsValidDOB(string dob)
    {
        Accept DD/MM/YYYY or DD-MM-YYYY
        Match match = Regex.Match(dob, @"^(\d{2})[/-](\d{2})[/-](\d{4})$");
        if (!match.Success)
        {
            ShowNotification("Enter valid DOB (DD/MM/YYYY)");
            return false;
        }

        int day = int.Parse(match.Groups[1].Value);
        int month = int.Parse(match.Groups[2].Value);
        int year = int.Parse(match.Groups[3].Value);

        if (year < 1900 || year > DateTime.Now.Year)
        {
            ShowNotification("Enter a valid year");
            return false;
        }
        if (month < 1 || month > 12)
        {
            ShowNotification("Enter a valid month");
            return false;
        }
        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            ShowNotification("Enter a valid day");
            return false;
        }

        // Must be at least 18 years old
        DateTime birthDate = new DateTime(year, month, day);
        int age = DateTime.Now.Year - birthDate.Year;
        if (DateTime.Now < birthDate.AddYears(age)) age--;

        if (age < 18)
        {
            ShowNotification("You must be at least 18 years old");
            return false;
        }

        return true;
    }*/

    #endregion

    #region Panel Sliding (DOTween)

    private void OnNextClicked()
    {
        if (isAnimating) return;
        PlayButtonSound();
        HideNotification();

        if (!ValidatePanel1()) return;

        SlideToPanel2();
    }

    private void OnBackToPanel1()
    {
        if (isAnimating) return;
        PlayButtonSound();
        HideNotification();
        SlideToPanel1();
    }

    private void SlideToPanel2()
    {
        if (panel1 == null || panel2 == null) return;

        isAnimating = true;

        // Ensure Panel2 starts from the right
        panel2.anchoredPosition = new Vector2(panelWidth, 0);

        Sequence seq = DOTween.Sequence();
        // Slide Panel1 out to the left
        seq.Join(panel1.DOAnchorPosX(-panelWidth, slideDuration).SetEase(slideEase));
        // Slide Panel2 in from the right
        seq.Join(panel2.DOAnchorPosX(0, slideDuration).SetEase(slideEase));
        seq.OnComplete(() => isAnimating = false);
    }

    private void SlideToPanel1()
    {
        if (panel1 == null || panel2 == null) return;

        isAnimating = true;

        // Ensure Panel1 is to the left
        panel1.anchoredPosition = new Vector2(-panelWidth, 0);

        Sequence seq = DOTween.Sequence();
        // Slide Panel2 out to the right
        seq.Join(panel2.DOAnchorPosX(panelWidth, slideDuration).SetEase(slideEase));
        // Slide Panel1 in from the left
        seq.Join(panel1.DOAnchorPosX(0, slideDuration).SetEase(slideEase));
        seq.OnComplete(() => isAnimating = false);
    }

    /// <summary>
    /// Resets both panels to initial position (Panel1 visible, Panel2 off-screen)
    /// </summary>
    /*private void ResetPanelPositions()
    {
        if (panel1 != null) panel1.anchoredPosition = Vector2.zero;
        if (panel2 != null) panel2.anchoredPosition = new Vector2(panelWidth, 0);
        isAnimating = false;
    }*/

    #endregion

    #region UI Updates

    private void UpdateUI()
    {
        Debug.Log("Updating UI with profile data");

        if (currentProfile == null || currentProfile.data == null) return;

        UserData data = currentProfile.data;

        // `gamer` is NULL until a profile row exists for this account. Every
        // real registration creates one automatically, but an account made
        // directly from the admin panel does not — so this is not a rare
        // edge case, it is the normal state of a freshly admin-created test
        // player. Every field below goes through `gamer?.` so a not-yet-
        // filled-in profile shows a placeholder instead of throwing a
        // NullReferenceException that used to abort this whole method before
        // a single field got set — which is why NOTHING displayed, not just
        // the missing ones.
        Gamer gamer = data.gamer;

        Debug.Log($"Profile data - Name: {gamer?.name}, Mobile: {data.mobile}, Email: {data.email}, Balance: {gamer?.total_cash}");

        // Display mode
        if (displayName != null)
            displayName.text = !string.IsNullOrEmpty(gamer?.name) ? gamer.name : "Not set";

        if (displayMobile != null)
            displayMobile.text = !string.IsNullOrEmpty(data.mobile) ? data.mobile : "Not set";

        if (displayEmail != null)
            displayEmail.text = !string.IsNullOrEmpty(data.email) ? data.email : "Not set";

        if (displayBalance != null)
        {
            // total_cash arrives as a string (e.g. "15000.00"); the :N0 format is
            // ignored on a string, so parse to a number first for thousands
            // grouping. TryParse(null, ...) returns false safely when gamer
            // itself is null, so this never throws.
            displayBalance.text = decimal.TryParse(gamer?.total_cash, out decimal cashValue)
                ? $"{cashValue:N0}"
                : "0";
        }

        // "Refer a friend" — the user's own code to share. Always present:
        // generated at registration, lives on the user record, not gamer.
        if (displayReferralCode != null)
            displayReferralCode.text = data.referral_code ?? "";

        // Verification status — reads KYC status, never account status.
        // No gamer row at all means KYC has not even started, i.e. not verified.
        bool isKycVerified = gamer != null && gamer.kyc_status == "verified";
        Debug.Log($"Profile KYC status: {(gamer != null ? gamer.kyc_status : "none")}");
        UpdateVerificationStatus(isKycVerified);

        // Load profile image if available
        if (!string.IsNullOrEmpty(gamer?.profile_image) && profileImage != null)
        {
            StartCoroutine(LoadImageFromURL(gamer.profile_image, profileImage));
        }

        // Edit mode fields — pre-fill blank (not "Not set"): these are INPUT
        // fields, and a literal placeholder word would get submitted as the
        // real value if the player tapped Save without touching the field.
        if (editName != null) editName.text = gamer?.name ?? "";
        if (editMobile != null) editMobile.text = data.mobile ?? "";
        if (editAddress != null) editAddress.text = gamer?.address ?? "";
        if (editAdharNumber != null) editAdharNumber.text = gamer?.adhar_number ?? "";
        if (editPanNumber != null) editPanNumber.text = gamer?.pan_number ?? "";
        if (editDateOfBirth != null) editDateOfBirth.text = gamer?.date_of_birth ?? "";

        // Gender dropdown
        if (genderDropdown != null)
        {
            int genderIndex = 0;
            if (gamer?.gender == "male") genderIndex = 1;
            else if (gamer?.gender == "female") genderIndex = 2;
            else if (gamer?.gender == "other") genderIndex = 3;
            genderDropdown.value = genderIndex;
        }
    }

    private void UpdateVerificationStatus(bool isVerified)
    {
        if (verificationStatusText != null)
        {
            verificationStatusText.text = isVerified ? "Verified" : "Not Verified";
        }

        if (verificationStatusIcon != null)
        {
            verificationBadge.gameObject.SetActive(true);
            verificationStatusIcon.color = isVerified ? verifiedColor : unverifiedColor;
        }

        /*if (verificationBadge != null)
        {
            verificationBadge.gameObject.SetActive(isVerified);
        }*/
    }

    private void SetViewMode()
    {
        isEditing = false;
        if (viewModePanel != null) viewModePanel.SetActive(true);
        if (editModePanel != null) editModePanel.SetActive(false);
        //if (editButton != null) editButton.gameObject.SetActive(true);

        //ResetPanelPositions();
    }

    private void SetEditMode()
    {
        isEditing = true;
        if (viewModePanel != null) viewModePanel.SetActive(false);
        if (editModePanel != null)
        {
            editModePanel.SetActive(true);
            editModePanel.transform.localScale = Vector3.zero;
            editModePanel.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
        }
        if (editButton != null) editButton.gameObject.SetActive(false);

        //ResetPanelPositions();

        // Clear pending uploads
        pendingProfileImage = null;
        pendingAdharDoc = null;
        pendingPanDoc = null;
    }

    #region
    private void UpdateBankUI()
    {
        Debug.Log("Updating UI with profile data");

        if (currentBankDetails == null) return;

        // Display mode
        accountHolderNameText.text = currentBankDetails.data.account_holder_name ?? "N/A";
        accountNumberText.text = currentBankDetails.data.account_number ?? "N/A";
        bankNameText.text = currentBankDetails.data.name ?? "N/A";
        branchNameText.text = currentBankDetails.data.branch ?? "N/A";
        ifscCodeText.text = currentBankDetails.data.ifsc_code ?? "N/A";
        upi_id.text = currentBankDetails.data.upi_id ?? "N/A";

        Debug.Log($"Profile verification status: {(currentBankDetails.is_disabled ? "Disabled" : "Active")}");

        // Verification status
        /*UpdateVerificationStatus(currentProfile.is_disabled);

        // Load profile image if available
        if (!string.IsNullOrEmpty(currentProfile.data.gamer.profile_image) && profileImage != null)
        {
            StartCoroutine(LoadImageFromURL(currentProfile.data.gamer.profile_image, profileImage));
        }*/

        // Edit mode fields - pre-fill
        editAccountHolderName.text = currentBankDetails.data.account_holder_name ?? "";
        editAccountNumberInput.text = currentBankDetails.data.account_number ?? "";
        editBankNameInput.text = currentBankDetails.data.name ?? "";
        editBranchInput.text = currentBankDetails.data.branch ?? "";
        editIFSCInput.text = currentBankDetails.data.ifsc_code ?? "";
        editUPIInput.text = currentBankDetails.data.upi_id ?? "";
    }

    private void SetBankViewMode()
    {
        isEditing = false;
        if (bankDetailsViewPanelParent != null) bankDetailsViewPanelParent.SetActive(true);
        if (bankDetailsViewPanel != null) bankDetailsViewPanel.SetActive(true);
        if (bankDetailsEditPanel != null) bankDetailsEditPanel.SetActive(false);
        if (addCashViewModePanel != null) addCashViewModePanel.SetActive(false);

        //ResetPanelPositions();
    }

    public void OpenEditbankDetails()
    {
        if (editModeBankPanel != null) editModeBankPanel.SetActive(true);
    }

    #endregion
    #endregion

    #region Button Handlers

    private void OnEditClicked()
    {
        PlayButtonSound();
        SetEditMode();
    }

    private void OnSaveClicked()
    {
        if (isAnimating) return;
        PlayButtonSound();
        HideNotification();

        // Profile photo (and the Aadhaar/PAN document uploads) are
        // DELIBERATELY not validated here — the client has not confirmed
        // whether a profile photo should be required, so Save must succeed
        // with pendingProfileImage left null. Do not add a check requiring
        // it without that confirmation.
        SaveProfile();
    }

    private void OnCancel1Clicked()
    {
        if (isAnimating) return;
        PlayButtonSound();
        HideNotification();
        SetViewMode();
        UpdateUI(); // Reset to saved values
    }

    private void OnLogoutClicked()
    {
        PlayButtonSound();
        ShowConfirmDialog("Are you sure you want to logout?", () =>
        {
            ShowLoading();
            APIManager.Instance?.Logout((success, message) =>
            {
                if (this == null) return;   // scene unloaded while the request was in flight
                HideLoading();
                if (success)
                {
                    PlayerPrefs.SetInt(UIManagerMenu.FIRST_LOGIN_KEY, 1);
                    PlayerPrefs.Save();

                    // Stop the game BGM now rather than when the menu finishes
                    // loading, so logout is silent from the tap onwards.
                    if (AudioManager.Instance != null)
                        AudioManager.Instance.ResetPlayback();

                    UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
                }
                else
                {
                    ShowNotification(message);
                }
            });
        });
    }

    private void OnLogoutAllClicked()
    {
        PlayButtonSound();
        ShowConfirmDialog("Logout from all devices?", () =>
        {
            ShowLoading();
            APIManager.Instance?.LogoutAll((success, message) =>
            {
                if (this == null) return;   // scene unloaded while the request was in flight
                HideLoading();
                if (success)
                {
                    PlayerPrefs.SetInt(UIManagerMenu.FIRST_LOGIN_KEY, 1);
                    PlayerPrefs.Save();

                    // Stop the game BGM now rather than when the menu finishes
                    // loading, so logout is silent from the tap onwards.
                    if (AudioManager.Instance != null)
                        AudioManager.Instance.ResetPlayback();

                    UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
                }
                else
                {
                    ShowNotification(message);
                }
            });
        });
    }

    #endregion

    #region Save Profile

    private void SaveProfile()
    {
        if (APIManager.Instance == null)
        {
            ShowNotification("Something went wrong. Try again.");
            Debug.LogError("APIManager.Instance is NULL!");
            return;
        }

        string name = editName?.text?.Trim() ?? "";
        string gender = GetSelectedGender();
        string dob = editDateOfBirth?.text?.Trim() ?? "";
        string address = editAddress?.text?.Trim() ?? "";
        string adhar = editAdharNumber?.text?.Trim() ?? "";
        string pan = editPanNumber?.text?.Trim().ToUpper() ?? "";

        ShowLoading();

        APIManager.Instance.UpdateProfile(
            name, gender, dob, address, adhar, pan,
            pendingProfileImage, pendingAdharDoc, pendingPanDoc,
            (success, message) =>
            {
                if (this == null) return;   // scene unloaded while the request was in flight
                HideLoading();

                if (success)
                {
                    ShowNotification("Profile updated successfully!");
                    SetViewMode();
                    LoadProfile(); // Refresh data from server
                }
                else
                {
                    ShowNotification(message);

                    Debug.Log(message);
                }
            }
        );
    }

    private string GetSelectedGender()
    {
        if (genderDropdown == null) return "";

        switch (genderDropdown.value)
        {
            case 1: return "male";
            case 2: return "female";
            case 3: return "other";
            default: return "";
        }
    }

    #endregion

    #region Image Handling

    private enum ImageType { Profile, Adhar, Pan }

    [Header("Image Settings")]
    [SerializeField] private int maxImageWidth = 1024;
    [SerializeField] private int maxImageHeight = 1024;
    [SerializeField] private int jpegQuality = 75;

    /// <summary>
    /// Cross-platform image picker — works on Android (NativeGallery) and Windows/Editor (file dialog)
    /// </summary>
    private void PickImage(ImageType type)
    {
        Debug.Log($"Pick image for: {type}");

#if UNITY_ANDROID && !UNITY_EDITOR
        PickImageAndroid(type);
#else
        PickImageWindows(type);
#endif
    }

    // ─────────────────────────────────────────────
    //  ANDROID — Uses NativeGallery plugin
    //  Install: https://github.com/yasirkula/UnityNativeGallery
    // ─────────────────────────────────────────────
    #if UNITY_ANDROID && !UNITY_EDITOR
    private void PickImageAndroid(ImageType type)
    {
        // CheckPermission returns bool in this NativeGallery version
        bool hasPermission = NativeGallery.CheckPermission(
            NativeGallery.PermissionType.Read, NativeGallery.MediaType.Image);

        if (hasPermission)
        {
            NativeGallery.GetImageFromGallery((path) =>
            {
                Debug.Log($"Android image picked: {path}");
                if (!string.IsNullOrEmpty(path))
                    ProcessPickedImage(path, type);
                else
                    ShowNotification("Image selection cancelled");
            }, "Select Image", "image/*");
        }
        else
        {
            // RequestPermission (synchronous) does not exist; use the async/callback API
            // RequestPermissionAsync with a callback will provide a NativeGallery.Permission result
            NativeGallery.RequestPermissionAsync((perm) =>
            {
                if (perm == NativeGallery.Permission.Granted)
                {
                    NativeGallery.GetImageFromGallery((path) =>
                    {
                        Debug.Log($"Android image picked: {path}");
                        if (!string.IsNullOrEmpty(path))
                            ProcessPickedImage(path, type);
                        else
                            ShowNotification("Image selection cancelled");
                    }, "Select Image", "image/*");
                }
                else
                {
                    ShowNotification("Gallery permission is required to upload images");
                }
            }, NativeGallery.PermissionType.Read, NativeGallery.MediaType.Image);
        }
    }
    #endif

    // ─────────────────────────────────────────────
    //  WINDOWS / EDITOR — Uses SimpleFileBrowser or standalone file dialog
    //  Install: https://github.com/yasirkula/UnitySimpleFileBrowser
    //  Or use the built-in EditorUtility in Editor mode
    // ─────────────────────────────────────────────
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
    private void PickImageWindows(ImageType type)
    {
#if UNITY_EDITOR
        // In Editor, use Unity's built-in file dialog
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "Select Image",
            "",
            "png,jpg,jpeg"
        );

        if (!string.IsNullOrEmpty(path))
        {
            ProcessPickedImage(path, type);
        }
#else
        // In Windows build, use SimpleFileBrowser
        // Install: https://github.com/yasirkula/UnitySimpleFileBrowser
        SimpleFileBrowser.FileBrowser.SetFilters(true, 
            new SimpleFileBrowser.FileBrowser.Filter("Images", ".png", ".jpg", ".jpeg"));
        SimpleFileBrowser.FileBrowser.SetDefaultFilter(".jpg");

        StartCoroutine(ShowFileBrowserCoroutine(type));
#endif
    }

#if !UNITY_EDITOR
    private IEnumerator ShowFileBrowserCoroutine(ImageType type)
    {
        yield return SimpleFileBrowser.FileBrowser.WaitForLoadDialog(
            SimpleFileBrowser.FileBrowser.PickMode.Files,
            false,
            null,
            null,
            "Select Image",
            "Select"
        );

        if (SimpleFileBrowser.FileBrowser.Success)
        {
            string path = SimpleFileBrowser.FileBrowser.Result[0];
            ProcessPickedImage(path, type);
        }
        else
        {
            ShowNotification("Image selection cancelled");
        }
    }
#endif
#endif

    // ─────────────────────────────────────────────
    //  COMMON — Process, resize, and assign picked image
    // ─────────────────────────────────────────────
    private void ProcessPickedImage(string path, ImageType type)
    {
        try
        {
            if (!System.IO.File.Exists(path))
            {
                ShowNotification("File not found");
                return;
            }

            // Check file size (limit 10MB raw file)
            long fileSize = new System.IO.FileInfo(path).Length;
            if (fileSize > 10 * 1024 * 1024)
            {
                ShowNotification("Image too large (max 10MB)");
                return;
            }

            byte[] rawData = System.IO.File.ReadAllBytes(path);

            // Decode into texture
            Texture2D originalTex = new Texture2D(2, 2);
            if (!originalTex.LoadImage(rawData))
            {
                ShowNotification("Invalid image file");
                Destroy(originalTex);
                return;
            }

            // Resize if too large
            Texture2D finalTex = ResizeTextureIfNeeded(originalTex, maxImageWidth, maxImageHeight);

            // Encode to JPEG for upload (smaller size)
            byte[] imageData = finalTex.EncodeToJPG(jpegQuality);

            Debug.Log($"Image processed — Original: {originalTex.width}x{originalTex.height}, " +
                      $"Final: {finalTex.width}x{finalTex.height}, Size: {imageData.Length / 1024}KB");

            // Clean up textures
            if (finalTex != originalTex) Destroy(originalTex);

            // Assign to correct slot and update preview
            Image previewTarget = null;

            switch (type)
            {
                case ImageType.Profile:
                    pendingProfileImage = imageData;
                    previewTarget = profileImagePreview;
                    break;

                case ImageType.Adhar:
                    pendingAdharDoc = imageData;
                    previewTarget = adharPreview;
                    break;

                case ImageType.Pan:
                    pendingPanDoc = imageData;
                    previewTarget = panPreview;
                    break;
            }

            // Show preview
            if (previewTarget != null)
            {
                Sprite sprite = Sprite.Create(finalTex,
                    new Rect(0, 0, finalTex.width, finalTex.height),
                    Vector2.one * 0.5f);
                previewTarget.sprite = sprite;
                previewTarget.color = Color.white; // In case it was transparent/hidden

                // Nice pop animation on the preview
                previewTarget.transform.localScale = Vector3.zero;
                previewTarget.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
            }

            ShowNotification($"{type} image selected!");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to process image: {e.Message}\n{e.StackTrace}");
            ShowNotification("Failed to load image");
        }
    }

    /// <summary>
    /// Resizes texture if it exceeds max dimensions while maintaining aspect ratio
    /// </summary>
    private Texture2D ResizeTextureIfNeeded(Texture2D source, int maxW, int maxH)
    {
        if (source.width <= maxW && source.height <= maxH)
            return source;

        float ratioW = (float)maxW / source.width;
        float ratioH = (float)maxH / source.height;
        float ratio = Mathf.Min(ratioW, ratioH);

        int newW = Mathf.RoundToInt(source.width * ratio);
        int newH = Mathf.RoundToInt(source.height * ratio);

        RenderTexture rt = RenderTexture.GetTemporary(newW, newH, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Graphics.Blit(source, rt);

        Texture2D resized = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
        resized.ReadPixels(new Rect(0, 0, newW, newH), 0, 0);
        resized.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return resized;
    }

    private void LoadLocalImage(byte[] data, Image targetImage)
    {
        if (data == null || targetImage == null) return;

        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(data);
        targetImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
    }

    private IEnumerator LoadImageFromURL(string url, Image targetImage)
    {
        using (UnityEngine.Networking.UnityWebRequest request =
               UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Texture2D tex = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
                targetImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            }
        }
    }

    #endregion

    #region Event Handlers

    private void OnProfileFetchedHandler(bool success, string message) { }
    private void OnProfileUpdatedHandler(bool success, string message) { }
    private void OnLogoutHandler(bool success, string message) { }

    #endregion

    #region Helpers

    private void ShowLoading()
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
    }

    private void HideLoading()
    {
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    private void ShowNotification(string message)
    {
        // Try the popup system first
        if (UIManagerMenu.Instance != null)
        {
            UIManagerMenu.Instance.ShowPopup(message);
            return;
        }

        // Fallback: animate the notification text. If that label is not wired
        // either, Toast guarantees the message still reaches the player instead
        // of disappearing (KYC and bank-detail errors matter too much to drop).
        if (notificationText == null)
        {
            Toast.Show(message);
            return;
        }

        notificationText.gameObject.SetActive(true);
        notificationText.text = message;
        notificationText.color = new Color(notificationText.color.r, notificationText.color.g, notificationText.color.b, 0);

        DOTween.Kill(notificationText);
        Sequence seq = DOTween.Sequence();
        seq.Append(notificationText.DOFade(1f, 0.2f));
        seq.AppendInterval(2.5f);
        seq.Append(notificationText.DOFade(0f, 0.3f));
        seq.OnComplete(() => notificationText.gameObject.SetActive(false));
        seq.SetTarget(notificationText);
    }

    private void HideNotification()
    {
        if (notificationText != null)
        {
            DOTween.Kill(notificationText);
            notificationText.gameObject.SetActive(false);
        }
    }

    private void ShowConfirmDialog(string message, Action onConfirm)
    {
        // TODO: Replace with your actual confirm dialog UI
        onConfirm?.Invoke();
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

    #endregion
}
