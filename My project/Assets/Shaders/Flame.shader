// Flame
//
// Procedural small fire (lighter, candle) drawn on a vertical strip mesh, no textures.
// Mode Flame: a teardrop billboard that turns around the world up axis (tilting partly toward the camera so it
// never goes edge-on), with travelling noise wobble, height flicker, a blue base and a hot core.
// Mode Glow: a soft camera-facing halo around the flame, purely additive.
// FlameVisual feeds the per-flame _Lean (xyz = world offset of the tip as a fraction of the height,
// w = stretch), _Seed and _Intensity through a MaterialPropertyBlock.

Shader "Midnight Scour/Flame"
{
    Properties
    {
        [Enum(Flame, 0, Glow, 1)] _Mode("Mode", Float) = 0
        _FlameSize("Size (x = width, y = height, metres)", Vector) = (0.014, 0.045, 0, 0)

        [Header(Colour)]
        [HDR] _CoreColor("Core", Color) = (4.0, 3.1, 1.9, 1)
        [HDR] _MidColor("Body", Color) = (3.2, 1.45, 0.4, 1)
        [HDR] _OuterColor("Edge", Color) = (1.6, 0.36, 0.07, 1)
        [HDR] _BaseColor("Base", Color) = (0.3, 0.5, 1.8, 1)
        _BaseAmount("Base Amount", Range(0, 1)) = 0.6
        _Opacity("Opacity", Range(0, 1)) = 0.6

        [Header(Shape)]
        _BulbHeight("Bulb Height", Range(0.1, 0.6)) = 0.3
        _TipShape("Tip Sharpness", Range(0.3, 3)) = 1.1
        _CoreSize("Core Size", Range(0.05, 1)) = 0.55
        _Softness("Edge Softness", Range(0.02, 1)) = 0.4

        [Header(Motion)]
        _Wobble("Wobble", Range(0, 1)) = 0.6
        _Sway("Idle Sway", Range(0, 1)) = 0.35
        _Tongues("Tip Tongues", Range(0, 1)) = 0.5
        _Turbulence("Inner Turbulence", Range(0, 1)) = 0.6
        _FlickerSpeed("Flicker Speed", Float) = 1.3
        _HeightFlicker("Height Flicker", Range(0, 0.6)) = 0.24
        _ViewTilt("Tilt Toward Camera", Range(0, 1)) = 0.5

        [Header(Glow)]
        [HDR] _GlowColor("Glow Colour", Color) = (0.9, 0.38, 0.1, 1)
        _GlowSize("Glow Size (metres)", Float) = 0.16
        _GlowCenter("Glow Centre (fraction of height)", Range(0, 1)) = 0.4
        _GlowFalloff("Glow Falloff", Range(1, 30)) = 8

        _SoftDistance("Soft Intersection (metres)", Float) = 0.015

        [HideInInspector] _Lean("Lean", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Seed("Seed", Float) = 0
        [HideInInspector] _Intensity("Intensity", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied: the body partly covers what is behind it, so it still reads against a bright wall.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  eyeDepth   : TEXCOORD2;
                float  flicker    : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float4 _FlameSize;
                half4 _CoreColor;
                half4 _MidColor;
                half4 _OuterColor;
                half4 _BaseColor;
                float _BaseAmount;
                float _Opacity;
                float _BulbHeight;
                float _TipShape;
                float _CoreSize;
                float _Softness;
                float _Wobble;
                float _Sway;
                float _Tongues;
                float _Turbulence;
                float _FlickerSpeed;
                float _HeightFlicker;
                float _ViewTilt;
                half4 _GlowColor;
                float _GlowSize;
                float _GlowCenter;
                float _GlowFalloff;
                float _SoftDistance;
                float4 _Lean;
                float _Seed;
                float _Intensity;
            CBUFFER_END

            float Hash11(float n) { return frac(sin(n) * 43758.5453); }

            float Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                return lerp(Hash11(i), Hash11(i + 1.0), f * f * (3.0 - 2.0 * f));
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 0-1: a slow swell with a faster shimmer on top.
            float Flicker(float t) { return Noise1(t * 2.3) * 0.65 + Noise1(t * 7.1 + 13.0) * 0.35; }

            Varyings Vert(Attributes IN)
            {
                float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
                float4x4 m = GetObjectToWorldMatrix();
                float scale = length(float3(m._m01, m._m11, m._m21));
                float t = _Time.y * _FlickerSpeed + _Seed;
                float flick = Flicker(t);

                float3 toCam = normalize(_WorldSpaceCameraPos - pivot);
                float3 worldUp = float3(0, 1, 0);
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp = UNITY_MATRIX_V[1].xyz;
                float height = _FlameSize.y * scale;
                float y = IN.positionOS.y;

                float3 positionWS;
                if (_Mode < 0.5)
                {
                    float3 right = cross(toCam, worldUp);
                    float rightLength = length(right);
                    right = rightLength > 1e-3 ? right / rightLength : camRight;
                    float3 up = normalize(lerp(worldUp, cross(right, toCam), _ViewTilt));

                    float stretch = max(1.0 + _Lean.w, 0.3);
                    float h = height * (1.0 + (flick - 0.5) * 2.0 * _HeightFlicker) * stretch;
                    // The strip is twice the flame's width so the wobble has room; it breathes against the height.
                    float breathe = 1.0 + (Noise1(t * 3.3 + 5.0) - 0.5) * 0.3 * _Sway;
                    float w = _FlameSize.x * 2.0 * scale * breathe / sqrt(stretch);
                    // Draughts: the whole flame drifts side to side even when held still.
                    float sway = ((Noise1(t * 1.8 + 3.0) - 0.5) * 1.2 + (Noise1(t * 4.6 + 7.0) - 0.5) * 0.8) * _Sway;
                    float3 bend = _Lean.xyz + right * sway;
                    positionWS = pivot + right * IN.positionOS.x * w + up * y * h + bend * (y * y) * h;
                }
                else
                {
                    float size = _GlowSize * scale * (0.9 + 0.2 * flick);
                    float3 centre = pivot + worldUp * height * _GlowCenter + _Lean.xyz * 0.3 * height;
                    positionWS = centre + camRight * IN.positionOS.x * size + camUp * (y - 0.5) * size;
                }

                Varyings OUT;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.eyeDepth = -TransformWorldToView(positionWS).z;
                OUT.flicker = flick;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(IN.screenPos.xy / IN.screenPos.w), _ZBufferParams);
                float soft = saturate((sceneDepth - IN.eyeDepth) / max(_SoftDistance, 1e-4));
                float t = _Time.y * _FlickerSpeed + _Seed;

                if (_Mode > 0.5)
                {
                    float2 p = IN.uv - 0.5;
                    float r2 = dot(p, p) * 4.0;
                    float glow = exp(-r2 * _GlowFalloff) * saturate(1.0 - r2);
                    glow *= (0.75 + 0.5 * IN.flicker) * _Intensity * soft;
                    return half4(_GlowColor.rgb * glow, 0);
                }

                // x: -1..1 across the strip (flame half-width 0.5 at the bulb), y: 0 base .. 1 tip.
                float x = (IN.uv.x - 0.5) * 2.0;
                // Interpolation can dip just below 0 at the base; sqrt there gives NaN, which bloom smears across the screen.
                float y = saturate(IN.uv.y);

                // Waves travelling up the flame, swinging the upper part more than the base.
                float wave = (Noise2(float2(y * 2.5 - t * 2.6, t * 0.7)) - 0.5) * 2.0;
                float ripple = (Noise2(float2(y * 6.0 - t * 5.3, 3.7 + t * 0.5)) - 0.5) * 2.0;
                float xd = x - (wave * 0.45 + ripple * 0.2) * _Wobble * y * sqrt(y) * 1.3;

                // Teardrop: round bottom up to the bulb, then a convex taper to the tip.
                float c = clamp(_BulbHeight, 0.05, 0.95);
                float below = saturate((c - y) / c);
                float above = saturate((y - c) / (1.0 - c));
                float profile = y < c ? sqrt(saturate(1.0 - below * below)) : pow(max(cos(above * 1.5707963), 0.0), _TipShape);
                profile *= 1.0 + (Noise2(float2(y * 8.0 - t * 6.0, x * 2.0 + 11.0)) - 0.5) * 0.4 * _Wobble;
                // Tongues licking upward break up the tip, sometimes pinching a piece off.
                float tongue = Noise2(float2(xd * 3.0 + 20.0, y * 4.0 - t * 7.0));
                profile *= lerp(1.0, smoothstep(0.15, 0.65, tongue) * 1.35, _Tongues * smoothstep(0.4, 0.95, y));
                float d = abs(xd) / max(0.5 * profile, 1e-4);

                float mask = 1.0 - smoothstep(1.0 - _Softness, 1.0, d);
                mask *= smoothstep(0.0, 0.06, y) * (1.0 - smoothstep(0.85, 1.0, y));

                // Brighter and dimmer streaks rising through the body so it doesn't read as a flat shape.
                float turbulence = Noise2(float2(xd * 2.5 + 5.0, y * 3.5 - t * 4.5)) * 0.6
                                 + Noise2(float2(xd * 6.0 - 3.0, y * 8.0 - t * 8.5)) * 0.4;
                float turbulenceAmount = _Turbulence * lerp(0.4, 1.0, y);
                mask *= lerp(1.0, lerp(0.6, 1.1, turbulence), turbulenceAmount);

                float core = (1.0 - smoothstep(0.0, 1.0, d / max(_CoreSize, 0.05))) * smoothstep(0.03, 0.2, y) * (1.0 - smoothstep(0.35, 0.8, y));
                float baseMask = (1.0 - smoothstep(0.0, 0.28, y)) * _BaseAmount * lerp(0.5, 1.0, saturate(d));

                float3 col = lerp(_OuterColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.7, saturate(1.0 - d)));
                col = lerp(col, _CoreColor.rgb, core);
                col = lerp(col, _BaseColor.rgb, baseMask * (1.0 - core * 0.5));
                col = lerp(col, _OuterColor.rgb, smoothstep(0.55, 1.0, y) * 0.6);

                float a = mask * soft;
                float brightness = (0.85 + 0.3 * IN.flicker) * _Intensity * lerp(1.0, lerp(0.75, 1.2, turbulence), turbulenceAmount);
                return half4(col * a * brightness, a * _Opacity * (1.0 - baseMask * 0.7));
            }
            ENDHLSL
        }
    }
}
