// ============================================================================
//  CubismLineArtAdaptive.shader
//  ---------------------------------------------------------------------------
//  Purpose: keep a Live2D Cubism ArtMesh that contains ONLY black ink line art
//           readable at any camera zoom / orthographic size, the same way Clip
//           Studio Paint's vector "Adjust line thickness when scaling" keeps a
//           vector stroke readable when you shrink a layer.
//
//  How it works (short version):
//    Live2D line art is a RASTER texture, not a vector, so we cannot literally
//    re-stroke it. Instead we do a screen-space DILATION (a max-filter) of the
//    texture's alpha channel. The dilation radius is measured in SCREEN PIXELS,
//    not texels, so it is derived from ddx/ddy of the UVs. That means:
//       - Zoomed IN  -> one screen pixel covers a fraction of a texel ->
//                       the offsets are tiny -> the art looks untouched.
//       - Zoomed OUT -> one screen pixel covers many texels -> the offsets
//                       automatically grow in texel space -> the ink is pushed
//                       outward and survives minification instead of being
//                       averaged into transparency.
//    After the dilation we re-darken the (now blurred/averaged) alpha with a
//    gain curve, because minified mip samples return coverage, not opacity.
//
//  Target: Unity 6, Live2D Cubism SDK 5 (5-r.5-beta.2), Built-in Render
//          Pipeline, unmasked line-art drawable, normal (alpha) blending.
//
//  NOTE ON MASKING: this is a standalone unlit shader. It does NOT implement
//  Cubism's clipping-mask path (CUBISM_MASK_ON / cubism_MaskTexture). If your
//  line-art ArtMesh is clipped by a mask in the Cubism Editor, copy the frag
//  block marked "DILATION CORE" into a duplicate of the SDK's CubismUnlit
//  shader instead of using this file as-is.
// ============================================================================

