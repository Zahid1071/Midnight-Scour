using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Footstep clips per floor surface, shared by the player and NPCs.</summary>
[CreateAssetMenu(menuName = "Audio/Footstep Bank", fileName = "Footstep Bank")]
public class FootstepBank : ScriptableObject
{
    [Serializable]
    public class Surface
    {
        public FootstepSurfaceType type;
        public AudioClip[] walk;
        [Tooltip("Empty = walk clips.")]
        public AudioClip[] run;
        [Range(0f, 2f)] public float volume = 1f;
    }

    [Tooltip("Used when the floor has no entry of its own.")]
    public FootstepSurfaceType fallback = FootstepSurfaceType.Wood;
    public Surface[] surfaces;

    // Fraction of a clip's peak that counts as the footfall, and how much lead-in to keep before it.
    const float OnsetFraction = 0.1f;
    const float PreRollSeconds = 0.004f;

    static readonly Dictionary<AudioClip, AudioClip> s_tight = new Dictionary<AudioClip, AudioClip>();

    readonly Dictionary<AudioClip[], int> _lastIndex = new Dictionary<AudioClip[], int>();

    public Surface Get(FootstepSurfaceType type)
    {
        Surface fallbackEntry = null;
        if (surfaces == null)
            return null;
        foreach (Surface s in surfaces)
        {
            if (s == null)
                continue;
            if (s.type == type && HasClips(s.walk))
                return s;
            if (s.type == fallback && HasClips(s.walk))
                fallbackEntry = s;
        }
        return fallbackEntry;
    }

    /// <summary>Random clip for the surface, never the same one twice in a row.</summary>
    public AudioClip Pick(Surface surface, bool running)
    {
        if (surface == null)
            return null;
        AudioClip[] pool = running && HasClips(surface.run) ? surface.run : surface.walk;
        if (!HasClips(pool))
            return null;
        if (pool.Length == 1)
            return pool[0];

        int index;
        if (_lastIndex.TryGetValue(pool, out int last))
        {
            index = UnityEngine.Random.Range(0, pool.Length - 1);
            if (index >= last)
                index++;
        }
        else
        {
            index = UnityEngine.Random.Range(0, pool.Length);
        }
        _lastIndex[pool] = index;
        return Tight(pool[index]);
    }

    /// <summary>
    /// <paramref name="clip"/> downmixed to mono and starting right at its footfall, so it sounds the instant it is
    /// played instead of after the recording's own silent lead-in. Clips that can't be read come back unchanged.
    /// </summary>
    public static AudioClip Tight(AudioClip clip)
    {
        if (clip == null)
            return null;
        // Destroyed copies (left over from an earlier play session) are rebuilt.
        if (s_tight.TryGetValue(clip, out AudioClip tight) && tight != null)
            return tight;
        if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            s_tight[clip] = clip;
            return clip;
        }

        int channels = clip.channels;
        int frames = clip.samples;
        var data = new float[frames * channels];
        // Not loaded yet: use it as-is this time and try again next pick.
        if (!clip.GetData(data, 0))
            return clip;

        float peak = 0f;
        for (int i = 0; i < data.Length; i++)
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));

        int onset = 0;
        float threshold = peak * OnsetFraction;
        for (int i = 0; i < data.Length; i++)
        {
            if (Mathf.Abs(data[i]) >= threshold)
            {
                onset = i / channels;
                break;
            }
        }

        int start = Mathf.Max(0, onset - Mathf.RoundToInt(clip.frequency * PreRollSeconds));
        int length = frames - start;
        if (peak <= 0f || length <= 0)
        {
            s_tight[clip] = clip;
            return clip;
        }

        var mono = new float[length];
        for (int f = 0; f < length; f++)
        {
            float sum = 0f;
            int offset = (start + f) * channels;
            for (int c = 0; c < channels; c++)
                sum += data[offset + c];
            mono[f] = sum / channels;
        }

        tight = AudioClip.Create(clip.name, length, 1, clip.frequency, false);
        tight.SetData(mono, 0);
        s_tight[clip] = tight;
        return tight;
    }

    static bool HasClips(AudioClip[] clips) => clips != null && clips.Length > 0;
}
