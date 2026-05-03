using UnityEngine;
using System;

public class PerceptionController : MonoBehaviour
{
    [SerializeField] private Transform player;   // assign once in Inspector
    public Transform searchTarget;               // used by Suspicious/Search/Ambush

    // Events (both names for backward compatibility)
    public event Action OnVisionAlerted;   // soft-sight reached (Natural only)
    public event Action OnPlayerSeen;      // alias to the same moment
    public event Action OnVisionConfirmed; // sustained sight -> Chase

    // Runtime state
    public bool CanSeePlayer { get; private set; }
    public Vector3 LastKnownPlayerPosition { get; private set; }
    public float visionMeter { get; private set; } = 0f; // read-only (debug)

    private NPCController npc;

    // Cached config from NPCData
    float maxViewDistance, maxViewHalfAngle;
    float confirmSeconds, buildRate, decayRate;
    float preAlertSeconds;   // now comes from SO
    float gracePeriod, keepChaseDist;
    float ambushAudioMul;

    // Streak guards / timers
    bool alertedThisStreak, confirmedThisStreak;
    float lostSightTimer;

    LayerMask ignoreLayer;

    void Awake()
    {
        npc = GetComponent<NPCController>();
        var data = npc?.interaction?.data;

        if (data == null)
        {
            Debug.LogWarning($"{name}: NPCData missing. Using safe defaults.");
            maxViewDistance = 10f; maxViewHalfAngle = 90f;
            confirmSeconds = 1.5f; buildRate = 1f; decayRate = 1.5f;
            preAlertSeconds = 0.45f;               // fallback default
            gracePeriod = 2f; keepChaseDist = 3f;
            ambushAudioMul = 0.5f;
            return;
        }

        // Vision (from NPCData.VisionSettings)
        maxViewDistance  = data.vision.maxViewDistance;
        maxViewHalfAngle = data.vision.maxViewAngle;
        confirmSeconds   = data.vision.confirmSeconds;
        preAlertSeconds  = data.vision.preAlertSeconds;  // <-- read from SO
        buildRate        = data.vision.buildRate;
        decayRate        = data.vision.decayRate;

        // Hearing & chase retention
        ambushAudioMul   = data.hearing.ambushSuspicionMultiplier;
        gracePeriod      = data.chaseRetention.gracePeriod;
        keepChaseDist    = data.chaseRetention.keepChasingDistance;

        ignoreLayer      = data.vision.ignoreLayer;

        // Safety: if designer left it 0, derive a sane value
        if (preAlertSeconds <= 0f)
            preAlertSeconds = Mathf.Clamp(confirmSeconds * 0.3f, 0.2f, Mathf.Max(0.2f, confirmSeconds - 0.05f));
    }

    void Update()
    {
        PlayerCheckVision();
        UpdateVisionMeters();
        HandleVisionLoss();
        CageCheckVision();
    }

    private void PlayerCheckVision()
    {
        if (!player) { CanSeePlayer = false; return; }

        // 1. GİZLİLİK KONTROLÜ (Önceki düzeltmemiz)
        if (player.gameObject.layer == 7)
        {
            CanSeePlayer = false;
            return;
        }

        var playerCol = player.GetComponentInChildren<Collider>();
        if (!playerCol) { CanSeePlayer = false; return; }

        // Vektör Hesaplamaları
        Vector3 eye = transform.position + Vector3.up * 1.7f; // Göz hizası
        Vector3 aim = playerCol.bounds.center;                // Hedef (Oyuncu göğsü)
        Vector3 dirToTarget = (aim - eye).normalized;         // Hedefe giden yön vektörü

        // --- EKLENEN KISIM: AÇI KONTROLÜ (FOV) ---
        
        // NPC'nin yüzü (transform.forward) ile Hedef arasındaki açıyı bul
        float angleToTarget = Vector3.Angle(transform.forward, dirToTarget);

        // Eğer açı, izin verilen yarı-açıdan (maxViewHalfAngle) büyükse
        // (Örneğin 60 dereceden büyükse, yani oyuncu çok yandaysa veya arkadaysa)
        if (angleToTarget > maxViewHalfAngle)
        {
            CanSeePlayer = false;
            return; // Göremez, fonksiyondan çık
        }
        // -----------------------------------------

        // 2. RAYCAST (Mesafe ve Duvar Kontrolü)
        // Açı tutuyorsa şimdi de arada duvar var mı diye bakıyoruz
        if (Physics.Raycast(eye, dirToTarget, out RaycastHit hit, maxViewDistance, ~ignoreLayer, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.root == player.root)
            {
                LastKnownPlayerPosition = aim;
                CanSeePlayer = true;
                //Debug.Log(hit.transform.name);
                return;

            }
        }

        // Çizim debug (Sadece sahne ekranında görünür)
        Debug.DrawRay(eye, dirToTarget * maxViewDistance, CanSeePlayer ? Color.green : Color.red);

        CanSeePlayer = false;
    }

