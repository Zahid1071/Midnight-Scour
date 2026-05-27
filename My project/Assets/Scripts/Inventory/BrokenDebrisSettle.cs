using System.Collections;
using UnityEngine;

/// <summary>
/// After a short delay, stops simulating and removes debris for performance.
/// </summary>
[DisallowMultipleComponent]
public sealed class BrokenDebrisSettle : MonoBehaviour
{
    [Min(0f)]
    [SerializeField] private float disappearAfterMinSeconds = 2f;
    [Min(0f)]
    [SerializeField] private float disappearAfterMaxSeconds = 3f;

    public void Init(float minSeconds, float maxSeconds)
    {
        if (minSeconds >= 0f) disappearAfterMinSeconds = minSeconds;
        if (maxSeconds >= 0f) disappearAfterMaxSeconds = maxSeconds;
    }

    private void Start()
    {
        StartCoroutine(SettleRoutine());
    }

    IEnumerator SettleRoutine()
    {
        float min = Mathf.Min(disappearAfterMinSeconds, disappearAfterMaxSeconds);
        float max = Mathf.Max(disappearAfterMinSeconds, disappearAfterMaxSeconds);
        float wait = Random.Range(min, max);
        yield return new WaitForSeconds(wait);

        // Stop any remaining physics, then remove whole debris.
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
            Destroy(rb);

        Destroy(gameObject);
    }
}
