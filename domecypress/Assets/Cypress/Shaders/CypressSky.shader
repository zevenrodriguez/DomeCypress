// Cloudy procedural skybox for the cypress dome (URP).
// Gradient sky + a drifting fbm cloud deck projected onto a flat layer, lit from the main light:
// clouds glow warm on the sun side and go blue-grey away from it; the sun disc dims behind cloud.
// Cheap: only sky pixels pay for it, and it drives ambient light when used as the scene skybox.
Shader "Cypress/CloudySky"
{
    Properties
    {
        [Header(Sky)]
        _ZenithColor ("Zenith", Color) = (0.32, 0.38, 0.5, 1)
        _HorizonColor ("Horizon", Color) = (0.85, 0.75, 0.7, 1)
        _GroundColor ("Below Horizon", Color) = (0.35, 0.36, 0.34, 1)
        _HorizonSharpness ("Horizon Falloff", Range(0.5, 8)) = 2.5
        _Exposure ("Exposure", Range(0, 4)) = 1

        [Header(Sun)]
        _SunColor ("Sun Color", Color) = (1, 0.8, 0.6, 1)
        _SunSize ("Sun Disc Size", Range(0.0005, 0.02)) = 0.003
        _SunGlow ("Sun Glow", Range(0, 4)) = 1.2
        _SunGlowPower ("Sun Glow Tightness", Range(2, 128)) = 12

        [Header(Clouds)]
        _CloudCoverage ("Coverage", Range(0, 1)) = 0.65
        _CloudSoftness ("Softness", Range(0.01, 0.5)) = 0.18
        _CloudScale ("Scale", Float) = 1.4
        _CloudSpeed ("Drift Speed", Float) = 0.006
        _CloudLit ("Lit Color", Color) = (0.9, 0.82, 0.78, 1)
        _CloudShadow ("Shadow Color", Color) = (0.42, 0.44, 0.5, 1)
        _CloudSunTint ("Sun-side Rim Tint", Color) = (1, 0.7, 0.5, 1)
        _CloudHorizonFade ("Horizon Fade", Range(0.01, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GroundColor;
                half _HorizonSharpness, _Exposure;
                half4 _SunColor;
                half _SunSize, _SunGlow, _SunGlowPower;
                half _CloudCoverage, _CloudSoftness;
                float _CloudScale, _CloudSpeed;
                half4 _CloudLit, _CloudShadow, _CloudSunTint;
                half _CloudHorizonFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dir = input.positionOS.xyz;
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }
            float Fbm(float2 p)
            {
                float s = 0, a = 0.5;
                [unroll] for (int i = 0; i < 5; i++) { s += a * Noise(p); p = p * 2.07 + 13.1; a *= 0.5; }
                return s / 0.96875;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.dir);
                float3 sunDir = normalize(_MainLightPosition.xyz);
                half3 sunCol = _SunColor.rgb * _MainLightColor.rgb / max(max(_MainLightColor.r, _MainLightColor.g), max(_MainLightColor.b, 0.001));

                // sky gradient
                float up = saturate(d.y);
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, 1.0 / _HorizonSharpness));
                sky = lerp(sky, _GroundColor.rgb, saturate(-d.y * 6.0));
                float mu = saturate(dot(d, sunDir));
                sky += sunCol * _SunGlow * (pow(mu, _SunGlowPower) * 0.6 + pow(mu, 3) * 0.15);   // haze glow around the sun

                // cloud deck: project onto a flat layer above the camera
                float2 uv = d.xz / max(d.y + 0.08, 0.02) * 0.25 * _CloudScale + _Time.y * _CloudSpeed * float2(1, 0.4);
                float n = Fbm(uv);
                float detail = Fbm(uv * 3.1 + 7.7);
                float cloud = smoothstep(1.0 - _CloudCoverage - _CloudSoftness, 1.0 - _CloudCoverage + _CloudSoftness, n * 0.8 + detail * 0.2);
                cloud *= smoothstep(0.0, _CloudHorizonFade, d.y);
                // thicker parts are darker underneath; the sun side gets a warm rim
                float thick = saturate((n - (1.0 - _CloudCoverage)) * 2.5);
                half3 cloudCol = lerp(_CloudLit.rgb, _CloudShadow.rgb, thick);
                cloudCol += _CloudSunTint.rgb * sunCol * pow(mu, 6) * (1 - thick) * 0.8;
                half3 col = lerp(sky, cloudCol, cloud);

                // sun disc, dimmed by cloud
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.6, dot(d, sunDir));
                col += sunCol * disc * 6.0 * (1 - cloud * 0.9);
                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
