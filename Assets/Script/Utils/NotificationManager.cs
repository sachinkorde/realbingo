using UnityEngine;
using System.Collections.Generic;
using TMPro;
using System;

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
    }

    // Call this to push a new notification to the phone's memory
    public void AddNotification(string msg)
    {
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
}