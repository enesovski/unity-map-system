Shader "Elysium/UI/MapFogOverlay_Optimized"
{
    Properties
    {
        [Header(Mask)]
        [MainTexture] _FogMask ("Fog Mask", 2D) = "black" {}
        // The fog quad is larger than the map so that fog still covers the view when zoomed out
        // or panned past the world's edge. This maps the mask onto the map's share of the quad;
        // outside it the clamped sampler returns the mask's unexplored edge, i.e. solid fog.
        _FogMaskScale ("Fog Mask Scale (quad size / map size)", Float) = 1.0
        _FogAreaMask ("Fog Area Mask", 2D) = "white" {}
        _FogAreaMaskStrength ("Fog Area Mask Strength", Range(0, 1)) = 1.0
        _FogAreaMaskThreshold ("Fog Area Mask Threshold", Range(0, 1)) = 0.5
        _FogAreaMaskSoftness ("Fog Area Mask Softness", Range(0.001, 0.5)) = 0.08

        [Header(Fog Colors)]
        _FogColor ("Fog Color", Color) = (0.13, 0.20, 0.30, 1)
        _FogColorDeep ("Fog Deep Color", Color) = (0.05, 0.09, 0.17, 1)
        _FogOpacity ("Fog Opacity", Range(0, 1)) = 0.95
        _RevealedOpacity ("Revealed Area Opacity", Range(0, 1)) = 0.0

        [Header(Edge Shape)]
        _EdgeMidpoint ("Edge Midpoint", Range(0.0, 1.0)) = 0.5
        _EdgeWidth ("Edge Width", Range(0.001, 0.6)) = 0.28
        _EdgeSoftness ("Edge Softness", Range(0.05, 1.0)) = 0.55

        [Header(Noise)]
        _NoiseTex ("Noise Texture", 2D) = "gray" {}
        _WarpScale ("Warp Scale", Float) = 2.0
        _WarpStrength ("Warp Strength", Range(0, 0.3)) = 0.055
        _WarpDetailScale ("Warp Detail Scale", Float) = 6.5
        _WarpDetailStrength ("Warp Detail Strength", Range(0, 0.15)) = 0.0
        _EdgeNoiseScale ("Edge Noise Scale", Float) = 3.5
        _EdgeNoiseStrength ("Edge Noise Strength", Range(0, 0.6)) = 0.16
        _CloudScale ("Cloud Scale", Float) = 3.0
        _CloudStrength ("Cloud Strength", Range(0, 1)) = 0.30
        _FogDriftSpeed ("Fog Drift Speed", Range(0, 0.2)) = 0.008
        _FogDriftDirection ("Fog Drift Direction", Vector) = (1, 0.3, 0, 0)

        [Header(Rim)]
        [HDR] _RimColor ("Rim Color", Color) = (0.03, 0.05, 0.10, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.35
        _RimWidth ("Rim Width", Range(0.01, 0.6)) = 0.24

        [HideInInspector] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _StencilComp ("Stencil Comp", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Op", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIFogOverlayOptimized"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_FogMask);
            SAMPLER(sampler_FogMask);
            TEXTURE2D(_FogAreaMask);
            SAMPLER(sampler_FogAreaMask);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FogMask_TexelSize;
                float _FogMaskScale;

                half _FogAreaMaskStrength;
                half _FogAreaMaskThreshold;
                half _FogAreaMaskSoftness;

                half4 _FogColor;
                half4 _FogColorDeep;
                half _FogOpacity;
                half _RevealedOpacity;

                half _EdgeMidpoint;
                half _EdgeWidth;
                half _EdgeSoftness;

                float _WarpScale;
                half _WarpStrength;
                float _WarpDetailScale;
                half _WarpDetailStrength;

                float _EdgeNoiseScale;
                half _EdgeNoiseStrength;

                float _CloudScale;
                half _CloudStrength;
                half _FogDriftSpeed;
                half4 _FogDriftDirection;

                half4 _RimColor;
                half _RimStrength;
                half _RimWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half SampleNoise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).r;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Remap the oversized quad onto the map's footprint. Everything from here on
                // works in map space, so the fog's look over the world is unchanged and the
                // surrounding area samples the mask's clamped edge — solid, unexplored fog.
                float2 uv = (IN.uv - 0.5) * max(_FogMaskScale, 0.0001) + 0.5;

                float aspect = _FogMask_TexelSize.z / max(_FogMask_TexelSize.w, 1.0);
                float2 noiseUV = float2(uv.x * aspect, uv.y);
                float2 drift = _FogDriftDirection.xy * (_FogDriftSpeed * _Time.y);

                half coarseNoise = SampleNoise(noiseUV * _WarpScale + drift);
                half edgeNoise = SampleNoise(noiseUV * _EdgeNoiseScale + drift * 0.5h);
                half cloudNoise = SampleNoise(noiseUV * _CloudScale - drift * 0.7h);

                half2 warp = half2(coarseNoise - 0.5h, edgeNoise - 0.5h) * _WarpStrength;
                half maskRamp = saturate(SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, uv + warp).r);

                half center = _EdgeMidpoint + (edgeNoise - 0.5h) * _EdgeNoiseStrength;
                half reveal = smoothstep(center - _EdgeWidth, center + _EdgeWidth, maskRamp);
                half hidden = 1.0h - reveal;

                half softFactor = lerp(1.35h, 0.65h, _EdgeSoftness);
                hidden = saturate(hidden * softFactor + hidden * hidden * (1.0h - softFactor));

                half rimBase = saturate(hidden / max(_RimWidth, 0.001h));
                half rim = rimBase * (1.0h - rimBase) * 4.0h;

                half cloudMod = lerp(1.0h, cloudNoise, _CloudStrength);
                half3 col = lerp(_FogColor.rgb, _FogColorDeep.rgb, saturate(hidden * cloudMod));
                col = lerp(col, _RimColor.rgb, rim * _RimStrength);

                half coverage = lerp(_RevealedOpacity, _FogOpacity, hidden);
                half edge = hidden * (1.0h - hidden) * 4.0h;
                half alpha = saturate(coverage + (cloudNoise - 0.5h) * _CloudStrength * edge);
                alpha = saturate(alpha + rim * _RimStrength * _RimColor.a * 0.5h);

                half fogAreaRaw = SAMPLE_TEXTURE2D(_FogAreaMask, sampler_FogAreaMask, uv).r;
                half fogArea = smoothstep(
                    _FogAreaMaskThreshold - _FogAreaMaskSoftness,
                    _FogAreaMaskThreshold + _FogAreaMaskSoftness,
                    fogAreaRaw
                );
                alpha *= lerp(1.0h, fogArea, _FogAreaMaskStrength);

                col *= IN.color.rgb;
                alpha *= IN.color.a;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
