using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Makes the UI fit every screen from a 4:3 tablet to a 21:9 phone.
///
/// THE PROBLEM THIS FIXES
/// ----------------------
/// The scenes ship with CanvasScalers set to Screen Match Mode = MatchWidthOrHeight
/// against a 1920x1080 reference, and the Match value is DIFFERENT on almost
/// every canvas (0, 0.5 and 1 are all in use). That produces two separate bugs:
///
///   Match = 0 (match width) on a WIDE phone (2160x1080, 20:9, 22:9 ...):
///       scale = screenWidth / 1920 = 1.125  ->  the UI is drawn 12.5% too big
///       and the top and bottom of the design area fall off the screen.
///
///   Match = 1 (match height) on a 4:3 TABLET (1440x1080):
///       scale = screenHeight / 1080 = 1.0   ->  the design area is 1920 wide
///       but the screen is only 1440  ->  the left and right edges are cut off.
///
/// And because different canvases use different Match values, overlays drift
/// relative to the game board instead of staying locked to it.
///
/// THE FIX
/// -------
/// Screen Match Mode = Expand. The scale becomes
/// min(screenW/refW, screenH/refH), so the WHOLE 1920x1080 design area is
/// always fully visible on every device — nothing is ever cropped. Extra screen
/// space simply appears at the edges, which is what <see cref="BackgroundCoverFitter"/>
/// is for.
///
/// Applying it to every canvas also makes them consistent with each other, so
/// popups and HUD elements stop sliding around relative to the board.
///
/// USAGE
/// -----
/// Nothing to do — this installs itself on every CanvasScaler in every scene at
/// runtime. To turn it off, before the first scene loads:
///
///     ResponsiveCanvasScaler.AutoInstall = false;
///
/// To bake the settings into the scenes instead (so the Game view matches
/// without pressing Play), use Tools > Real Bingo > UI > Normalize Canvas
/// Scalers In Open Scenes.
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
[DisallowMultipleComponent]
[ExecuteAlways]                 // edit mode must look EXACTLY like play mode
[DefaultExecutionOrder(-900)]
public class ResponsiveCanvasScaler : MonoBehaviour
{
    public enum FitMode
    {
        /// <summary>Whole design area always visible. Safe default — nothing is ever cropped.</summary>
        Expand,
        /// <summary>Design area always fills the screen; the overflowing axis is cropped.</summary>
        Shrink,
        /// <summary>
        /// Match width on screens narrower than the reference, height on wider ones.
        /// Equivalent to Expand, but keeps MatchWidthOrHeight mode if you prefer it.
        /// </summary>
        DynamicMatch,
    }

    /// <summary>
    /// OFF by default, on purpose. Auto-attaching this at runtime would make the
    /// game look different in Play mode than in the Scene/Game view, which is
    /// exactly the "UI changes when I press Play" problem you never want to be
    /// debugging. Bake the settings into the scenes instead:
    ///     Tools > Real Bingo > UI > Normalize Canvas Scalers In Open Scenes
    /// Set this to true before the first scene loads only if you specifically
    /// want the runtime behaviour.
    /// </summary>
    public static bool AutoInstall = false;

    /// <summary>Fit mode used when this component installs itself automatically.</summary>
    public static FitMode DefaultFitMode = FitMode.Expand;

    [Tooltip("Expand = never crop (recommended). Shrink = always fill, crops the overflowing axis.")]
    public FitMode fitMode = FitMode.Expand;

    [Tooltip("Design resolution the UI was laid out against.")]
    public Vector2 referenceResolution = new Vector2(1920f, 1080f);

    [Tooltip("Force the reference resolution onto the scaler. Untick to keep whatever each canvas already has.")]
    public bool overrideReferenceResolution = true;

    private CanvasScaler scaler;
    private Vector2 lastScreenSize;

    private void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
        Apply();
    }

    private void OnEnable() => Apply();

    private void Update()
    {
        // Rotation, split-screen, foldables and the Editor Game-view all change
        // this at runtime.
        Vector2 size = new Vector2(Screen.width, Screen.height);
        if (size == lastScreenSize) return;

        lastScreenSize = size;
        Apply();
    }

    /// <summary>Re-applies the fit rules. Safe to call at any time.</summary>
    public void Apply()
    {
        if (scaler == null) scaler = GetComponent<CanvasScaler>();
        if (scaler == null) return;

        // World-space canvases have their own sizing rules — leave them alone.
        Canvas canvas = scaler.GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace) return;

        // Only write when something actually differs — in edit mode an
        // unconditional assignment would mark the scene dirty every frame.
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        if (overrideReferenceResolution && scaler.referenceResolution != referenceResolution)
            scaler.referenceResolution = referenceResolution;

        switch (fitMode)
        {
            case FitMode.Expand:
                if (scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.Expand)
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                break;

            case FitMode.Shrink:
                if (scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.Shrink)
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
                break;

            case FitMode.DynamicMatch:
                if (scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight)
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

                Vector2 reference = scaler.referenceResolution;
                if (reference.x <= 0f || reference.y <= 0f) return;

                float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
                float referenceAspect = reference.x / reference.y;

                // Wider than the design -> height is the tight axis, so match it.
                // Narrower -> width is the tight axis.
                float target = screenAspect >= referenceAspect ? 1f : 0f;
                if (!Mathf.Approximately(scaler.matchWidthOrHeight, target))
                    scaler.matchWidthOrHeight = target;
                break;
        }
    }

    // ── Auto-install ──────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!AutoInstall) return;

        SceneManager.sceneLoaded += (scene, mode) => InstallAll();
        InstallAll();
    }

    /// <summary>
    /// Puts a ResponsiveCanvasScaler on every CanvasScaler that does not have
    /// one. Call it yourself after instantiating a canvas at runtime.
    /// Returns how many were newly equipped.
    /// </summary>
    public static int InstallAll()
    {
        CanvasScaler[] scalers = Object.FindObjectsByType<CanvasScaler>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int added = 0;

        for (int i = 0; i < scalers.Length; i++)
        {
            CanvasScaler s = scalers[i];
            if (s == null) continue;
            if (s.GetComponent<ResponsiveCanvasScaler>() != null) continue;

            var responsive = s.gameObject.AddComponent<ResponsiveCanvasScaler>();
            responsive.fitMode = DefaultFitMode;
            added++;
        }

        return added;
    }

    /// <summary>
    /// Diagnostic: dumps every CanvasScaler in the loaded scenes with its mode
    /// and match value, plus the scale factor each one resolves to on the
    /// current screen. Use it to spot canvases that disagree with each other.
    /// </summary>
    public static string DescribeAllScalers()
    {
        CanvasScaler[] scalers = Object.FindObjectsByType<CanvasScaler>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var sb = new StringBuilder();
        sb.AppendLine($"[ResponsiveCanvasScaler] {scalers.Length} CanvasScaler(s) — screen {Screen.width}x{Screen.height} (aspect {(float)Screen.width / Mathf.Max(1, Screen.height):F3})");

        foreach (CanvasScaler s in scalers)
        {
            if (s == null) continue;

            Canvas c = s.GetComponent<Canvas>();
            float factor = c != null ? c.scaleFactor : 0f;

            sb.AppendLine(
                $"  • {GetPath(s.transform)}\n" +
                $"      mode={s.uiScaleMode}  match={s.screenMatchMode}  matchValue={s.matchWidthOrHeight:F2}  " +
                $"ref={s.referenceResolution.x}x{s.referenceResolution.y}  resolvedScale={factor:F3}");
        }

        return sb.ToString();
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
