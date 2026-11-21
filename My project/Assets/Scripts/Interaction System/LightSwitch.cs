using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wall switch: pressing E toggles every light under the assigned lamps and swaps their bulb material between
/// off and on. Switching on stutters for a moment before the bulbs settle.
/// </summary>
[DisallowMultipleComponent]
public class LightSwitch : MonoBehaviour, IInteractable
{
    [SerializeField] GameObject[] lamps;
    [SerializeField] Material offMaterial;
    [SerializeField] Material onMaterial;
    [SerializeField] bool onAtStart;

    [Tooltip("Flipped 180 degrees around its facing (local Z) axis on each toggle, so the rocker shows the other half pressed in.")]
    [SerializeField] Transform rocker;

    [Header("Switch-on flicker")]
    [Tooltip("Alternating on/off durations in seconds, starting with on, played before the lights stay on. Empty = instant.")]
    [SerializeField] float[] flickerPattern = { 0.06f, 0.12f, 0.05f, 0.3f, 0.08f, 0.07f };

    [Header("Audio")]
    [SerializeField] AudioClip switchSfx;
    [SerializeField, Range(0f, 1f)] float switchVolume = 0.8f;

    readonly List<Light> _lights = new();
    readonly List<Renderer> _bulbRenderers = new();
    Coroutine _flicker;

    public bool IsOn { get; private set; }

    void Awake()
    {
        foreach (GameObject lamp in lamps)
        {
            if (lamp == null)
                continue;
            _lights.AddRange(lamp.GetComponentsInChildren<Light>(true));
            foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true))
                if (UsesBulbMaterial(r))
                    _bulbRenderers.Add(r);
        }

        IsOn = onAtStart;
        Apply(IsOn);
    }

    public bool Interact()
    {
        IsOn = !IsOn;
        if (rocker != null)
            rocker.Rotate(0f, 0f, 180f, Space.Self);
        if (switchSfx != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(switchSfx, transform.position, switchVolume);

        if (_flicker != null)
        {
            StopCoroutine(_flicker);
            _flicker = null;
        }

        if (IsOn && flickerPattern != null && flickerPattern.Length > 0)
            _flicker = StartCoroutine(FlickerOn());
        else
            Apply(IsOn);
        return true;
    }

    /// <summary>
    /// Temporary on/off for scare flickers without flipping the rocker or changing <see cref="IsOn"/>.
    /// Finish with <see cref="SetOnInstant"/> / <see cref="StopFlicker"/> so logical state matches.
    /// </summary>
    public void PulseLit(bool lit)
    {
        if (_flicker != null)
        {
            StopCoroutine(_flicker);
            _flicker = null;
        }
        // Scare pulses only toggle emitters — material swaps hitch when many lamps flash.
        SetLightsEnabled(lit);
    }

    /// <summary>Sets the lights with no sound or flicker (checkpoint load).</summary>
    public void SetOnInstant(bool on)
    {
        if (_flicker != null)
        {
            StopCoroutine(_flicker);
            _flicker = null;
        }
        if (on != IsOn && rocker != null)
            rocker.Rotate(0f, 0f, 180f, Space.Self);
        IsOn = on;
        Apply(on);
    }

    /// <summary>Brief on/off stutter while already on (return scare). Does nothing if currently off.</summary>
    public void PlayFlicker()
    {
        if (!IsOn)
            return;
        if (_flicker != null)
            StopCoroutine(_flicker);
        _flicker = StartCoroutine(FlickerPulse());
    }

    /// <summary>Turns the lights on with the switch-on flicker pattern (no rocker flip if already on).</summary>
    public void TurnOnWithFlicker()
    {
        if (IsOn)
        {
            PlayFlicker();
            return;
        }
        if (rocker != null)
            rocker.Rotate(0f, 0f, 180f, Space.Self);
        IsOn = true;
        if (_flicker != null)
            StopCoroutine(_flicker);
        if (flickerPattern != null && flickerPattern.Length > 0)
            _flicker = StartCoroutine(FlickerOn());
        else
            Apply(true);
    }

    IEnumerator FlickerOn()
    {
        bool lit = true;
        foreach (float duration in flickerPattern)
        {
            Apply(lit);
            yield return new WaitForSeconds(duration);
            lit = !lit;
        }
        Apply(true);
        _flicker = null;
    }

    IEnumerator FlickerPulse()
    {
        float[] pulse = flickerPattern != null && flickerPattern.Length > 0
            ? flickerPattern
            : new[] { 0.05f, 0.1f, 0.04f, 0.18f, 0.06f };
        bool lit = false;
        foreach (float duration in pulse)
        {
            Apply(lit);
            yield return new WaitForSeconds(duration);
            lit = !lit;
        }
        Apply(true);
        _flicker = null;
    }

    /// <summary>Keeps stuttering on/off until <see cref="StopFlicker"/> (chase panic lights).</summary>
    public void StartConstantFlicker(float minOn = 0.04f, float maxOn = 0.14f, float minOff = 0.03f, float maxOff = 0.11f)
    {
        if (_flicker != null)
            StopCoroutine(_flicker);
        if (!IsOn)
        {
            if (rocker != null)
                rocker.Rotate(0f, 0f, 180f, Space.Self);
            IsOn = true;
        }
        _flicker = StartCoroutine(ConstantFlicker(minOn, maxOn, minOff, maxOff));
    }

    public void StopFlicker(bool leaveOn = true)
    {
        if (_flicker != null)
        {
            StopCoroutine(_flicker);
            _flicker = null;
        }
        Apply(leaveOn && IsOn);
    }

    IEnumerator ConstantFlicker(float minOn, float maxOn, float minOff, float maxOff)
    {
        while (true)
        {
            // Lights only — skip bulb material swaps so chase flicker doesn't allocate every tick.
            SetLightsEnabled(true);
            yield return new WaitForSeconds(Random.Range(minOn, maxOn));
            SetLightsEnabled(false);
            yield return new WaitForSeconds(Random.Range(minOff, maxOff));
        }
    }

    void SetLightsEnabled(bool lit)
    {
        for (int i = 0; i < _lights.Count; i++)
            if (_lights[i] != null)
                _lights[i].enabled = lit;
    }

    void Apply(bool lit)
    {
        SetLightsEnabled(lit);

        Material target = lit ? onMaterial : offMaterial;
        if (target == null)
            return;
        foreach (Renderer r in _bulbRenderers)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == offMaterial || mats[i] == onMaterial)
                    mats[i] = target;
            r.sharedMaterials = mats;
        }
    }

    bool UsesBulbMaterial(Renderer r)
    {
        foreach (Material m in r.sharedMaterials)
            if (m != null && (m == offMaterial || m == onMaterial))
                return true;
        return false;
    }
}
