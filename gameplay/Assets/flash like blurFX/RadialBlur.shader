Shader "Custom/RadialBlur"
{
   Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Motion Blur Settings)]
        _Pivot ("Pivot (x y)", Vector) = (0, 0, 0, 0)
        _Amount ("Amount (radians)", Range(0, 3.14159)) = 0.0
        [IntRange] _Quality ("Quality", Range(1, 16)) = 4
        [Toggle] _MarginDebug ("Margin Debug", Float) = 0

        [Header(Sprite Settings)]
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "RadialMotionBlur"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define ROOT_TWO 1.41421356237

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                half4 _Color;
                float2 _Pivot;
                float _Amount;
                int _Quality;
                float _MarginDebug;
            CBUFFER_END

            // Godot's insideUnitSquare: returns 1.0 if UV is within [0,1], else 0.0
            float InsideUnitSquare(float2 v)
            {
                float2 s = step(0.0.xx, v) - step(1.0.xx, v);
                return s.x * s.y;
            }

            // Rotate UV around point p by angle (radians)
            float2 Rotate(float2 uv, float2 p, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                float2x2 rotation = float2x2(c, -s, s, c);
                uv -= p;
                uv = mul(uv, rotation);
                uv += p;
                return uv;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                // --- Geometry expansion (Godot vertex equivalent) ---
                // NOTE: This assumes a centered quad sprite. See artifact notes
                // for why this needs adjustment on Live2D deformable meshes.
                float expand = 2.0 * length(_Pivot) + ROOT_TWO;

                float3 pos = input.positionOS.xyz;
                pos.xy = pos.xy * expand + _Pivot;

                VertexPositionInputs vertexInput = GetVertexPositionInputs(pos);
                output.positionCS = vertexInput.positionCS;

                // Expand UVs to leave margin for the rotated samples
                float2 uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.uv = (uv - 0.5) * expand + _Pivot + 0.5;

                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float inSquare = InsideUnitSquare(input.uv);
                float numSamples = inSquare;

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * inSquare;

                float stepSize = _Amount / (float)_Quality;
                float2 pivotCenter = _Pivot + 0.5;

                [loop]
                for (int i = 1; i <= _Quality; i++)
                {
                    // Positive rotation
                    float2 uvP = Rotate(input.uv, pivotCenter, (float)i * stepSize);
                    float sP = InsideUnitSquare(uvP);
                    numSamples += sP;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvP) * sP;

                    // Negative rotation
                    float2 uvN = Rotate(input.uv, pivotCenter, -(float)i * stepSize);
                    float sN = InsideUnitSquare(uvN);
                    numSamples += sN;
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvN) * sN;
                }

                // Avoid divide-by-zero outside the square
                color.rgb /= max(numSamples, 0.0001);
                color.a /= (float)_Quality * 2.0 + 1.0;

                if (_MarginDebug > 0.5)
                    color += 0.1;

                // Apply vertex color / tint
                color *= input.color;

                // Premultiply alpha for correct blending
                color.rgb *= color.a;

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
