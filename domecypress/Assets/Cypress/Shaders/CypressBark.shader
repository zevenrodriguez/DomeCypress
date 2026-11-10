// Cypress bark for URP, matching the layered bark material in DomeCypress/build_hybrid_swamp.py.
// Procedural (no textures/UVs): grey bark with cinnamon-red inner bark showing through, long vertical
// plates with dark cracks, a near-black wet band at the waterline, a mossy band just above it,
// and moss on upward-facing surfaces (branches, logs, buttress shoulders).
// Works in object space; the export keeps the water line at object y = 0.
Shader "Cypress/Bark"
{
    Properties
    {
        [Header(Bark)]
        _GreyColor ("Weathered Grey", Color) = (0.28, 0.25, 0.21, 1)
        _CinnamonColor ("Inner Bark (cinnamon)", Color) = (0.17, 0.085, 0.05, 1)
        _CrackColor ("Crack Color", Color) = (0.09, 0.07, 0.05, 1)
        _PaleAmount ("Pale Amount", Range(0, 1)) = 0.5
        _PlateScale ("Plate Scale (across, along)", Vector) = (9, 1.2, 0, 0)
        _PatchScale ("Cinnamon Patch Scale (across, along)", Vector) = (1.6, 0.25, 0, 0)

        [Header(Waterline)]
        _WetColor ("Wet Band Color", Color) = (0.012, 0.011, 0.008, 1)
        _WetBand ("Wet Band (top start, top end) m", Vector) = (0.25, 0.75, 0, 0)
        _MossColorA ("Moss Dark", Color) = (0.05, 0.07, 0.015, 1)
        _MossColorB ("Moss Light", Color) = (0.22, 0.28, 0.04, 1)
        _MossBand ("Moss Band (top start, top end) m", Vector) = (1.0, 1.9, 0, 0)
        _BandNoise ("Band Edge Noise (m)", Float) = 0.6

        [Header(Top Moss)]
        _MossTop ("Moss On Upward Faces", Range(0, 1)) = 0.55

        [Header(Surface)]
        _Smoothness ("Dry Smoothness", Range(0, 1)) = 0.12
        _WetSmoothness ("Wet Smoothness", Range(0, 1)) = 0.65
        _BumpHeight ("Bump Height (m)", Range(0, 0.03)) = 0.008
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "CypressCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GreyColor;
            half4 _CinnamonColor;
            half4 _CrackColor;
            half _PaleAmount;
            float4 _PlateScale;
            float4 _PatchScale;
            half4 _WetColor;
            float4 _WetBand;
            half4 _MossColorA;
            half4 _MossColorB;
            float4 _MossBand;
            float _BandNoise;
            half _MossTop;
            half _Smoothness;
            half _WetSmoothness;
            float _BumpHeight;
        CBUFFER_END
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
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
                output.positionOS = input.positionOS.xyz;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p = input.positionOS;
                float3 N = normalize(input.normalWS) * (isFrontFace ? 1 : -1);

                // long vertical plates separated by dark cracks
                float plates = MgFbm(float3(p.x * _PlateScale.x, p.y * _PlateScale.y, p.z * _PlateScale.x), 4);
                half ridge = smoothstep(0.36, 0.5, plates);
                float fibre = MgNoise(float3(p.x * 40.0, p.y * 3.0, p.z * 40.0));

                // grey outer bark vs cinnamon inner bark patches
                float patchN = MgFbm(float3(p.x * _PatchScale.x, p.y * _PatchScale.y, p.z * _PatchScale.x), 3);
                half pale = smoothstep(1.0 - _PaleAmount - 0.05, 1.0 - _PaleAmount + 0.05, patchN);
                half3 bark = lerp(_CinnamonColor.rgb, _GreyColor.rgb, pale);
                bark *= lerp(0.75, 1.1, fibre);
                bark = lerp(_CrackColor.rgb, bark, ridge);

                // noisy height bands: black wet band at the waterline, moss band above it
                float zn = p.y + MgNoise(p * 2.5) * _BandNoise;
                half mossN = MgFbm(p * 6.0, 3);
                half3 moss = lerp(_MossColorA.rgb, _MossColorB.rgb, mossN);
                half3 reddishWet = lerp(half3(0.10, 0.04, 0.02), moss, mossN);
                half mossBand = smoothstep(_MossBand.x, _MossBand.y, zn);
                half3 col = lerp(reddishWet, bark, mossBand);
                half wetBand = smoothstep(_WetBand.x, _WetBand.y, zn);
                col = lerp(_WetColor.rgb, col, wetBand);

                // moss on upward-facing surfaces
                half top = smoothstep(1.0 - _MossTop, 1.25 - _MossTop, N.y) * mossN;
                col = lerp(col, moss, saturate(top));

                half smoothness = lerp(_WetSmoothness, _Smoothness, smoothstep(_WetBand.x * 0.8, _WetBand.y * 1.2, zn));
                float height = (ridge * 0.7 + fibre * 0.3) * _BumpHeight;
                N = MgBumpNormal(N, input.positionWS, height);
                return half4(MgLightPBR(input.positionWS, N, col, smoothness, input.positionCS, input.fogFactor), 1);
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
            Cull Off

            HLSLPROGRAM
            #pragma vertex MgDepthVert
            #pragma fragment MgNullFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    FallBack Off
}
