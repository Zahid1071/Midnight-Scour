using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Root of a first-person item view (arms + item), placed in the scene under the HeldItemController.
/// The controller shows it while its item is selected, plays its draw state each time it is equipped
/// and its put-down state before switching to another item.
/// </summary>
[DisallowMultipleComponent]
public sealed class HeldItemView : MonoBehaviour
{
    [Tooltip("Inventory item this view is shown for.")]
    [SerializeField] private InventoryItem item;

    [SerializeField] private Animator animator;

    [Tooltip("Animator state played from the start every time the item is equipped.")]
    [SerializeField] private string drawState = "Draw";

    [Tooltip("Animator state played when the item is put away, normally the draw clip reversed. The view hides " +
             "when it ends. Empty or missing = hide instantly.")]
    [SerializeField] private string putDownState = "PutDown";

    [Min(0f)]
    [SerializeField] private float putDownBlend = 0.1f;

    [Tooltip("Optional layer + state played alongside the put-down (e.g. a lighter lid closing on a masked layer " +
             "while the arm lowers). Only used while PutDownOverlayStartTime >= 0. The layer fades in over the " +
             "put-down blend and is reset to weight 0 on the next draw.")]
    [SerializeField] private string putDownOverlayLayer = "";
    [SerializeField] private string putDownOverlayState = "";

    [Tooltip("Safety limit; the view hides after this long even if the put-down has not finished.")]
    [Min(0.1f)]
    [SerializeField] private float putDownTimeout = 3f;

    [SerializeField] private bool castShadows;

    [Tooltip("Rendering layers for every renderer of this view. Lights only affect it if they share a layer: " +
             "Default for the scene's lights, Viewmodel for arm-only lights. Camera-mounted lights that would " +
             "blow out the arms go on the Not Viewmodel layer, which the Viewmodel Skin shader ignores.")]
    [SerializeField] private uint renderingLayers = (1u << 0) | (1u << 1);

    private readonly Dictionary<string, Transform> _sockets = new();
    private float _putDownTimer;
    private bool _overlayPlaying;

    public InventoryItem Item => item;

    public Animator Animator => animator;

    public bool IsDrawing =>
        animator != null && animator.isActiveAndEnabled && animator.GetCurrentAnimatorStateInfo(0).IsName(drawState);

    public bool IsHolstering { get; private set; }

    /// <summary>Raised at the end of <see cref="Draw"/>, after the draw state started, so item scripts can layer on top of it.</summary>
    public event System.Action Drawn;

    /// <summary>
    /// Draw clip time (0-1) the put-down picks up from, reversing back to 0. Item scripts move it, e.g. a
    /// closed lighter skips the lid-closing part.
    /// </summary>
    public float PutDownStartTime { get; set; } = 1f;

    /// <summary>
    /// Draw clip time (0-1) the put-down overlay starts from, reversing back to 0. Negative = no overlay.
    /// </summary>
    public float PutDownOverlayStartTime { get; set; } = -1f;

    /// <summary>Finds a named transform (e.g. an exported socket bone) anywhere under this view.</summary>
    public Transform FindSocket(string socketName)
    {
        if (string.IsNullOrEmpty(socketName))
            return null;
        if (_sockets.TryGetValue(socketName, out Transform cached) && cached != null)
            return cached;

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == socketName)
            {
                _sockets[socketName] = t;
                return t;
            }
        }
        return null;
    }

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!castShadows)
                r.shadowCastingMode = ShadowCastingMode.Off;
            r.renderingLayerMask = renderingLayers;
            // Animated arms sit right at the near plane; stale bounds would cull them.
            if (r is SkinnedMeshRenderer smr)
                smr.updateWhenOffscreen = true;
        }
    }

    private void Update()
    {
        if (!IsHolstering)
            return;

        _putDownTimer += Time.deltaTime;
        if (_overlayPlaying)
        {
            int layer = OverlayLayerIndex();
            float weight = putDownBlend > 0f ? animator.GetLayerWeight(layer) + Time.deltaTime / putDownBlend : 1f;
            animator.SetLayerWeight(layer, Mathf.Min(weight, 1f));
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        bool finished = !animator.IsInTransition(0) && state.IsName(putDownState) && state.normalizedTime >= 1f;
        if (finished || _putDownTimer >= putDownTimeout)
            HolsterImmediate();
    }

    public void Draw()
    {
        IsHolstering = false;
        _overlayPlaying = false;
        gameObject.SetActive(true);
        if (animator == null || string.IsNullOrEmpty(drawState))
            return;

        animator.Rebind();
        int overlayLayer = OverlayLayerIndex();
        if (overlayLayer > 0)
            animator.SetLayerWeight(overlayLayer, 0f);
        animator.Play(drawState, 0, 0f);
        animator.Update(0f);
        Drawn?.Invoke();
    }

    /// <summary>Plays the put-down, then hides the view. Check IsHolstering to wait for it.</summary>
    public void Holster()
    {
        if (IsHolstering)
            return;
        if (!gameObject.activeInHierarchy || animator == null || string.IsNullOrEmpty(putDownState)
            || !animator.HasState(0, Animator.StringToHash(putDownState)))
        {
            HolsterImmediate();
            return;
        }

        IsHolstering = true;
        _putDownTimer = 0f;
        float sourceLength = animator.GetCurrentAnimatorStateInfo(0).length;
        animator.CrossFade(putDownState, sourceLength > 0f ? putDownBlend / sourceLength : 0f, 0, 1f - PutDownStartTime);

        // Faded in over putDownBlend in Update, like the base layer's cross-fade.
        int overlayLayer = OverlayLayerIndex();
        _overlayPlaying = overlayLayer > 0 && PutDownOverlayStartTime >= 0f;
        if (_overlayPlaying)
            animator.Play(putDownOverlayState, overlayLayer, 1f - PutDownOverlayStartTime);
    }

    public void HolsterImmediate()
    {
        IsHolstering = false;
        _overlayPlaying = false;
        gameObject.SetActive(false);
    }

    private int OverlayLayerIndex() =>
        string.IsNullOrEmpty(putDownOverlayLayer) ? -1 : animator.GetLayerIndex(putDownOverlayLayer);
}
