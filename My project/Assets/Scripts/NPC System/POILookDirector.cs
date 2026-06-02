using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class POILookDirector : MonoBehaviour
{
    [Header("References (assign in inspector)")]
    [SerializeField] Transform npc;
    [SerializeField] Transform poi;
    [SerializeField] Transform playerPoi;
    [SerializeField] BoxCollider poiBounds;

    [Header("Look Chance")]
    [SerializeField, Range(0f, 1f)] float triggerChance = 0.30f;
    [SerializeField, Range(0f, 1f)] float lookAtPlayerChance = 0.08f;
    [SerializeField] float chanceCheckIntervalSeconds = 5f;
    [SerializeField] float holdSeconds = 2f;
    [SerializeField] float rampSeconds = 0.6f;

    NPCController _npc;
    Rig _lookRig;
    MultiAimConstraint[] _aimConstraints;
    Coroutine _holdRoutine;
    bool _chaseLookActive;
    Transform _overrideLookTarget;
    float _overrideWeight;

    void Awake()
    {
        if (poiBounds == null)
            poiBounds = GetComponent<BoxCollider>();

        if (npc != null)
        {
            _npc = npc.GetComponent<NPCController>();
            _lookRig = npc.GetComponentInChildren<Rig>(true);
            _aimConstraints = npc.GetComponentsInChildren<MultiAimConstraint>(true);
        }

        if (poi != null)
            SetConstraintTargets(poi);

        if (_lookRig != null)
            _lookRig.weight = 0f;
    }

    void OnEnable()
    {
        StartCoroutine(LookLoop());
    }

    IEnumerator LookLoop()
    {
        while (true)
        {
            if (_lookRig == null && npc != null)
                _lookRig = npc.GetComponentInChildren<Rig>(true);

            if (_overrideLookTarget != null && _lookRig != null && poi != null)
            {
                StopAmbientHold();
                _lookRig.weight = _overrideWeight;
                poi.position = _overrideLookTarget.position;
                yield return null;
                continue;
            }

            if (IsChaseLookState())
            {
                if (!_chaseLookActive)
                    EnterChaseLook();

                if (_lookRig != null)
                    _lookRig.weight = 1f;
                if (poi != null && playerPoi != null)
                    poi.position = playerPoi.position;

                yield return null;
                continue;
            }

            if (_chaseLookActive)
                ExitChaseLook();

            if (npc != null && poi != null && poiBounds != null && _lookRig != null)
            {
                MovePOIInsideBounds();
                yield return null;

                if (Random.value < triggerChance)
                    TriggerLookHold();
            }

            yield return new WaitForSeconds(chanceCheckIntervalSeconds);
        }
    }

    bool IsChaseLookState()
    {
        if (_npc == null || playerPoi == null) return false;
        return _npc.currentState == NPCController.AIState.Chase
            || _npc.currentState == NPCController.AIState.Capture;
    }

    void EnterChaseLook()
    {
        _chaseLookActive = true;
        StopAmbientHold();
        if (poi != null)
            SetConstraintTargets(poi);
    }

    void ExitChaseLook()
    {
        _chaseLookActive = false;
        if (poi != null)
            SetConstraintTargets(poi);
        if (_lookRig != null)
            _lookRig.weight = 0f;
    }

    void StopAmbientHold()
    {
        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }
    }

    void MovePOIInsideBounds()
    {
        if (playerPoi != null && Random.value < lookAtPlayerChance)
        {
            poi.position = playerPoi.position;
            return;
        }

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

    void SetConstraintTargets(Transform target)
    {
        if (target == null || _aimConstraints == null) return;

        for (int i = 0; i < _aimConstraints.Length; i++)
        {
            MultiAimConstraint c = _aimConstraints[i];
            var data = c.data;
            var sources = data.sourceObjects;
            if (sources.Count <= 0) continue;

            var first = sources[0];
            sources[0] = new WeightedTransform(target, first.weight);
            data.sourceObjects = sources;
            c.data = data;
        }
    }

    public void OverrideLookAt(Transform target, float weight)
    {
        _overrideLookTarget = target;
        _overrideWeight = Mathf.Clamp01(weight);
        if (poi != null)
            SetConstraintTargets(poi);
    }

    public void ClearOverrideLook()
    {
        _overrideLookTarget = null;
        if (_lookRig != null)
            _lookRig.weight = 0f;
    }
}
