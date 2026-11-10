// Orchid root material for URP, matching the OrchidRoot material in DomeCypress/roots.blend:
// green velamen with light/dark speckle, a brighter yellow-green growing tip, and blotchy brown
// patches (kept off the tips). Procedural in object space; the mesh comes from OrchidRootSphere.cs,
// which puts the tip amount (0 = body, 1 = tip) in uv.x.
Shader "Cypress/OrchidRoot"
{
    Properties
    {
        [Header(Green)]
        _GreenDark ("Green Dark", Color) = (0.485, 0.767, 0.293, 1)
        _GreenLight ("Green Light", Color) = (0.61, 0.865, 0.381, 1)
        _TipColor ("Growing Tip", Color) = (0.701, 0.931, 0.381, 1)
        _SpeckleScale ("Speckle Scale", Float) = 18

        [Header(Brown Patches)]
        _BrownDark ("Brown Dark", Color) = (0.437, 0.313, 0.19, 1)
        _BrownLight ("Brown Light", Color) = (0.665, 0.527, 0.381, 1)
        _BrownScale ("Patch Scale", Float) = 2.2
        _BrownStart ("Patch Threshold (lower = more brown)", Range(0, 1)) = 0.4
        _BrownSoftness ("Patch Edge Softness", Range(0.01, 0.5)) = 0.2

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _BumpHeight ("Bump Height (m)", Range(0, 0.02)) = 0.004

        [Header(Fade)]
        _Fade ("Fade (1 = solid, 0 = gone; set by OrchidRootSphere.cs)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Assets/Cypress/Shaders/CypressCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GreenDark;
            half4 _GreenLight;
            half4 _TipColor;
            float _SpeckleScale;
            half4 _BrownDark;
            half4 _BrownLight;
            float _BrownScale;
            half _BrownStart;
            half _BrownSoftness;
            half _Smoothness;
            float _BumpHeight;
            half _Fade;
        CBUFFER_END

        // Fade: a fine 4x4 screen-door pattern drops more pixels as _Fade goes from 1 to 0.
        // The roots stay opaque, so overlapping roots never sort wrong while fading.
        static const float FadeBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        void FadeClip(float2 pixel)
        {
            if (_Fade >= 0.999) return;
            uint2 p = (uint2)pixel % 4;
            clip(_Fade - (FadeBayer[p.y * 4 + p.x] + 0.5) / 16.0);
        }

        // shadow and depth passes: same fade, nothing else to draw
        half4 FadeOnlyFrag(float4 positionCS : SV_POSITION) : SV_Target
        {
            FadeClip(positionCS.xy);
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float  tip        : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.tip = input.uv.x;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                FadeClip(input.positionCS.xy);

                float3 p = input.positionOS;
                float3 N = normalize(input.normalWS) * (isFrontFace ? 1 : -1);
                half tip = saturate(input.tip);

                // green velamen with fine light/dark speckle, brighter at the growing tip
                float speckle = MgNoise(p * _SpeckleScale);
                half3 col = lerp(_GreenDark.rgb, _GreenLight.rgb, speckle);
                col = lerp(col, _TipColor.rgb, tip);

                // blotchy brown patches, never on the tips
                float patchN = MgFbm(p * _BrownScale, 4);
                half mask = smoothstep(_BrownStart, _BrownStart + _BrownSoftness, patchN) * (1 - tip);
                half3 brown = lerp(_BrownDark.rgb, _BrownLight.rgb, MgNoise(p * 40.0));
                col = lerp(col, brown, mask);

                N = MgBumpNormal(N, input.positionWS, speckle * _BumpHeight);
                return half4(MgLightPBR(input.positionWS, N, col, _Smoothness, input.positionCS, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex MgShadowVert
            #pragma fragment FadeOnlyFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex MgDepthVert
            #pragma fragment FadeOnlyFrag
            ENDHLSL
        }
    }

    FallBack Off
}
