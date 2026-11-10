// Low-lying morning mist for URP: a few large horizontal layers with drifting noise.
// Cheap stand-in for volumetric fog: unlit, no shadows, 2 noise octaves per pixel. Soft-intersects
// trunks/ground via the depth texture, fades near the camera and toward the layer's edge.
Shader "Cypress/Mist"
{
    Properties
    {
        _MistColor ("Mist Color", Color) = (0.78, 0.78, 0.8, 1)
        _Density ("Density", Range(0, 1)) = 0.35
        _NoiseScale ("Noise Scale", Float) = 0.06
        _Breakup ("Breakup", Range(0, 1)) = 0.55
        _DriftSpeed ("Drift Speed", Vector) = (0.12, 0.05, 0, 0)
        _SoftDistance ("Soft Intersection (m)", Float) = 1.2
        _CameraFade ("Fade Near Camera (m)", Float) = 2.5
        _EdgeRadius ("Edge Fade Radius (m)", Float) = 55
        _SunScatter ("Sun Scatter", Range(0, 3)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Mist"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MistColor;
                half _Density;
                float _NoiseScale;
                half _Breakup;
                float4 _DriftSpeed;
                float _SoftDistance;
                float _CameraFade;
                float _EdgeRadius;
                half _SunScatter;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 centerWS   : TEXCOORD1;
                float  viewDepth  : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.centerWS = TransformObjectToWorld(float3(0, 0, 0));
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.viewDepth = -TransformWorldToView(o.positionWS).z;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                float2 uv = p.xz * _NoiseScale + p.y * 3.7;                 // each layer height gets its own pattern
                float t = _Time.y;
                float n = Noise(uv + t * _DriftSpeed.xy) * 0.65 + Noise(uv * 2.9 - t * _DriftSpeed.xy * 1.7) * 0.35;
                half a = lerp(1.0, smoothstep(0.25, 0.85, n), _Breakup) * _Density;

                // soft intersection with the scene behind
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                a *= saturate((sceneDepth - input.viewDepth) / max(_SoftDistance, 1e-3));
                // fade close to the camera and at the edge of the layer
                a *= saturate((input.viewDepth - 0.3) / max(_CameraFade, 1e-3));
                a *= 1.0 - smoothstep(_EdgeRadius * 0.7, _EdgeRadius, length(p.xz - input.centerWS.xz));

                // lit by the sky, with forward scattering toward the (low) sun
                Light light = GetMainLight();
                float3 V = normalize(p - GetCameraPositionWS());
                half scatter = pow(saturate(dot(V, light.direction)), 4) * _SunScatter;   // glows when looking toward the sun
                half3 col = _MistColor.rgb * (SampleSH(float3(0, 1, 0)) + light.color * (0.25 + scatter));
                col = MixFog(col, input.fogFactor);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
