using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class POILookDirector : MonoBehaviour
{
    [Header("References (assign in inspector)")]
    [SerializeField] Rig lookRig;
    [SerializeField] Transform poi;
    [SerializeField] Transform playerPoi;
    [SerializeField] BoxCollider poiBounds;

    [Header("Look Chance")]
    [SerializeField, Range(0f, 1f)] float lookAtPlayerChance = 0.08f;
    [SerializeField] float holdSeconds = 2f;
    [SerializeField] float rampSeconds = 0.6f;

    Rig _lookRig;
    MultiAimConstraint[] _aimConstraints;
    Coroutine _holdRoutine;
    bool _lockedToPlayer;

    public bool IsLockedToPlayer => _lockedToPlayer;

    void Awake()
    {
        if (poiBounds == null)
            poiBounds = GetComponent<BoxCollider>();

        _lookRig = lookRig;
        if (_aimConstraints == null && _lookRig != null)
            _aimConstraints = _lookRig.GetComponentsInChildren<MultiAimConstraint>(true);

        if (poi != null)
            SetConstraintTargets(poi);

        if (_lookRig != null)
            _lookRig.weight = 0f;
    }

    void StopHold()
    {
        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }
    }

    void EnsureRig()
    {
        if (_lookRig == null)
            _lookRig = lookRig;
        if (_aimConstraints == null && _lookRig != null)
            _aimConstraints = _lookRig.GetComponentsInChildren<MultiAimConstraint>(true);
        if (poi != null)
            SetConstraintTargets(poi);
    }

    public void SetLookWeight(float weight)
    {
        EnsureRig();
        if (_lookRig == null) return;
        _lookRig.weight = Mathf.Clamp01(weight);
    }

    public void SetPOIPosition(Vector3 worldPosition)
    {
        if (poi == null) return;
        poi.position = worldPosition;
    }

    public void SnapPOITo(Transform target)
    {
        if (target == null) return;
        SetPOIPosition(target.position);
    }

    public void SnapPOIToPlayer()
    {
        if (playerPoi == null) return;
        SetPOIPosition(playerPoi.position);
    }

    /// <summary>
    /// Keeps the look rig pinned on the player until <see cref="UnlockLookFromPlayer"/> is called.
    /// </summary>
    public void LockLookToPlayer()
    {
        _lockedToPlayer = true;
        StopHold();
        EnsureRig();
        SetLookWeight(1f);
        SnapPOIToPlayer();
    }

    public void UnlockLookFromPlayer()
    {
        _lockedToPlayer = false;
    }

    /// <summary>Call while locked so the POI tracks a moving player.</summary>
    public void TickLockedPlayerLook()
    {
        if (!_lockedToPlayer)
            return;

        SetLookWeight(1f);
        SnapPOIToPlayer();
    }

    public void SnapPOIToRandomInBounds()
    {
        if (poi == null || poiBounds == null) return;

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

    public void SnapPOIToRandomOrPlayer()
    {
        if (playerPoi != null && Random.value < lookAtPlayerChance)
        {
            SnapPOIToPlayer();
            return;
        }

        SnapPOIToRandomInBounds();
    }

    public void TriggerLookHold()
    {
        EnsureRig();
        if (_lookRig == null) return;

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

    public void CancelHoldAndClearLook()
    {
        if (_lockedToPlayer)
            return;

        StopHold();
        if (_lookRig != null)
            _lookRig.weight = 0f;
    }
}
