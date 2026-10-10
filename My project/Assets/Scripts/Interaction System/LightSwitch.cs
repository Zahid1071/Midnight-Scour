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

    void Apply(bool lit)
    {
        foreach (Light l in _lights)
            if (l != null)
                l.enabled = lit;

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
