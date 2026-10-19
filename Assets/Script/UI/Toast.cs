using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum ToastType { Info, Success, Error, Warning }

/// <summary>
/// Scene-independent on-screen message.
///
/// UIManagerMenu only exists in the menu scene, so anything raised from the game
/// scene had nowhere to appear and the player just saw a control that seemed to
/// do nothing. This builds its own canvas on first use — no prefab, no Inspector
/// wiring — and survives scene loads, so it works from anywhere:
///
///     Toast.Error("No internet connection.");
///     Toast.Success("500 added to your wallet.");
///     Toast.Info("Betting is closed for this round.");
///
/// Messages queue rather than overwrite, so a burst of failures is all readable.
/// </summary>
public class Toast : MonoBehaviour
{
    private static Toast instance;

    private CanvasGroup group;
    private TextMeshProUGUI label;
    private Image background;
    private readonly Queue<(string text, ToastType type, float seconds)> queue = new();
    private bool draining;

    // Same palette as the admin dashboard so the two read as one product.
    private static readonly Color InfoColor = new(0.11f, 0.13f, 0.20f, 0.95f);
    private static readonly Color SuccessColor = new(0.09f, 0.40f, 0.24f, 0.95f);
    private static readonly Color ErrorColor = new(0.55f, 0.12f, 0.16f, 0.95f);
    private static readonly Color WarningColor = new(0.55f, 0.38f, 0.05f, 0.95f);

    public static void Show(string message, float seconds = 3.5f) => Enqueue(message, ToastType.Info, seconds);
    public static void Info(string message, float seconds = 3.5f) => Enqueue(message, ToastType.Info, seconds);
    public static void Success(string message, float seconds = 3f) => Enqueue(message, ToastType.Success, seconds);
    public static void Warning(string message, float seconds = 4f) => Enqueue(message, ToastType.Warning, seconds);
    public static void Error(string message, float seconds = 4.5f) => Enqueue(message, ToastType.Error, seconds);

    private static void Enqueue(string message, ToastType type, float seconds)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // The Console line is the safety net: if UI creation ever fails, the
        // message is still recoverable rather than lost entirely.
        Debug.Log($"[Toast:{type}] {message}");

        if (!Application.isPlaying) return;

        if (instance == null) instance = Create();
        if (instance == null) return;

        instance.queue.Enqueue((message, type, seconds));
        if (!instance.draining) instance.StartCoroutine(instance.Drain());
    }

    private static Toast Create()
    {
        var host = new GameObject("~Toast");
        DontDestroyOnLoad(host);

        var canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;                       // above every game UI

        var scaler = host.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        host.AddComponent<GraphicRaycaster>().enabled = false;   // never eat taps

        var toast = host.AddComponent<Toast>();

        // Panel
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(host.transform, false);

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0, -70f);
        rect.sizeDelta = new Vector2(1150f, 120f);

        toast.background = panel.GetComponent<Image>();
        toast.background.color = InfoColor;
        toast.background.raycastTarget = false;

        // Label
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(panel.transform, false);

        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(34f, 14f);
        textRect.offsetMax = new Vector2(-34f, -14f);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 34f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        // Word wrapping is on by default. Not set explicitly because the API for
        // it was renamed (enableWordWrapping -> textWrappingMode), and relying on
        // the default keeps this compiling on either TMP version.

        toast.label = tmp;
        toast.group = host.AddComponent<CanvasGroup>();
        toast.group.alpha = 0f;
        toast.group.blocksRaycasts = false;
        toast.group.interactable = false;

        return toast;
    }

    private IEnumerator Drain()
    {
        draining = true;

        while (queue.Count > 0)
        {
            var (text, type, seconds) = queue.Dequeue();

            if (label == null || group == null) break;

            label.text = text;
            background.color = type switch
            {
                ToastType.Success => SuccessColor,
                ToastType.Error => ErrorColor,
                ToastType.Warning => WarningColor,
                _ => InfoColor,
            };

            // Every message the player must not miss also arrives through the
            // hand — a toast at the top of the screen is easy to look past.
            switch (type)
            {
                case ToastType.Success: HapticManager.Success(); break;
                case ToastType.Error: HapticManager.Failure(); break;
                case ToastType.Warning: HapticManager.Warning(); break;
                default: HapticManager.Light(); break;
            }

            // Unscaled time throughout: messages must still appear if the game
            // pauses (Time.timeScale = 0) during an error state.
            group.alpha = 1f;
            yield return new WaitForSecondsRealtime(seconds);

            for (float t = 0.35f; t > 0f; t -= Time.unscaledDeltaTime)
            {
                group.alpha = t / 0.35f;
                yield return null;
            }

            group.alpha = 0f;
            yield return new WaitForSecondsRealtime(0.12f);
        }

        draining = false;
    }
}
