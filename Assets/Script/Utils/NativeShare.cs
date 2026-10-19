using System;
using UnityEngine;
using RealBingo.Localization;

/// <summary>
/// Opens Android's native "Share via…" sheet (WhatsApp, SMS, email, anything
/// the player has installed) for a plain-text message. Built directly on
/// Android's Intent.ACTION_SEND through Unity's Java bridge — this project
/// has no third-party share plugin installed, and a plain-text share does
/// not need one.
///
/// In the Editor (no Android runtime) this copies the text to the system
/// clipboard instead, so testing in Play Mode never throws.
/// </summary>
public static class NativeShare
{
    /// <param name="text">The message body to share.</param>
    /// <param name="chooserTitle">Title of the "Share via…" dialog.</param>
    public static void ShareText(string text, string chooserTitle = "Share via")
    {
        if (string.IsNullOrEmpty(text)) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var intentClass = new AndroidJavaClass("android.content.Intent");
            using var intentObject = new AndroidJavaObject("android.content.Intent");

            intentObject.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
            intentObject.Call<AndroidJavaObject>("setType", "text/plain");
            intentObject.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);

            using AndroidJavaObject chooser =
                intentClass.CallStatic<AndroidJavaObject>("createChooser", intentObject, chooserTitle);

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            currentActivity.Call("startActivity", chooser);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NativeShare] Failed to open the share sheet: {e.Message}");
            Toast.Warning(L.T("referral.share_failed"));
        }
#else
        Debug.Log($"[NativeShare] (Editor) Would share: {text}");
        GUIUtility.systemCopyBuffer = text;
        Toast.Info(L.T("referral.message_copied"));
#endif
    }
}
