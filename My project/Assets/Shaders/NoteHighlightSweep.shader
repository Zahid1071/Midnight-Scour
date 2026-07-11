// NoteHighlightSweep
//
// A subtle "attention glint" overlay for readable notes/letters.
// Instead of drawing an outline, a soft diagonal band of light sweeps
// across the object every few seconds, then rests. Meant to be used as
// an EXTRA material slot layered on top of a note's normal Lit material
// on the exact same mesh (same submesh) - it does not replace lighting,
// it just adds a thin glint on top additively.
//
// Setup:
//   1. Select the note prefab's MeshRenderer.
//   2. Increase the Materials array size by 1.
//   3. Drag "Mat_NoteHighlightSweep" into the new slot.
// The mesh will render twice (once lit normally, once with this glint),
// which is a standard, cheap way to layer an overlay without duplicating geometry.

Shader "Custom/NoteHighlightSweep"
{
    Properties
    {
        _SweepColor("Sweep Color", Color) = (1, 1, 0.92, 1)
        _SweepIntensity("Sweep Intensity", Range(0, 2)) = 0.45
        _SweepWidth("Sweep Width", Range(0.01, 0.5)) = 0.10
        _SweepPeriod("Sweep Period (seconds)", Float) = 6.0
        _SweepDuration("Sweep Active Duration (seconds)", Float) = 1.1
        _SweepDirection("Sweep Direction (UV space)", Vector) = (1, 1, 0, 0)
        _PhaseOffset("Phase Offset (0-1, vary per object)", Range(0, 1)) = 0.0
        _FresnelInfluence("Fresnel Edge Influence", Range(0, 1)) = 0.25
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "NoteSweepOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _SweepColor;
                float _SweepIntensity;
                float _SweepWidth;
                float _SweepPeriod;
                float _SweepDuration;
                float4 _SweepDirection;
                float _PhaseOffset;
                float _FresnelInfluence;
                float _FresnelPower;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vertexInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = vertexInputs.positionCS;
                OUT.positionWS = vertexInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Diagonal coordinate across the note's UV space ("crossway" line).
                float2 dir = normalize(_SweepDirection.xy + 1e-5);
                float diag = dot(IN.uv - 0.5, dir) + 0.5;

                // A per-object phase offset (set via _PhaseOffset, or vary via material
                // instancing/MaterialPropertyBlock) keeps multiple notes from all
                // flashing in perfect unison.
                float period = max(_SweepPeriod, 0.01);
                float activeFrac = saturate(_SweepDuration / period);

                float phase = frac((_Time.y / period) + _PhaseOffset);

                // Sweep travels slightly beyond the [0,1] UV range on both ends so it
                // fades on/off the edge of the mesh instead of popping.
                float travel = lerp(-0.35, 1.35, saturate(phase / max(activeFrac, 0.0001)));

                float dist = abs(diag - travel);
                float band = smoothstep(_SweepWidth, 0.0, dist);

                // Outside the active window there is no sweep at all - this is the
                // "rests, then glints every now and then" pacing.
                band *= step(phase, activeFrac);

                // Small fresnel-based sheen so the glint feels more like light catching
                // paper at an angle than a flat decal.
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float fresnel = pow(1.0 - saturate(dot(normalize(IN.normalWS), viewDirWS)), _FresnelPower);
                float edge = lerp(1.0, fresnel, _FresnelInfluence);

                half3 col = _SweepColor.rgb * band * edge * _SweepIntensity;
                return half4(col, band);
            }
            ENDHLSL
        }
    }
}
