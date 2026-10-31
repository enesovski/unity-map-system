Shader "Hidden/Elysium/MapFogRevealSplat"
{
    Properties
    {
        _MainTex ("Existing Mask", 2D) = "black" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "RevealSplat"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float2 _SplatCenter;
            float2 _SplatRadius;
            half _SplatEdgeSoftness;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half existingReveal = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                float2 normalizedOffset = (input.uv - _SplatCenter) / max(_SplatRadius, 0.00001);
                float distanceToCenter = length(normalizedOffset);
                half reveal = 1.0 - smoothstep(1.0 - _SplatEdgeSoftness, 1.0, distanceToCenter);
                half combinedReveal = max(existingReveal, reveal);

                return half4(combinedReveal, combinedReveal, combinedReveal, 1.0);
            }
            ENDHLSL
        }
    }
}
