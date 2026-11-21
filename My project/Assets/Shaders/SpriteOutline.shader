Shader "MidnightScour/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (0.92, 0.18, 0.16, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 24)) = 8
        _GlowColor ("Glow Color", Color) = (0.92, 0.18, 0.16, 0.5)
        _GlowWidth ("Glow Width (px)", Range(0, 40)) = 18
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "SpriteOutline"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _OutlineColor;
                half4 _GlowColor;
                half _OutlineWidth;
                half _GlowWidth;
                half _AlphaClip;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half SampleA(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half MaxAtRadius(float2 uv, float px)
            {
                float2 t = _MainTex_TexelSize.xy * px;
                half a = 0;
                a = max(a, SampleA(uv + float2( t.x,  0)));
                a = max(a, SampleA(uv + float2(-t.x,  0)));
                a = max(a, SampleA(uv + float2( 0,  t.y)));
                a = max(a, SampleA(uv + float2( 0, -t.y)));
                a = max(a, SampleA(uv + float2( t.x,  t.y)));
                a = max(a, SampleA(uv + float2(-t.x,  t.y)));
                a = max(a, SampleA(uv + float2( t.x, -t.y)));
                a = max(a, SampleA(uv + float2(-t.x, -t.y)));
                return a;
            }

            half NeighborMaxWithin(float2 uv, float widthPx)
            {
                half a = 0;
                int steps = (int)ceil(widthPx);
                steps = clamp(steps, 0, 24);
                for (int i = 1; i <= steps; i++)
                    a = max(a, MaxAtRadius(uv, (float)i));
                return a;
            }

            half SoftGlow(float2 uv, float innerPx, float outerPx)
            {
                half outer = NeighborMaxWithin(uv, outerPx);
                half inner = NeighborMaxWithin(uv, innerPx);
                return saturate(outer - max(inner, SampleA(uv)));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half spriteA = tex.a;

                half outlineEdge = 0;
                if (_OutlineWidth > 0.01)
                {
                    half around = NeighborMaxWithin(IN.uv, _OutlineWidth);
                    outlineEdge = saturate(around - spriteA);
                }

                half glow = 0;
                if (_GlowWidth > _OutlineWidth)
                    glow = SoftGlow(IN.uv, _OutlineWidth, _GlowWidth);

                half4 spriteCol = half4(tex.rgb, spriteA) * IN.color;
                half4 outlineCol = _OutlineColor;
                outlineCol.a *= outlineEdge;

                half4 glowCol = _GlowColor;
                glowCol.a *= glow;

                half4 col = glowCol;
                col.rgb = lerp(col.rgb, outlineCol.rgb, outlineCol.a);
                col.a = max(col.a, outlineCol.a);
                col.rgb = lerp(col.rgb, spriteCol.rgb, spriteA);
                col.a = max(col.a, spriteCol.a);

                clip(col.a - _AlphaClip);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