Shader "Live2D Cubism/Unlit Line Art Adaptive"
{
    Properties
    {
        // [PerRendererData] because CubismRenderer assigns the model's atlas
        // through a MaterialPropertyBlock at runtime, not through the material.
        [PerRendererData] _MainTex ("Main Texture", 2D) = "white" {}

        // --- Line look -------------------------------------------------------
        _LineColor ("Line Tint", Color) = (0, 0, 0, 1)

        // --- The two knobs you will actually tune ---------------------------
        // How many SCREEN PIXELS of ink to grow outward. 0 = shader does
        // nothing. ~1.0-1.5 reads like a 1px vector stroke at any zoom.
        _GrowPixels ("Grow Amount (screen px)", Range(0, 6)) = 1.2

        // Minified mip samples come back semi-transparent (they are coverage,
        // not ink). This multiplies them back toward solid. Raise until the
        // lines are properly black when the camera is zoomed out.
        _AlphaGain ("Alpha Gain", Range(1, 12)) = 3.0

        // --- Fine tuning -----------------------------------------------------
        // Kills faint atlas noise / anti-alias fringe before the gain amplifies
        // it into a grey halo. Raise if you see fog around the strokes.
        _AlphaFloor ("Ink Threshold", Range(0, 0.5)) = 0.04

        // Negative = sample sharper mips (crisper, but can shimmer when the
        // camera moves). 0 = trust the hardware mip choice.
        _MipBias ("Mip Bias", Range(-4, 0)) = -1.0

        // Safety clamp in UV space so a huge zoom-out cannot make the taps
        // wander across the atlas into a neighbouring ArtMesh's pixels.
        _MaxGrowUV ("Max Grow (UV)", Range(0, 0.1)) = 0.02

        // --- Blend state (Cubism sets these per drawable on its own shaders;
        //     exposed here so the material matches the SDK defaults) ----------
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Int) = 5  // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Int) = 10 // OneMinusSrcAlpha
        [Enum(UnityEngine.Rendering.CullMode)]  _Cull     ("Cull",      Int) = 0  // Off
        [Enum(Off, 0, On, 1)]                   _ZWrite   ("ZWrite",    Int) = 0  // Off
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Blend  [_SrcBlend] [_DstBlend]
            Cull   [_Cull]
            ZWrite [_ZWrite]
            ZTest  LEqual
            Lighting Off
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0        // tex2Dgrad + ddx/ddy need SM3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;

            float4 _LineColor;
            float  _GrowPixels;
            float  _AlphaGain;
            float  _AlphaFloor;
            float  _MipBias;
            float  _MaxGrowUV;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                // Cubism bakes drawable opacity + multiply/screen colour into
                // the mesh's vertex colours (CubismRenderer.ApplyVertexColors),
                // so this MUST be read and multiplied in, or the model will
                // ignore opacity parameters and fade-ins.
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            // 8 unit vectors around a circle. Two rings of these (one rotated
            // 22.5 degrees, at half radius) + the centre = 17 taps total, which
            // is enough to close the gaps for a ~1-2px grow without banding.
            static const float2 RING_OUTER[8] =
            {
                float2( 1.00000,  0.00000), float2( 0.70711,  0.70711),
                float2( 0.00000,  1.00000), float2(-0.70711,  0.70711),
                float2(-1.00000,  0.00000), float2(-0.70711, -0.70711),
                float2( 0.00000, -1.00000), float2( 0.70711, -0.70711)
            };
            static const float2 RING_INNER[8] =
            {
                float2( 0.92388,  0.38268), float2( 0.38268,  0.92388),
                float2(-0.38268,  0.92388), float2(-0.92388,  0.38268),
                float2(-0.92388, -0.38268), float2(-0.38268, -0.92388),
                float2( 0.38268, -0.92388), float2( 0.92388, -0.38268)
            };

            fixed4 frag (v2f i) : SV_Target
            {
                // ============ DILATION CORE (portable block) =================
                // ddx/ddy of the UV give "how much UV does one screen pixel
                // cover, right here, right now". This is the whole trick: it
                // already contains the camera zoom, the model transform, and
                // any non-uniform scale, so no C# script has to feed us the
                // orthographic size.
                float2 duvdx = ddx(i.uv);
                float2 duvdy = ddy(i.uv);

                // Pre-compute the gradient once so every tap uses the SAME mip
                // level. Sampling with tex2D inside a loop would let the GPU
                // derive garbage gradients from the offset UVs.
                float lodScale = exp2(_MipBias);
                float2 gx = duvdx * lodScale;
                float2 gy = duvdy * lodScale;

                // Convert "N screen pixels" into a UV-space offset. Building it
                // from duvdx/duvdy (instead of a scalar texel size) keeps the
                // grow circular on screen even if the ArtMesh is squashed or
                // rotated by a deformer.
                float2 growU = duvdx * _GrowPixels;
                float2 growV = duvdy * _GrowPixels;

                // Clamp so an extreme zoom-out cannot pull in a neighbouring
                // region of the Cubism texture atlas.
                float growLen = max(length(growU), length(growV));
                float limiter = (growLen > _MaxGrowUV) ? (_MaxGrowUV / max(growLen, 1e-6)) : 1.0;
                growU *= limiter;
                growV *= limiter;

                // Max-filter: if ANY sample inside the disc has ink, this pixel
                // becomes ink. That is the dilation / "thicken the stroke" step.
                float a = tex2Dgrad(_MainTex, i.uv, gx, gy).a;

                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    float2 oOuter = RING_OUTER[k].x * growU + RING_OUTER[k].y * growV;
                    float2 oInner = (RING_INNER[k].x * growU + RING_INNER[k].y * growV) * 0.5;

                    a = max(a, tex2Dgrad(_MainTex, i.uv + oOuter, gx, gy).a);
                    a = max(a, tex2Dgrad(_MainTex, i.uv + oInner, gx, gy).a);
                }

                // Re-darken. A minified sample of a 1-texel line returns its
                // coverage (e.g. 0.08), not its opacity, so the max-filter alone
                // still looks grey. Floor first so we amplify ink, not fringe.
                a = saturate((a - _AlphaFloor) / max(1e-4, 1.0 - _AlphaFloor));
                a = saturate(a * _AlphaGain);
                // ============ END DILATION CORE ==============================

                fixed4 col;
                // Ink is a flat tint; we deliberately do NOT use the sampled RGB,
                // because a dilated sample's RGB is meaningless at the new edge.
                // This is why this shader is black-ink-only by design.
                col.rgb = _LineColor.rgb * i.color.rgb;
                col.a   = a * _LineColor.a * i.color.a;

                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
