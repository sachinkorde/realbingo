using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using System;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using Unity.Notifications;
#endif

// 1. Data model for a single notification
[Serializable]
public class NotificationData
{
    public string message;
    public string timestamp;
    public bool isRead;
}

// 2. Wrapper class so we can convert the list to JSON
[Serializable]
public class NotificationSaveData
{
    public List<NotificationData> notifications = new List<NotificationData>();
}

public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance;

    private const string ENABLED_KEY = "NotificationsEnabled";

    /// <summary>
    /// The player's notification switch in Settings. It lives here rather than in
    /// the panel so the preference outlives whichever screen last touched it, and
    /// so AddNotification can honour it without knowing any UI exists. Mirrors
    /// HapticManager.Enabled.
    ///
    /// Prefer <see cref="SetEnabled"/> from UI: it also deals with the badge, the
    /// OS permission prompt and any reminders already queued on the device.
    /// </summary>
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(ENABLED_KEY, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(ENABLED_KEY, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    [Header("Scroll View Settings")]
    [Tooltip("The 'Content' GameObject inside your Scroll View")]
    public Transform contentContainer;
    [Tooltip("A UI Prefab with a TMP_Text component attached to it")]
    public GameObject notificationPrefab;

    [Header("Badge UI Settings")]
    [Tooltip("The Red Circle Image holding the text")]
    public GameObject badgeObject;
    [Tooltip("The Text inside the red circle")]
    public TMP_Text badgeText;

    private NotificationSaveData saveData = new NotificationSaveData();
    private const string PREFS_KEY = "UserNotifications";
    private const int MAX_NOTIFICATIONS = 50; // Keep list from getting too large

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        LoadNotifications();
        InitializeDeviceNotifications();
    }

    private void Start()
    {
        // Already opted in from a previous run (or a fresh install, which
        // defaults to on): make sure the OS actually lets us post, otherwise the
        // switch says "on" while Android silently drops everything.
        if (Enabled) RequestDevicePermission();
    }

    private void OnDestroy()
    {
        // C#'s ?. operator bypasses Unity's == null override, so a static left
        // pointing at a destroyed object gets called into. Drop it here.
        if (Instance == this) Instance = null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) ScheduleComeBackReminders();
        else CancelDeviceReminders();
    }

    private void OnApplicationQuit()
    {
        ScheduleComeBackReminders();
    }

    // ── The Settings switch ──────────────────────────────────────────────────

    /// <summary>
    /// The single entry point for the Settings switch: stores the preference,
    /// brings the badge in line, asks the OS for permission when switching on,
    /// and clears anything already queued on the device when switching off.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;

        if (enabled)
        {
            RequestDevicePermission();
        }
        else
        {
            // A player who just switched these off should not get a reminder
            // tomorrow from something queued before they did.
            CancelDeviceReminders();
        }

        UpdateBadge();
    }

    // Call this to push a new notification to the phone's memory
    public void AddNotification(string msg)
    {
        // Switched off in Settings: drop it rather than badge a player who
        // asked not to be told.
        if (!Enabled) return;

        NotificationData newNotif = new NotificationData
        {
            message = msg,
            timestamp = DateTime.Now.ToString("dd MMM, hh:mm tt"),
            isRead = false
        };

        saveData.notifications.Insert(0, newNotif); // Add to top of list

        // Limit list size to save memory
        if (saveData.notifications.Count > MAX_NOTIFICATIONS)
        {
            saveData.notifications.RemoveAt(saveData.notifications.Count - 1);
        }

        SaveNotifications();
        UpdateBadge();
    }

    private void LoadNotifications()
    {
        if (PlayerPrefs.HasKey(PREFS_KEY))
        {
            string json = PlayerPrefs.GetString(PREFS_KEY);

            // Guard: a corrupt / empty pref must not leave saveData null,
            // otherwise the next AddNotification() throws.
            try
            {
                saveData = JsonUtility.FromJson<NotificationSaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Notification load error: {e.Message}");
                saveData = null;
            }

            if (saveData == null) saveData = new NotificationSaveData();
            if (saveData.notifications == null) saveData.notifications = new List<NotificationData>();
        }
        UpdateBadge();
    }

    private void SaveNotifications()
    {
        string json = JsonUtility.ToJson(saveData);
        PlayerPrefs.SetString(PREFS_KEY, json);
        PlayerPrefs.Save();
    }

    public void UpdateBadge()
    {
        // Switched off in Settings: no badge either. Leaving the red dot up after
        // the player turned notifications off is the same nag they just declined.
        if (!Enabled)
        {
            if (badgeObject != null) badgeObject.SetActive(false);
            return;
        }

        int unreadCount = 0;
        foreach (var n in saveData.notifications)
        {
            if (!n.isRead) unreadCount++;
        }

        if (unreadCount > 0)
        {
            if (badgeObject != null) badgeObject.SetActive(true);
            if (badgeText != null) badgeText.text = unreadCount >= 10 ? "10+" : unreadCount.ToString();
        }
        else
        {
            if (badgeObject != null) badgeObject.SetActive(false);
        }
    }

    // Called by UIManager when the player opens the side panel
    public void PopulateScrollView()
    {
        if (contentContainer == null || notificationPrefab == null) return;

        // Clear old UI items
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        // Spawn new items and mark them as read
        foreach (var n in saveData.notifications)
        {
            GameObject go = Instantiate(notificationPrefab, contentContainer);
            TMP_Text txt = go.GetComponentInChildren<TMP_Text>();

            if (txt != null)
            {
                // Format:
                // 19 Mar, 04:30 PM
                // Winning Number: 8 | Balance: ₹15,000
                txt.text = $"<color=#AAAAAA><size=80%>{n.timestamp}</size></color>\n{n.message}";
            }

            n.isRead = true; // Mark as read when viewed
        }

        SaveNotifications(); // Save the "Read" status
        UpdateBadge(); // Hide the badge
    }

    // ── Device (OS) notifications ────────────────────────────────────────────
    //
    // The in-app list above is what the player sees while the game is open. These
    // are the ones the phone shows when it is not. Both answer to the same
    // Settings switch, so "Notification: off" means off everywhere.
    //
    // Everything here is compiled out on desktop and in the Editor: the Mobile
    // Notifications package only has a real implementation on a device, and the
    // Editor would otherwise log channel errors on every play.

    [Header("Device Reminders")]
    [Tooltip("Reminder sent this many hours after the player leaves the game. 0 disables it.")]
    [SerializeField] private float firstReminderHours = 4f;

    [Tooltip("Second reminder, for a player who did not come back. 0 disables it.")]
    [SerializeField] private float secondReminderHours = 24f;

    [Tooltip("Shown on the first device reminder.")]
    [SerializeField] private string firstReminderTitle = "Real Bingo";
    [SerializeField] private string firstReminderText = "A new round is waiting for you.";

    [Tooltip("Shown on the second device reminder.")]
    [SerializeField] private string secondReminderTitle = "Real Bingo";
    [SerializeField] private string secondReminderText = "Your table is still open — come back and play.";

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
    private const string ChannelId = "realbingo_general";
    private static bool deviceCenterReady;
    private Coroutine permissionRoutine;
