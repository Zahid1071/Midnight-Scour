using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class POILookDirector : MonoBehaviour
{
    [Header("References (assign in inspector)")]
    [SerializeField] Transform butcher;
    [SerializeField] Transform poi;
    [SerializeField] BoxCollider poiBounds;

    [Header("POI Movement")]
    [SerializeField] float poiMoveIntervalSeconds = 3f;

    [Header("Look Chance")]
    [SerializeField, Range(0f, 1f)] float triggerChance = 0.30f;
    [SerializeField] float chanceCheckIntervalSeconds = 5f;
    [SerializeField] float holdSeconds = 2f;
    [SerializeField] float rampSeconds = 0.6f;

    Rig _lookRig;
    Coroutine _holdRoutine;

    void Awake()
    {
        if (poiBounds == null)
            poiBounds = GetComponent<BoxCollider>();

        if (butcher == null || poi == null || poiBounds == null) return;

        _lookRig = butcher.GetComponentInChildren<Rig>(true);

        EnsureConstraintsTargetPOI();

        if (_lookRig != null)
            _lookRig.weight = 0f;
    }

    void OnEnable()
    {
        StartCoroutine(POIMoveLoop());
        StartCoroutine(LookChanceLoop());
    }

    IEnumerator POIMoveLoop()
    {
        while (true)
        {
            if (butcher != null && poi != null && poiBounds != null)
                MovePOIInsideBounds();

            yield return new WaitForSeconds(poiMoveIntervalSeconds);
        }
    }

    IEnumerator LookChanceLoop()
    {
        while (true)
        {
            if (butcher != null)
            {
                if (_lookRig == null) _lookRig = butcher.GetComponentInChildren<Rig>(true);
                if (_lookRig != null && Random.value < triggerChance)
                    TriggerLookHold();
            }

            yield return new WaitForSeconds(chanceCheckIntervalSeconds);
        }
    }

    void MovePOIInsideBounds()
    {
        Transform bt = poiBounds.transform;
        Vector3 c = poiBounds.center;
        Vector3 half = poiBounds.size * 0.5f;

        Vector3 localRandom = new Vector3(
            Random.Range(c.x - half.x, c.x + half.x),
            Random.Range(c.y - half.y, c.y + half.y),
            Random.Range(c.z - half.z, c.z + half.z)
        );

        poi.position = bt.TransformPoint(localRandom);
    }

    void TriggerLookHold()
    {
        if (_holdRoutine != null)
            StopCoroutine(_holdRoutine);
        _holdRoutine = StartCoroutine(LookHoldRoutine());
    }

    IEnumerator LookHoldRoutine()
    {
        float ramp = Mathf.Max(0.01f, rampSeconds);
        float total = Mathf.Max(0.01f, holdSeconds);
        float hold = Mathf.Max(0f, total - (ramp * 2f));

        yield return RampRigWeight(_lookRig.weight, 1f, ramp);
        if (hold > 0f)
            yield return new WaitForSeconds(hold);
        yield return RampRigWeight(_lookRig.weight, 0f, ramp);

        _holdRoutine = null;
    }

    IEnumerator RampRigWeight(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float a = seconds <= 0f ? 1f : Mathf.Clamp01(t / seconds);
            _lookRig.weight = Mathf.Lerp(from, to, a);
            yield return null;
        }
        _lookRig.weight = to;
    }

    void EnsureConstraintsTargetPOI()
    {
        if (butcher == null || poi == null) return;

        MultiAimConstraint[] aims = butcher.GetComponentsInChildren<MultiAimConstraint>(true);
        for (int i = 0; i < aims.Length; i++)
        {
            MultiAimConstraint c = aims[i];
            var data = c.data;
            var sources = data.sourceObjects;
            if (sources.Count <= 0) continue;

            var first = sources[0];
            if (first.transform != null) continue;

            sources[0] = new WeightedTransform(poi, first.weight);
            data.sourceObjects = sources;
            c.data = data;
        }
    }
}