    private void UpdateVisionMeters()
    {
        // meter in seconds-equivalent
        if (CanSeePlayer) visionMeter += buildRate * Time.deltaTime;
        else              visionMeter -= decayRate * Time.deltaTime;

        visionMeter = Mathf.Clamp(visionMeter, 0f, confirmSeconds);

        // NATURAL-only soft alert gate
        if (!alertedThisStreak &&
            npc.currentState == NPCController.AIState.Natural &&
            visionMeter >= preAlertSeconds)
        {
            alertedThisStreak = true;
            OnVisionAlerted?.Invoke();
            OnPlayerSeen?.Invoke(); // alias for older/newer NPCController versions
        }

        // Confirm chase (any state)
        if (!confirmedThisStreak && visionMeter >= confirmSeconds)
        {
            confirmedThisStreak = true;
            OnVisionConfirmed?.Invoke();
        }

        // fully drained resets streak guards
        if (!CanSeePlayer && visionMeter <= 0f)
        {
            alertedThisStreak = false;
            confirmedThisStreak = false;
        }
    }

    public void HearPlayerFootstep(Transform soundSource, float suspicionAmount)
    {
        if (!soundSource) return;

        if (npc.currentState == NPCController.AIState.Ambush)
            suspicionAmount *= ambushAudioMul;

        string home = npc.interaction?.data?.homeID ?? string.Empty;
        Transform anchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(soundSource.position, home);
        if (!anchor) return;

        LastKnownPlayerPosition = soundSource.position;
        searchTarget = anchor;

        npc.GetComponent<SuspicionSystem>()?.AddFootstepSuspicion(suspicionAmount);
    }

    /// <summary>
    /// Loud one-shot at a world position (e.g. body bag impact). Unlike <see cref="HearPlayerFootstep"/>,
    /// does not abort when no anchor is found — NPC still gets footstep suspicion and last-known point.
    /// </summary>
    public void HearLoudWorldNoise(Vector3 worldPosition, float footstepSuspicionToAdd = 300f)
    {
        if (npc.currentState == NPCController.AIState.Ambush)
            footstepSuspicionToAdd *= ambushAudioMul;

        string home = npc.interaction?.data?.homeID ?? string.Empty;
        Transform anchor = NPCAnchorManager.Instance != null
            ? NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(worldPosition, home)
            : null;

        LastKnownPlayerPosition = worldPosition;
        searchTarget = anchor;

        npc.GetComponent<SuspicionSystem>()?.AddFootstepSuspicion(footstepSuspicionToAdd);
    }

    private void HandleVisionLoss()
    {
        if (CanSeePlayer) { lostSightTimer = 0f; return; }

        if (npc.currentState == NPCController.AIState.Chase)
        {
            float dist = Vector3.Distance(transform.position, player.position);

            if (dist < keepChaseDist)
            {
                lostSightTimer = 0f;
            }
            else
            {
                lostSightTimer += Time.deltaTime;
                // if (lostSightTimer >= gracePeriod)
                //     npc.fsm.SetState(NPCController.AIState.Suspicious);
            }
        }
    }

    [Header("Cage Settings")]
    public Cage targetCage; // Inspector'dan Cage.cs olan objeyi sürükle
    private bool cageAlertTriggered = false; // Sürekli spamlamaması için

    private void CageCheckVision()
    {
        if (targetCage == null || cageAlertTriggered) return;

        // Göz ve Hedef Vektörleri
        Vector3 eye = transform.position + Vector3.up * 1.7f;
        
        // Kafes Merkezini Bul
        Vector3 cageCenter = targetCage.transform.position;
        Collider triggerCol = targetCage.GetComponent<Collider>();
        if (triggerCol != null) cageCenter = triggerCol.bounds.center;
        else cageCenter += Vector3.up * 1.5f;

        Vector3 dirToCage = (cageCenter - eye).normalized;
        float distToCage = Vector3.Distance(eye, cageCenter);

        if (distToCage > maxViewDistance) return;

        // RAYCAST (Layer filtresi yok, direkt bakıyor)
        if (Physics.Raycast(eye, dirToCage, out RaycastHit hit, maxViewDistance))
        {
            Cage hitCage = hit.transform.GetComponentInParent<Cage>();

            if (hitCage != null && hitCage == targetCage)
            {
                
                // 1. Durum: İçerisi Boş mu?
                bool isEmpty = (hitCage.isOccupied == false);

                // 2. Durum: Kapı Açık mı? (Referans atanmışsa kontrol et, yoksa kapalı varsay)
                bool isOpen = (hitCage.cageDoor != null && hitCage.cageDoor.objectOpen);

                // VEYA (||) mantığı: Boşsa VEYA Açıksa -> ALARM!
                if (isEmpty || isOpen)
                {
                    Debug.Log($"⚠️ NPC: Player Escaped! (isEmpty: {isEmpty}, isOpen: {isOpen})");
                    
                    npc.fsm.SetState(NPCController.AIState.Search);
                    
                    string homeID = npc.interaction.data.homeID;
                    // var anchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(cageCenter, homeID);
                    // if (anchor) npc.perception.searchTarget = anchor;

                    cageAlertTriggered = true; // Tekrar spamlamasın
                }
            }
        }
    }

    // Convenience accessor
    public Transform PlayerTransform => player;
}