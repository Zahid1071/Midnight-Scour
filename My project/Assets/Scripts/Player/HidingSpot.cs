using System.Collections; // Coroutine (IEnumerator) kullanmak için bu şart
using UnityEngine;

public class HidingSpot : MonoBehaviour, IInteractable
{
    public bool isOccupied { get; private set; }

    [Header("Setup - Sides")]
    public Transform leftPivot; 
    public Animator leftAnimator;
    
    public Transform rightPivot;
    public Animator rightAnimator;

    [Header("Settings")]
    public float blendDuration = 1.0f;
    public float animationDelay = 0.2f;

    // O an hangi tarafı kullanıyoruz? (Kod karar verecek)
    private Transform activePivot;
    private Animator activeAnimator;

    private int defaultLayer;
    private int hiddenLayer = 7;
    private bool isToggling = false;
    

    void Start()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) defaultLayer = player.layer;
    }

    public void Interact()
    {
        if (isToggling) return;

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;
        
        PlayerHider hider = player.GetComponent<PlayerHider>();
        if (hider == null) return;

        if (!hider.isHidden)
        {
            // --- MESAFE KONTROLÜ ---
            // If there is no right side assigned (like a locker), default to the left side.
            if (rightPivot == null)
            {
                activePivot = leftPivot;
                activeAnimator = leftAnimator;
                Debug.Log("Single entry point used (e.g. Locker).");
            }
            else
            {
                // Two sides exist (like a bed). Compare distances.
                float distToLeft = Vector3.Distance(player.transform.position, leftPivot.parent.parent.position);
                float distToRight = Vector3.Distance(player.transform.position, rightPivot.parent.parent.position);

                if (distToLeft < distToRight)
                {
                    activePivot = leftPivot;
                    activeAnimator = leftAnimator;
                    Debug.Log("left");
                }
                else
                {
                    activePivot = rightPivot;
                    activeAnimator = rightAnimator;
                    Debug.Log("right");
                }
            }

            StartCoroutine(EnterHidingRoutine(player, hider));
        }
    }

    private IEnumerator EnterHidingRoutine(GameObject player, PlayerHider hider)
    {
        isToggling = true;
        isOccupied = true;

        // Seçilen (Active) pivotu kullan
        HidingCameraSystem.Instance.AttachToPivot(activePivot);
        
        if(activeAnimator != null) activeAnimator.Play("Idle");

        HidingCameraSystem.Instance.SetActive(true);
        hider.DisableControlsOnly(); 

        yield return new WaitForSeconds(blendDuration);

        hider.SetVisuals(false);
        player.layer = hiddenLayer;
        hider.SetHiddenState(true, this);

        yield return new WaitForSeconds(animationDelay);

        if(activeAnimator != null) activeAnimator.SetTrigger("Enter");

        isToggling = false;
    }

    public void RevealPlayer(GameObject player)
    {
        if (!isOccupied || isToggling) return;
        StartCoroutine(ExitHidingRoutine(player));
    }

    private IEnumerator ExitHidingRoutine(GameObject player)
    {
        isToggling = true;

        if (activeAnimator != null)
        {
            float startOffset = 0f;
            AnimatorStateInfo stateInfo = activeAnimator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.IsName("Enter"))
            {
                if (stateInfo.normalizedTime < 1.0f)
                {
                    startOffset = 1.0f - stateInfo.normalizedTime;
                }
            }

            // Seçilen (Active) animatörü kullan
            activeAnimator.Play("Exit", 0, startOffset);

            yield return null; 

            yield return new WaitUntil(() => 
                activeAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f);
        }
        else
        {
            yield return new WaitForSeconds(0.5f);
        }

        HidingCameraSystem.Instance.SetActive(false);
        
        PlayerHider hider = player.GetComponent<PlayerHider>();
        if (hider != null)
        {
            hider.SetVisuals(true);
            hider.SetHiddenState(false, null);
        }
        player.layer = defaultLayer;

        yield return new WaitForSeconds(blendDuration);

        if (hider != null) hider.EnableControls();
        HidingCameraSystem.Instance.Detach();

        isOccupied = false;
        isToggling = false;
        
        // Temizlik: Referansları boşaltabiliriz (opsiyonel)
        activePivot = null;
        activeAnimator = null;
    }
}