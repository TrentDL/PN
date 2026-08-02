// Live2DMotionBlur.shader
// Faithful 1:1 port of the Godot "2D Motion Blur" shader (canvas_item) by Polygon Tweaker
// (godotshaders.com/shader/2d-motion-blur/), adapted for a full-screen RawImage that displays
// a flattened RenderTexture of a masked Live2D character.
//
// Works on a UI RawImage in URP (UI uses the legacy render path). A URP-native HLSL version
// is in the setup guide if you need it on a world-space mesh instead.
//
// Driven from Live2DMotionBlur.cs via:  _BlurDir (Godot 'dir')  and  _Quality (Godot 'quality').
//
// MODIFIED: the vertex stage now expands the quad geometry (port of RadialBlur's approach)
// so the directional smear can bleed PAST the quad's original edges instead of being clipped.
// See accompanying setup guide (Live2DMotionBlur_GeometryExpansion_Guide.md).

Shader "Custom/Live2DMotionBlurFixed"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}                 // = Godot TEXTURE (set by the RawImage)
        _BlurDir ("Blur Direction (UV)", Vector) = (0,0,0,0)  // = Godot 'uniform vec2 dir' (uses .xy)
        _Quality ("Quality (samples per side)", Int) = 4       // = Godot 'uniform int quality'
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            sampler2D _MainTex;
            float4 _BlurDir;   // Godot: uniform vec2 dir -> .xy
            int    _Quality;   // Godot: uniform int quality

            v2f vert (appdata v)
            {
                v2f o;

                // === ADDED: geometry expansion (directional analog of RadialBlur's vert) ===
                // RadialBlur grew the quad by  expand = 2*length(_Pivot) + ROOT_TWO  so the
                // ROTATED samples had geometry to land on. A directional blur is simpler:
                // the smear only ever reaches +/- _BlurDir in UV space, so we only need a
                // margin of length(_BlurDir) on EACH side -> total scale = 1 + 2*length(dir).
                float2 dir    = _BlurDir.xy;                 // ADDED: smear vector (UV units)
                float  expand = 1.0 + 2.0 * length(dir);     // ADDED: 1 = original quad, +2*|dir| = margin both sides

                // ADDED: scale the quad's vertices outward around its local origin.
                // (Assumes a centered quad, exactly like RadialBlur's pos.xy * expand.)
                float3 pos = v.vertex.xyz;                   // ADDED
                pos.xy     = pos.xy * expand;                // ADDED: grow geometry past original edges
                o.pos      = UnityObjectToClipPos(float4(pos, v.vertex.w)); // CHANGED: was UnityObjectToClipPos(v.vertex)

                // ADDED: scale UVs to match, so the original texture still maps onto the
                // CENTER 1/expand of the now-larger quad. The frag's insideUnitSquare()
                // then masks everything outside [0,1] back to transparent -> clean smear.
                o.uv = (v.uv - 0.5) * expand + 0.5;          // CHANGED: was o.uv = v.uv
                // === END ADDED ===

                o.color = v.color;
                return o;
            }

            // Godot insideUnitSquare(): 1.0 inside [0,1]x[0,1], else 0.0
            float insideUnitSquare(float2 v)
            {
                float2 s = step(float2(0.0, 0.0), v) - step(float2(1.0, 1.0), v);
                return s.x * s.y;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 dir    = _BlurDir.xy;
                int   quality = max(_Quality, 1);

                float inSquare   = insideUnitSquare(i.uv);
                float numSamples = inSquare;
                fixed4 col       = tex2D(_MainTex, i.uv) * inSquare;

                float2 stepSize = dir / (float)quality;
                float2 uv;

                for (int s = 1; s <= quality; s++)
                {
                    uv = i.uv + stepSize * (float)s;
                    inSquare = insideUnitSquare(uv);
                    numSamples += inSquare;
                    col += tex2D(_MainTex, uv) * inSquare;

                    uv = i.uv - stepSize * (float)s;
                    inSquare = insideUnitSquare(uv);
                    numSamples += inSquare;
                    col += tex2D(_MainTex, uv) * inSquare;
                }

                col.rgb /= max(numSamples, 1e-5);         // Godot: COLOR.rgb /= numSamples  (color over VALID samples)
                col.a   /= ((float)quality * 2.0 + 1.0);  // Godot: COLOR.a /= quality*2+1   (alpha over TOTAL -> soft trail)
                return col * i.color;                       // respect RawImage tint/alpha
            }
            ENDCG
        }
    }
}
