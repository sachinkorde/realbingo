// Rotating neon boundary for the special 'J' (Jackpot) chip and slot.
// PRD Topic 5.4 / 6.1: "a high-contrast, fine neon boundary outline ... this
// outline must continuously rotate at a fixed angular velocity (360 degree loop)".
//
// Drawn entirely in the fragment shader — no sprite, no mask, no extra objects.
// A rotating angular sweep is multiplied over a ring, which is what gives the
// "light chasing round the rim" look. Because it is procedural it stays crisp at
// any size and costs one draw call.
//
// This is a UI shader: it keeps the full UI property block (stencil, clip rect,
// colour mask) so Image masking, Mask/RectMask2D and canvas batching all behave
// exactly as they do with UI-Default. Drop it on a plain Image.

Shader "RealBingo/UI Neon Border"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _NeonColor      ("Neon Colour", Color) = (1, 0.85, 0.1, 1)
        _GlowColor      ("Glow Colour", Color) = (1, 0.55, 0.0, 1)

        _Radius         ("Ring Radius", Range(0.1, 0.5)) = 0.42
        _Thickness      ("Ring Thickness", Range(0.002, 0.2)) = 0.035
        _Softness       ("Edge Softness", Range(0.001, 0.1)) = 0.012

        _SweepSpeed     ("Sweep Speed (rev/sec)", Range(-3, 3)) = 0.6
        _SweepWidth     ("Sweep Width", Range(0.02, 1)) = 0.35
        _SweepBoost     ("Sweep Brightness", Range(1, 6)) = 2.6
        _BaseBrightness ("Base Ring Brightness", Range(0, 1)) = 0.35

        _GlowSize       ("Outer Glow Size", Range(0, 0.3)) = 0.06
        _GlowStrength   ("Outer Glow Strength", Range(0, 2)) = 0.8

        // Standard UI plumbing — do not remove, masking depends on these.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "NEON_BORDER"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _NeonColor;
            fixed4 _GlowColor;
            float  _Radius;
            float  _Thickness;
            float  _Softness;
            float  _SweepSpeed;
            float  _SweepWidth;
            float  _SweepBoost;
            float  _BaseBrightness;
            float  _GlowSize;
            float  _GlowStrength;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Centre the UV so the ring maths is in -0.5..0.5 space.
                float2 p = IN.texcoord - 0.5;
                float dist = length(p);

                // Ring: 1 on the band, fading out over _Softness on both sides.
                float halfThickness = _Thickness * 0.5;
                float ring = smoothstep(halfThickness + _Softness, halfThickness,
                                        abs(dist - _Radius));

                // Angular sweep. atan2 gives -PI..PI; normalise to 0..1 then
                // scroll it over time so the highlight chases round the rim.
                float angle = atan2(p.y, p.x) / (2.0 * UNITY_PI) + 0.5;
                float phase = frac(angle - _Time.y * _SweepSpeed);

                // Triangular pulse so the bright head fades smoothly into a tail.
                float sweep = saturate(1.0 - phase / max(_SweepWidth, 0.0001));
                sweep = sweep * sweep;

                float intensity = _BaseBrightness + sweep * _SweepBoost;

                // Outer bloom, strongest just beyond the ring.
                float glowBand = smoothstep(_Radius + halfThickness + _GlowSize,
                                            _Radius + halfThickness, dist);
                float glow = glowBand * (1.0 - ring) * _GlowStrength * (0.35 + sweep);

                fixed4 col = _NeonColor * ring * intensity + _GlowColor * glow;
                col.a = saturate(ring * intensity + glow);

                col *= IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
