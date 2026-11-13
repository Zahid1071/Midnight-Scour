using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Puts a sound in the level through Steam Audio: HRTF (so front, back and height read, not just left and right),
/// occlusion and frequency-dependent transmission through walls and shut doors, air absorption, and real-time
/// reflections off the surrounding geometry, so a scream sounds like it is in a room rather than on the headphones.
/// </summary>
public static class SpatialAudio
{
    /// <summary>
    /// One-shot from a point; the object is destroyed when the clip ends. Volume above 1 boosts past the clip's level.
    /// </summary>
    public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume, float minDistance, float maxDistance,
        AudioMixerGroup group, Transform parent = null, float pitch = 1f)
    {
        if (clip == null)
            return null;
        AudioSource src = Create(clip.name, parent, position, group, minDistance, maxDistance);
        src.pitch = pitch;
        src.PlayOneShot(clip, volume);
        Object.Destroy(src.gameObject, clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch)) + 0.25f);
        return src;
    }

    /// <summary>
    /// A new 3D source at a point (or riding on parent). Built inactive and switched on once configured: Steam Audio
    /// reads the source's settings as it is enabled, so the first audio frame isn't played unattenuated.
    /// </summary>
    public static AudioSource Create(string name, Transform parent, Vector3 position, AudioMixerGroup group,
        float minDistance, float maxDistance, AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic, bool reflections = true)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null)
            go.transform.SetParent(parent, false);
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.outputAudioMixerGroup = group;
        Configure(src, minDistance, maxDistance, rolloff, true, reflections);
        go.SetActive(true);
        return src;
    }

    /// <summary>
    /// Settings shared by every Steam Audio source in the game. Call before the source's object is enabled. Pathing
    /// (sound reaching the player around corners and through open doors) uses the baked probe batch around the
    /// source's current position, so leave it off for sources that get moved between areas. Steam Audio only
    /// simulates reflections for a limited number of sources (Steam Audio Settings), so leave them off for sources
    /// that are mostly silent.
    /// </summary>
    public static void Configure(AudioSource src, float minDistance, float maxDistance, AudioRolloffMode rolloff, bool pathing,
        bool reflections = true)
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
        steam.airAbsorption = true;
        steam.occlusion = true;
        // Volumetric so a sound by a doorway fades as it goes behind the frame instead of cutting off. Kept small:
        // samples that dip into the floor count as occluded.
        steam.occlusionType = SteamAudio.OcclusionType.Volumetric;
        steam.occlusionRadius = 0.2f;
        steam.occlusionSamples = 16;
        steam.transmission = true;
        steam.transmissionType = SteamAudio.TransmissionType.FrequencyDependent;
        steam.maxTransmissionSurfaces = 2;
        steam.reflections = reflections;
        steam.reflectionsType = SteamAudio.ReflectionsType.Realtime;
        steam.reflectionsMixLevel = 1f;

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