#endif

    private void InitializeDeviceNotifications()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (deviceCenterReady) return;

        var args = NotificationCenterArgs.Default;
        args.AndroidChannelId = ChannelId;
        args.AndroidChannelName = "Game updates";
        args.AndroidChannelDescription = "Round reminders and wallet updates.";

        NotificationCenter.Initialize(args);
        deviceCenterReady = true;
#endif
    }

    /// <summary>
    /// Asks the OS for permission to post. Android 13+ and every iOS version
    /// require this; without it the switch would read "on" while nothing is ever
    /// delivered. Repeat calls are cheap — the OS answers from its own record
    /// once the player has already decided.
    /// </summary>
    private void RequestDevicePermission()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        InitializeDeviceNotifications();

        if (permissionRoutine != null) return;
        permissionRoutine = StartCoroutine(RequestDevicePermissionRoutine());
#endif
    }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
    private IEnumerator RequestDevicePermissionRoutine()
    {
        var request = NotificationCenter.RequestPermission();
        yield return request;

        permissionRoutine = null;

        if (request.Status != NotificationsPermissionStatus.Granted)
        {
            Debug.Log($"[Notifications] device permission not granted ({request.Status}); " +
                      "in-app notifications still work, the phone will not show any.");
        }
    }
#endif

    /// <summary>
    /// Queues the "come back" reminders as the player leaves. Called on pause and
    /// on quit, and always clears the queue first so backgrounding the game twice
    /// does not stack two sets of the same reminder.
    /// </summary>
    private void ScheduleComeBackReminders()
    {
        CancelDeviceReminders();

        if (!Enabled) return;

        ScheduleReminder(firstReminderHours, firstReminderTitle, firstReminderText);
        ScheduleReminder(secondReminderHours, secondReminderTitle, secondReminderText);
    }

    private void ScheduleReminder(float hoursFromNow, string title, string text)
    {
        if (hoursFromNow <= 0f || string.IsNullOrEmpty(text)) return;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        var notification = new Notification
        {
            Title = title,
            Text = text,
            ShowInForeground = false,   // they are reminders for a closed app
        };

        NotificationCenter.ScheduleNotification(
            notification,
            new NotificationIntervalSchedule(TimeSpan.FromHours(hoursFromNow)));
#endif
    }

    /// <summary>Drops every reminder still queued on the device.</summary>
    private void CancelDeviceReminders()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (!deviceCenterReady) return;

        NotificationCenter.CancelAllScheduledNotifications();

        // Switched off: clear what already landed in the shade too, and the
        // springboard badge with it.
        if (!Enabled)
        {
            NotificationCenter.CancelAllDeliveredNotifications();
            NotificationCenter.ClearBadge();
        }
#endif
    }
}