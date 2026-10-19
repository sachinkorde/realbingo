using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puts the rotating neon boundary on an Image, with zero setup.
///
/// PRD Topic 5.4 / 6.1 asks the special 'J' (Jackpot) chip and slot to carry a
/// "high-contrast, fine neon boundary outline" that "continuously rotates at a
/// fixed angular velocity (360 degree loop)".
///
/// USAGE
///   1. Make an empty child Image on the J chip / J slot, sized to cover it.
///   2. Add this component. Done — it creates its own material at runtime.
///
/// The rotation happens inside the shader, driven by _Time, so there is no
/// Update() and no tween running per frame. One draw call, no CPU cost, and it
/// stays perfectly smooth even if the game hitches.
///
/// Nothing here depends on the shader existing: if it is missing (stripped from
/// a build, say) the component disables itself and the Image renders normally.
/// </summary>
[RequireComponent(typeof(Image))]
[DisallowMultipleComponent]
[ExecuteAlways]
public class NeonBorder : MonoBehaviour
{
    public const string ShaderName = "RealBingo/UI Neon Border";

    [Header("Colours")]
    public Color neonColor = new Color(1f, 0.85f, 0.1f, 1f);
    public Color glowColor = new Color(1f, 0.55f, 0f, 1f);

    [Header("Ring")]
    [Range(0.1f, 0.5f)] public float radius = 0.42f;
    [Range(0.002f, 0.2f)] public float thickness = 0.035f;
    [Range(0.001f, 0.1f)] public float softness = 0.012f;

    [Header("Sweep")]
    [Tooltip("Revolutions per second. Negative reverses direction.")]
    [Range(-3f, 3f)] public float sweepSpeed = 0.6f;
    [Range(0.02f, 1f)] public float sweepWidth = 0.35f;
    [Range(1f, 6f)] public float sweepBrightness = 2.6f;
    [Range(0f, 1f)] public float baseBrightness = 0.35f;

    [Header("Outer Glow")]
    [Range(0f, 0.3f)] public float glowSize = 0.06f;
    [Range(0f, 2f)] public float glowStrength = 0.8f;

    private Image image;
    private Material runtimeMaterial;

    private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int SweepSpeedId = Shader.PropertyToID("_SweepSpeed");
    private static readonly int SweepWidthId = Shader.PropertyToID("_SweepWidth");
    private static readonly int SweepBoostId = Shader.PropertyToID("_SweepBoost");
    private static readonly int BaseBrightId = Shader.PropertyToID("_BaseBrightness");
    private static readonly int GlowSizeId = Shader.PropertyToID("_GlowSize");
    private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");

    private void OnEnable()
    {
        image = GetComponent<Image>();
        Build();
        Apply();
    }

    private void OnDisable()
    {
        // The material is created per-instance, so it must be cleaned up or it
        // leaks one material per object every time the scene reloads.
        if (runtimeMaterial == null) return;

        if (Application.isPlaying) Destroy(runtimeMaterial);
        else DestroyImmediate(runtimeMaterial);

        runtimeMaterial = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        Apply();
    }
#endif

    private void Build()
    {
        if (image == null) return;
        if (runtimeMaterial != null) return;

        Shader shader = Shader.Find(ShaderName);

        if (shader == null)
        {
            Debug.LogWarning(
                $"[NeonBorder] Shader '{ShaderName}' not found — is it included in the build? " +
                "Disabling so the Image renders normally.", this);
            enabled = false;
            return;
        }

        runtimeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        image.material = runtimeMaterial;

        // The shader draws everything itself; it just needs a white source so
        // the vertex colour is not multiplied by a stale sprite.
        image.color = Color.white;
        image.raycastTarget = false;
    }

    /// <summary>Pushes the Inspector values into the material. Safe to call any time.</summary>
    public void Apply()
    {
        if (runtimeMaterial == null) Build();
        if (runtimeMaterial == null) return;

        runtimeMaterial.SetColor(NeonColorId, neonColor);
        runtimeMaterial.SetColor(GlowColorId, glowColor);
        runtimeMaterial.SetFloat(RadiusId, radius);
        runtimeMaterial.SetFloat(ThicknessId, thickness);
        runtimeMaterial.SetFloat(SoftnessId, softness);
        runtimeMaterial.SetFloat(SweepSpeedId, sweepSpeed);
        runtimeMaterial.SetFloat(SweepWidthId, sweepWidth);
        runtimeMaterial.SetFloat(SweepBoostId, sweepBrightness);
        runtimeMaterial.SetFloat(BaseBrightId, baseBrightness);
        runtimeMaterial.SetFloat(GlowSizeId, glowSize);
        runtimeMaterial.SetFloat(GlowStrengthId, glowStrength);
    }
}
