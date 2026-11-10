// Peat hummocks for URP, exported from BlenderTools/export_cypress_unity.py.
// Vertex color rgb = sand / mud / reef / seagrass tint, alpha = muck amount; uv0.x = wetness.
// Muck areas get dark organic mud with silty specks, puddly sheen and lumpy relief.
Shader "Cypress/Ground"
{
    Properties
    {
        [Header(Muck)]
        _MudDark ("Mud Dark", Color) = (0.112, 0.093, 0.07, 1)
        _MudMid ("Mud Mid", Color) = (0.206, 0.176, 0.133, 1)
        _MudLight ("Mud Light", Color) = (0.293, 0.248, 0.183, 1)
        _SpeckColor ("Leaf Speck Color", Color) = (0.437, 0.349, 0.235, 1)
        _MuckScale ("Muck Scale", Float) = 1.5

        [Header(Underwater Leaf Litter)]
        _LitterTex ("Pond-Bed Litter (tileable)", 2D) = "black" {}
        _LitterScale ("Litter Tiles Per Meter", Float) = 0.6
        _LitterTint ("Litter Tint", Color) = (1, 1, 1, 1)
        _LitterBand ("Litter Blend (top, bottom) y", Vector) = (0.03, -0.12, 0, 0)

        [Header(Surface)]
        _DrySmoothness ("Dry Smoothness", Range(0, 1)) = 0.08
        _WetSmoothness ("Wet Smoothness", Range(0, 1)) = 0.7
        _PuddleSmoothness ("Puddle Smoothness", Range(0, 1)) = 0.88
        _BumpHeight ("Bump Height (m)", Range(0, 0.1)) = 0.03
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "CypressCommon.hlsl"

        TEXTURE2D(_LitterTex);
        SAMPLER(sampler_LitterTex);

        CBUFFER_START(UnityPerMaterial)
            float _LitterScale;
            half4 _LitterTint;
            float4 _LitterBand;
            half4 _MudDark;
            half4 _MudMid;
            half4 _MudLight;
            half4 _SpeckColor;
            float _MuckScale;
            half _DrySmoothness;
            half _WetSmoothness;
            half _PuddleSmoothness;
            float _BumpHeight;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2; // rgb linear tint, a = muck
                half   wet        : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = half4(MgToLinear(input.color.rgb), input.color.a);
                output.wet = input.uv.x;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p = input.positionWS;
                half muck = saturate(input.color.a);

                // dark organic mud with silty streaks and specks of broken-down leaves
                float n = MgFbm(p * _MuckScale, 5);
                half3 mud = lerp(lerp(_MudDark.rgb, _MudMid.rgb, smoothstep(0.3, 0.55, n)), _MudLight.rgb, smoothstep(0.55, 0.75, n));
                mud = lerp(mud, _SpeckColor.rgb, 1 - smoothstep(0.12, 0.2, MgCells(p * 22.0)));
                half3 albedo = lerp(input.color.rgb, mud, muck);

                // below the waterline the bed is a mat of sunken leaves; two rotated scales hide the tiling
                half under = smoothstep(_LitterBand.x, _LitterBand.y, p.y);
                if (under > 0.001)
                {
                    float2 uv = p.xz * _LitterScale;
                    float2 uv2 = float2(uv.x * 0.8 - uv.y * 0.6, uv.x * 0.6 + uv.y * 0.8) * 0.43 + 0.37;
                    half3 litA = SAMPLE_TEXTURE2D(_LitterTex, sampler_LitterTex, uv).rgb;
                    half3 litB = SAMPLE_TEXTURE2D(_LitterTex, sampler_LitterTex, uv2).rgb;
                    half3 litter = lerp(litA, litB, smoothstep(0.35, 0.65, MgNoise(p * 0.7))) * _LitterTint.rgb;
                    albedo = lerp(albedo, litter, under);
                }

                // wet sand / mud is glossier; muck gets puddly patches
                half smoothness = lerp(_DrySmoothness, _WetSmoothness, saturate(input.wet));
                half puddle = lerp(0.35, _PuddleSmoothness, smoothstep(0.45, 0.6, MgNoise(p * 4.0)));
                smoothness = lerp(smoothness, puddle, muck);
                smoothness = lerp(smoothness, 0.55, under);

                float height = lerp(MgNoise(p * 45.0) * 0.1, MgFbm(p * 3.0, 3), muck) * _BumpHeight;
                float3 N = MgBumpNormal(normalize(input.normalWS), p, height);
                return half4(MgLightPBR(p, N, albedo, smoothness, input.positionCS, input.fogFactor), 1);
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

            HLSLPROGRAM
            #pragma vertex MgShadowVert
            #pragma fragment MgNullFrag
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

            HLSLPROGRAM
            #pragma vertex MgDepthVert
            #pragma fragment MgNullFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    FallBack Off
}
