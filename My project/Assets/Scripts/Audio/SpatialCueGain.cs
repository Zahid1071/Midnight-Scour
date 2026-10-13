using UnityEngine;

/// <summary>
/// Volume trim on an AudioSource's output, set by <see cref="SpatialAudioCues"/>. Works under PlayOneShot and
/// leaves AudioSource.volume to whoever owns it (fades, one-shot volumes).
/// </summary>
[DisallowMultipleComponent]
public class SpatialCueGain : MonoBehaviour
{
    volatile float _target = 1f;
    float _current = 1f;

    public float Gain
    {
        get => _target;
        set => _target = Mathf.Clamp01(value);
    }

    // Audio thread: ramps across the buffer so changes don't click.
    void OnAudioFilterRead(float[] data, int channels)
    {
        float target = _target;
        float start = _current;
        if (start >= 0.9999f && target >= 0.9999f)
            return;

        int frames = data.Length / channels;
        for (int i = 0; i < frames; i++)
        {
            float g = start + (target - start) * (i + 1) / frames;
            int o = i * channels;
            for (int c = 0; c < channels; c++)
                data[o + c] *= g;
        }
        _current = target;
    }
}
