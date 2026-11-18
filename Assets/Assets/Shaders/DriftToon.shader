Shader "ChainDrift/DriftToon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map (alpha dùng để cắt)", 2D) = "white" {}
        [MainColor] _BaseColor("Màu sáng", Color) = (1,1,1,1)
        _ShadeColor("Màu tối", Color) = (0.72,0.8,0.94,1)
        _LitAmount("Dùng ánh sáng (0 = màu phẳng)", Range(0,1)) = 0
        _Steps("Số nấc sáng tối", Range(2,6)) = 3
        _DitherStrength("Độ dither (0 = nấc cứng)", Range(0,1)) = 1
        _DitherScale("Cỡ điểm dither (pixel)", Range(1,4)) = 1
        _DissolveAmount("Dither tan dần theo UV.y", Range(0,1)) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadeColor;
                half _LitAmount;
                half _Steps;
                half _DitherStrength;
                half _DitherScale;
                half _DissolveAmount;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            static const float BayerTable[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float Bayer4(float2 pix)
            {
                float2 p = floor(pix / max(_DitherScale, 1.0));
                uint x = ((uint)p.x) & 3u;
                uint y = ((uint)p.y) & 3u;
                return (BayerTable[y * 4u + x] + 0.5) / 16.0;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(tex.a - _Cutoff);

                float b = Bayer4(i.positionCS.xy);
                clip(b - i.uv.y * _DissolveAmount - 0.001);   // dither tan dần (mặc định tắt)

                half3 col = _BaseColor.rgb;
                if (_LitAmount > 0.001)
                {
                    float3 n = normalize(i.normalWS);
                    Light l = GetMainLight();
                    float ndl = saturate(dot(n, l.direction) * 0.5 + 0.5);
                    float lv = max(_Steps - 1.0, 1.0);
                    float q = saturate(floor(ndl * lv + lerp(0.5, b, _DitherStrength)) / lv);
                    half3 lit = lerp(_ShadeColor.rgb, _BaseColor.rgb, q);
                    col = lerp(col, lit, _LitAmount);
                }
                return half4(col * tex.rgb, 1);
            }
            ENDHLSL
        }
    }
}