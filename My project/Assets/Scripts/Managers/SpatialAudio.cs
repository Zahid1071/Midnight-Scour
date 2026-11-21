using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Puts a sound in the level through Steam Audio: HRTF (so front, back and height read, not just left and right).
/// Interactive SFX (doors, UI-adjacent world clicks) stay clear; atmospheric one-shots keep occlusion / reflections.
/// </summary>
public static class SpatialAudio
{
    /// <summary>Local gameplay SFX — spatial, but no reflection wash / occlusion choke.</summary>
    public const bool InteractiveReflections = false;
    public const bool InteractiveOcclusion = false;
    public const bool InteractiveAirAbsorption = false;

    /// <summary>Horror / room beds — full Steam sim, reflections dialed under 1 so nearby sources stay readable.</summary>
    public const float AtmosphericReflectionsMix = 0.55f;

    /// <summary>
    /// One-shot from a point; the object is destroyed when the clip ends. Volume above 1 boosts past the clip's level.
    /// </summary>
    public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume, float minDistance, float maxDistance,
        AudioMixerGroup group, Transform parent = null, float pitch = 1f, bool atmospheric = true)
    {
        if (clip == null)
            return null;
        AudioSource src = Create(clip.name, parent, position, group, minDistance, maxDistance,
            AudioRolloffMode.Logarithmic, atmospheric);
        src.pitch = pitch;
        src.PlayOneShot(clip, volume);
        // Keep the Steam Audio source alive past the dry clip so reflections don't click off.
        Object.Destroy(src.gameObject, clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch)) + 0.85f);
        return src;
    }

    /// <summary>
    /// A new 3D source at a point (or riding on parent). Built inactive and switched on once configured: Steam Audio
    /// reads the source's settings as it is enabled, so the first audio frame isn't played unattenuated.
    /// </summary>
    public static AudioSource Create(string name, Transform parent, Vector3 position, AudioMixerGroup group,
        float minDistance, float maxDistance, AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic,
        bool atmospheric = true)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null)
            go.transform.SetParent(parent, false);
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.outputAudioMixerGroup = group;
        if (atmospheric)
            ConfigureAtmospheric(src, minDistance, maxDistance, rolloff, pathing: true);
        else
            ConfigureInteractive(src, minDistance, maxDistance, rolloff);
        go.SetActive(true);
        return src;
    }

    /// <summary>
    /// Door / interact pool sources: HRTF + distance only. No realtime reflections (they hum then hard-cut) and no
    /// occlusion/air absorption (they make nearby basement SFX sound choked).
    /// </summary>
    public static void ConfigureInteractive(AudioSource src, float minDistance, float maxDistance,
        AudioRolloffMode rolloff = AudioRolloffMode.Linear)
    {
        Configure(src, minDistance, maxDistance, rolloff, pathing: false,
            reflections: InteractiveReflections,
            occlusion: InteractiveOcclusion,
            airAbsorption: InteractiveAirAbsorption,
            reflectionsMix: 0f);
    }

    /// <summary>Screams, runaways, room stingers — full Steam sim with tempered reflection mix.</summary>
    public static void ConfigureAtmospheric(AudioSource src, float minDistance, float maxDistance,
        AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic, bool pathing = true)
    {
        Configure(src, minDistance, maxDistance, rolloff, pathing,
            reflections: true,
            occlusion: true,
            airAbsorption: true,
            reflectionsMix: AtmosphericReflectionsMix);
    }

    /// <summary>
    /// Settings shared by Steam Audio sources. Call before the source's object is enabled.
    /// </summary>
    public static void Configure(AudioSource src, float minDistance, float maxDistance, AudioRolloffMode rolloff,
        bool pathing, bool reflections = true, bool occlusion = true, bool airAbsorption = true,
        float reflectionsMix = 1f)
    {
        src.spatialBlend = 1f;
        src.spatialize = true;
        src.spread = 0f;
        src.dopplerLevel = 0f;
        src.rolloffMode = rolloff;
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;

#if STEAMAUDIO_ENABLED
        var steam = src.GetComponent<SteamAudio.SteamAudioSource>();
        if (steam == null)
            steam = src.gameObject.AddComponent<SteamAudio.SteamAudioSource>();
        steam.directBinaural = true;
        steam.interpolation = SteamAudio.HRTFInterpolation.Bilinear;
        steam.distanceAttenuation = true;
        steam.airAbsorption = airAbsorption;
        steam.occlusion = occlusion;
        steam.occlusionType = SteamAudio.OcclusionType.Volumetric;
        steam.occlusionRadius = 0.15f;
        steam.occlusionSamples = 12;
        steam.transmission = occlusion;
        steam.transmissionType = SteamAudio.TransmissionType.FrequencyDependent;
        steam.maxTransmissionSurfaces = 2;
        steam.reflections = reflections;
        steam.reflectionsType = SteamAudio.ReflectionsType.Realtime;
        steam.reflectionsMixLevel = reflections ? Mathf.Clamp01(reflectionsMix) : 0f;

        SteamAudio.SteamAudioProbeBatch batch = pathing ? ProbeBatchAt(src.transform.position) : null;
        steam.pathing = batch != null;
        steam.pathingProbeBatch = batch;
        steam.pathValidation = true;
        steam.findAlternatePaths = true;
        steam.applyHRTFToPathing = true;
        steam.pathingMixLevel = 1f;
#endif
    }

#if STEAMAUDIO_ENABLED
    static SteamAudio.SteamAudioProbeBatch ProbeBatchAt(Vector3 position)
    {
        foreach (var batch in Object.FindObjectsByType<SteamAudio.SteamAudioProbeBatch>(FindObjectsSortMode.None))
        {
            Vector3 local = batch.transform.InverseTransformPoint(position);
            if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f && Mathf.Abs(local.z) <= 0.5f)
                return batch;
        }
        return null;
    }
#endif
}
