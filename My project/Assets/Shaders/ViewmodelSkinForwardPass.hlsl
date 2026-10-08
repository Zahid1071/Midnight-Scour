#ifndef VIEWMODEL_SKIN_FORWARD_PASS_INCLUDED
#define VIEWMODEL_SKIN_FORWARD_PASS_INCLUDED

// Forward pass for "Midnight Scour/Viewmodel Skin": URP Lit inputs, vertex stage and GI, with a
// skin-specific direct lighting model for first-person arms that sit centimetres from a light.

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

// LitInput.hlsl owns UnityPerMaterial, so these live outside it (the shader is not SRP-Batcher
// compatible; irrelevant for a single viewmodel).
float _NearRadius;
half _Wrap;
half4 _ScatterColor;
half _HighlightKnee;
half _HighlightMax;
half _HotDesaturate;
half4 _AmbientFloor;
half4 _ReflectFillTop;
half4 _ReflectFillBottom;
half _ReflectFillUnlit;
// Global, set by ViewmodelReflectionScale on the flame light: 1 = flame at base brightness, 0 = no flame.
half _ViewmodelReflectScale;

half3 SkinDirectLight(BRDFData brdfData, Light light, half3 normalWS, half3 viewDirWS, bool softenNear)
{
    // Punctual falloff is ~1/d^2 near the light; a/(1 + r^2 a) turns that into 1/(d^2 + r^2),
    // capping surfaces closer than ~r while leaving distant lighting unchanged.
    float atten = light.distanceAttenuation;
    if (softenNear)
        atten = atten / (1.0 + _NearRadius * _NearRadius * atten);
    atten *= light.shadowAttenuation;

    half NdotL = dot(normalWS, light.direction);
    half lit = saturate(NdotL);
    half wrapped = saturate((NdotL + _Wrap) / (1.0 + _Wrap));
    half3 diffuse = brdfData.diffuse * (wrapped + _ScatterColor.rgb * (wrapped - lit));

    half3 specular = 0;
#ifndef _SPECULARHIGHLIGHTS_OFF
    specular = brdfData.specular * DirectBRDFSpecular(brdfData, normalWS, light.direction, viewDirWS) * lit;
#endif

    return light.color * (half)atten * (diffuse + specular);
}

// Soft shoulder on the brightest channel: below the knee nothing changes, above it the colour is
// scaled down uniformly (hue kept, no lemon-yellow clipping) and eased toward white like real
// overexposure.
half3 CompressHighlights(half3 color)
{
    half peak = max(color.r, max(color.g, color.b));
    half over = max(peak - _HighlightKnee, 0.0);
    half range = max(_HighlightMax - _HighlightKnee, 0.001);
    half peakOut = min(peak, _HighlightKnee + over / (1.0 + over / range));
    half3 scaled = color * (peakOut / max(peak, 0.0001));
    half hot = saturate(over / range) * _HotDesaturate;
    return lerp(scaled, peakOut.xxx, hot);
}

void ViewmodelSkinFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out float4 outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);
    BRDFData noClearCoat = (BRDFData)0;

    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();

    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    // Dark scenes leave arms with ~zero probe light; the floor keeps the unlit side readable.
    inputData.bakedGI = max(inputData.bakedGI, _AmbientFloor.rgb);
    half3 color = GlobalIllumination(brdfData, noClearCoat, 0.0,
                                     inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                     inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);

    // Dark rooms leave metal nothing to reflect; a vertical gradient stands in for the flame above.
    half3 reflectWS = reflect(-inputData.viewDirectionWS, inputData.normalWS);
    half3 reflectFill = lerp(_ReflectFillBottom.rgb, _ReflectFillTop.rgb, saturate(reflectWS.y * 0.5 + 0.5))
                        * max(_ViewmodelReflectScale, _ReflectFillUnlit);
    half fresnelTerm = Pow4(1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS)));
    color += reflectFill * EnvironmentBRDFSpecular(brdfData, fresnelTerm) * aoFactor.indirectAmbientOcclusion;

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        color += SkinDirectLight(brdfData, mainLight, inputData.normalWS, inputData.viewDirectionWS, false);
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_FORWARD_PLUS
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            color += SkinDirectLight(brdfData, light, inputData.normalWS, inputData.viewDirectionWS, false);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            color += SkinDirectLight(brdfData, light, inputData.normalWS, inputData.viewDirectionWS, true);
        }
    LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    color += inputData.vertexLighting * brdfData.diffuse;
#endif

    color += surfaceData.emission;
    color = CompressHighlights(color);
    color = MixFog(color, inputData.fogCoord);

    outColor = half4(color, OutputAlpha(surfaceData.alpha, IsSurfaceTypeTransparent(_Surface)));

#ifdef _WRITE_RENDERING_LAYERS
    uint renderingLayers = GetMeshRenderingLayer();
    outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
#endif
}

#endif
