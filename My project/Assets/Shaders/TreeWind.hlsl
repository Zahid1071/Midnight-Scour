#ifndef MIDNIGHT_SCOUR_TREE_WIND_INCLUDED
#define MIDNIGHT_SCOUR_TREE_WIND_INCLUDED

// Set globally by the TreeWind component. xy: wind direction on the ground plane, z: sway in metres at 10 m
// above the tree's pivot, w: speed.
float4 _MSTreeWind;
// x: gustiness 0..1, y: leaf flutter in metres.
float4 _MSTreeWindParams;
// Per material: 1 on leaf cards, 0 on bark.
float _WindFlutter;

float3 ApplyTreeWind(float3 positionOS)
{
    float strength = _MSTreeWind.z;
    if (strength <= 0.0)
        return positionOS;

    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float3 positionWS = TransformObjectToWorld(positionOS);
    float height = max(0.0, positionWS.y - pivotWS.y) * 0.1;

    // Each tree gets its own phase so the forest doesn't sway in lockstep.
    float phase = dot(pivotWS.xz, float2(0.173, 0.131));
    float t = _Time.y * _MSTreeWind.w;
    float sway = sin(t + phase) * 0.65 + sin(t * 0.43 + phase * 1.9) * 0.35;
    float gust = lerp(1.0, 0.5 + 0.5 * sin(t * 0.17 + phase * 0.6), _MSTreeWindParams.x);

    float2 wind = _MSTreeWind.xy;
    float2 across = float2(-wind.y, wind.x);
    float bend = strength * gust * height * height;
    positionWS.xz += (wind * (0.35 + 0.65 * sway) + across * 0.25 * sin(t * 0.71 + phase * 2.3)) * bend;
    positionWS.y -= 0.15 * bend * bend;

    float flutter = _WindFlutter * _MSTreeWindParams.y * gust;
    if (flutter > 0.0)
    {
        float leafPhase = dot(positionWS, float3(1.7, 2.3, 1.3));
        positionWS += float3(sin(t * 7.1 + leafPhase), sin(t * 5.3 + leafPhase * 1.3) * 0.5, cos(t * 6.3 + leafPhase)) * flutter;
    }

    return TransformWorldToObject(positionWS);
}

#endif
