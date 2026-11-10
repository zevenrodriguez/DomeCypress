// Alpha-clipped, two-sided leaf cards for the cypress canopy (URP).
// Samples the cypress spray atlas, clips at _Cutoff in every pass (so shadows and depth match the
// leaf silhouettes), varies the tint slowly across the canopy, and adds back-lit translucency.
Shader "Cypress/LeafCard"
{
    Properties
    {
        _BaseMap ("Leaf Atlas", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _TintA ("Tint Shade", Color) = (0.75, 0.85, 0.65, 1)
        _TintB ("Tint Sun", Color) = (1.12, 1.06, 0.85, 1)
        _TintScale ("Tint Variation Scale", Float) = 0.35
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _Translucency ("Translucency", Range(0, 2)) = 0.8
        _TranslucencyColor ("Translucency Color", Color) = (0.8, 0.95, 0.35, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "CypressCommon.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _Cutoff;
            half4 _TintA;
            half4 _TintB;
            float _TintScale;
            half _Smoothness;
            half _Translucency;
            half4 _TranslucencyColor;
        CBUFFER_END

        struct CardAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct CardShadowVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // _LightDirection / _LightPosition (shadow caster) are declared in MangroveCommon.hlsl

        CardShadowVaryings CardShadowVert(CardAttributes input)
        {
            CardShadowVaryings output = (CardShadowVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
        #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
            float3 lightDirectionWS = normalize(_LightPosition - positionWS);
        #else
            float3 lightDirectionWS = _LightDirection;
        #endif
            output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
        #if UNITY_REVERSED_Z
            output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
        #else
            output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
        #endif
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return output;
        }

        CardShadowVaryings CardDepthVert(CardAttributes input)
        {
            CardShadowVaryings output = (CardShadowVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return output;
        }

        half4 CardClipFrag(CardShadowVaryings input) : SV_Target
        {
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
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
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(CardAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(tex.a - _Cutoff);

                half3 tint = lerp(_TintA.rgb, _TintB.rgb, MgNoise(input.positionWS * _TintScale));
                half3 albedo = tex.rgb * tint;
                float3 N = normalize(input.normalWS) * (isFrontFace ? 1 : -1);
                half3 color = MgLightPBR(input.positionWS, N, albedo, _Smoothness, input.positionCS, input.fogFactor);

                // light coming through the leaves from behind
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half back = saturate(dot(-N, light.direction)) * (0.5 + 0.5 * pow(saturate(dot(-V, light.direction)), 4));
                color += albedo * _TranslucencyColor.rgb * light.color * light.shadowAttenuation * back * _Translucency;
                return half4(color, 1);
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
            #pragma vertex CardShadowVert
            #pragma fragment CardClipFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
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
            #pragma vertex CardDepthVert
            #pragma fragment CardClipFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    FallBack Off
}
