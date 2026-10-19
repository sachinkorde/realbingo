using TMPro;
using UnityEngine;
using RealBingo.Localization;
using UnityEngine.UI;

/// <summary>
/// Checks on launch whether this build is out of date, and shows an update
/// popup if so.
///
/// Two modes, decided by the server:
///   • Force update  — the popup cannot be dismissed. The player must install
///     the new build to continue. Used when an old build would be broken or
///     unsafe to keep playing on (e.g. a money-handling bug fix).
///   • Optional      — the player is told an update exists and can dismiss it.
///
/// SETUP
///   1. Put this on a GameObject in your FIRST scene (Main).
///   2. Wire the popup objects below.
///
/// The installed versionCode is read from Android's PackageInfo at runtime, so
/// Player Settings → Android → Bundle Version Code is the ONLY place to bump on
/// a release. There is no second value to keep in sync.
/// </summary>
public class VersionChecker : MonoBehaviour
{
    [Header("This build")]
    [Tooltip("Editor-only fallback. On device the real versionCode is read from " +
             "the installed PackageInfo, so this value is ignored in a build.")]
    [SerializeField] private int editorVersionCode = 1;

    /// <summary>
    /// The installed APK's actual versionCode, read from Android's PackageManager.
    ///
    /// Previously this was a hand-maintained SerializeField that had to be kept
    /// in sync with Player Settings > Android > Bundle Version Code. Nothing
    /// enforced that, so the two silently drifted: Player Settings said 2, the
    /// field said 1, and every build reported 1 forever — locking players out
    /// with an update prompt that reinstalling could never clear.
    ///
    /// Reading PackageInfo removes the duplicate entirely. There is now exactly
    /// one place to bump: Player Settings.
    /// </summary>
    private int CurrentVersionCode
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject pm = activity.Call<AndroidJavaObject>("getPackageManager");
                string package = activity.Call<string>("getPackageName");
                using AndroidJavaObject info = pm.Call<AndroidJavaObject>("getPackageInfo", package, 0);

                return info.Get<int>("versionCode");
            }
            catch (System.Exception e)
            {
                // Never block play because we could not read our own version.
                Debug.LogWarning($"[VersionChecker] Could not read versionCode: {e.Message}");
                return int.MaxValue;
            }
#else
            return editorVersionCode;
#endif
        }
    }

    [Header("Update popup")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button updateButton;
    [SerializeField] private Button laterButton;   // hidden entirely when the update is forced

    [Header("Behaviour")]
    [Tooltip("Re-check when the app returns from the background, so a forced update takes effect without a full restart.")]
    [SerializeField] private bool recheckOnResume = true;

    private string downloadUrl;
    private bool updateIsMandatory;

    private void Awake()
    {
        if (popupRoot != null) popupRoot.SetActive(false);
        if (updateButton != null) updateButton.onClick.AddListener(OnUpdateClicked);
        if (laterButton != null) laterButton.onClick.AddListener(OnLaterClicked);
    }

    private void Start() => CheckVersion();

    private void OnApplicationPause(bool paused)
    {
        if (!paused && recheckOnResume) CheckVersion();
    }

    public void CheckVersion()
    {
        if (APIManager.Instance == null) return;

        APIManager.Instance.GetAppVersion((ok, data) =>
        {
            // A failed check must NEVER block play. If the server is
            // unreachable or the response is malformed, the player keeps
            // playing on the build they have — locking people out because of
            // a network blip would be far worse than a missed update prompt.
            if (!ok || data == null) return;

            // version_code 0 means no release has been published yet.
            if (data.version_code <= 0) return;

            int installed = CurrentVersionCode;
            Debug.Log($"[VersionChecker] installed={installed} server={data.version_code} mandatory={data.is_mandatory}");

            if (data.version_code <= installed) return;   // already up to date

            downloadUrl = data.download_url;
            updateIsMandatory = data.is_mandatory;

            ShowPopup(data);
        });
    }

    private void ShowPopup(AppVersionData data)
    {
        if (popupRoot == null)
        {
            // No popup wired — still tell the player rather than staying silent.
            Toast.Warning($"A new version ({data.version_name}) is available.");
            return;
        }

        if (titleText != null)
            titleText.text = updateIsMandatory ? "Update Required" : "Update Available";

        if (messageText != null)
        {
            string body = updateIsMandatory
                ? $"Version {data.version_name} is required to keep playing. Please update to continue."
                : $"Version {data.version_name} is now available.";

            if (!string.IsNullOrWhiteSpace(data.changelog))
                body += $"\n\n{data.changelog}";

            messageText.text = body;
        }

        // On a forced update there must be no way past this popup.
        if (laterButton != null) laterButton.gameObject.SetActive(!updateIsMandatory);

        popupRoot.SetActive(true);
    }

    private void OnUpdateClicked()
    {
        if (string.IsNullOrEmpty(downloadUrl))
        {
            Toast.Error(L.T("update.no_link"));
            return;
        }

        Application.OpenURL(downloadUrl);

        // Deliberately NOT closing the popup on a forced update: the player is
        // sent to the browser/installer, and if they come back without having
        // installed it, the block must still be in place.
        if (!updateIsMandatory) popupRoot?.SetActive(false);
    }

    private void OnLaterClicked()
    {
        // Only ever reachable when the update is optional — the button is
        // hidden entirely otherwise.
        if (updateIsMandatory) return;

        popupRoot?.SetActive(false);
    }
}
