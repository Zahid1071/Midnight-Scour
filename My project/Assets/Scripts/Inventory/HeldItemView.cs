using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Root of a first-person item prefab (arms + item). HeldItemController shows it while the
/// item is selected and plays its draw state each time it is equipped.
/// </summary>
[DisallowMultipleComponent]
public sealed class HeldItemView : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [Tooltip("Animator state played from the start every time the item is equipped.")]
    [SerializeField] private string drawState = "Draw";

    [SerializeField] private bool castShadows;

    public Animator Animator => animator;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!castShadows)
                r.shadowCastingMode = ShadowCastingMode.Off;
            // Animated arms sit right at the near plane; stale bounds would cull them.
            if (r is SkinnedMeshRenderer smr)
                smr.updateWhenOffscreen = true;
        }
    }

    public void Draw()
    {
        gameObject.SetActive(true);
        if (animator == null || string.IsNullOrEmpty(drawState))
            return;

        animator.Rebind();
        animator.Play(drawState, 0, 0f);
        animator.Update(0f);
    }

    public void Holster()
    {
        gameObject.SetActive(false);
    }
}
