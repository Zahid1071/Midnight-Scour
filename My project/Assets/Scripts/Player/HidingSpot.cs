using System.Collections; // Coroutine (IEnumerator) kullanmak için bu şart
using UnityEngine;

public class HidingSpot : MonoBehaviour
{
    [Header("Main Settings")]
    public Transform hidePosition; // Where the player gets moved to
    public bool isOccupied = false;

    // --- EKSİK DEĞİŞKENLER BURAYA EKLENDİ ---
    [Header("Pivots & Animators")]
    public Transform leftPivot;
    public Transform rightPivot;
    public Animator leftAnimator;
    public Animator rightAnimator;

    private Transform activePivot;
    private Animator activeAnimator;
    private bool isToggling = false; // İşlem sürerken spamlanmasını önler
    // ----------------------------------------

    private int normalLayer;
    private int hiddenLayer = 7;

    void Start()
    {
        // Player'ın sahnede olduğundan emin olup NullReference hatasını önlüyoruz
        GameObject playerObj = GameObject.Find("Player");
        if (playerObj != null)
        {
            normalLayer = playerObj.layer;
        }
    }

    public void HidePlayer(GameObject player)
    {
        if (isOccupied || isToggling) return;

        // hider değişkenini player üzerinden buluyoruz
        PlayerHider hider = player.GetComponent<PlayerHider>();
        if (hider == null) return;

        player.transform.position = hidePosition.position;
        player.transform.rotation = hidePosition.rotation;

        if (!hider.isHidden)
        {
            // --- MESAFE KONTROLÜ (BEYİN BURASI) ---
            float distToLeft = Vector3.Distance(player.transform.position, leftPivot.position);
            float distToRight = Vector3.Distance(player.transform.position, rightPivot.position);

            if (distToLeft < distToRight)
            {
                // Sol taraf daha yakın
                activePivot = leftPivot;
                activeAnimator = leftAnimator;
            }
            else
            {
                // Sağ taraf daha yakın
                activePivot = rightPivot;
                activeAnimator = rightAnimator;
            }

            // Animasyon ve gizlenme işlemini başlat
            StartCoroutine(EnterHidingRoutine(player, hider));
        }
    }

    private IEnumerator EnterHidingRoutine(GameObject player, PlayerHider hider)
    {
        isToggling = true;
        isOccupied = true;
        
        hider.SetHidden(true);
        player.layer = hiddenLayer;

        // IEnumerator içinde mutlaka bir yield return olmalı.
        // Dolaba girme animasyonunun bitmesini beklemek için buraya bir süre koyabilirsin.
        yield return new WaitForSeconds(0.5f); 

        isToggling = false;
    }

    public void RevealPlayer(GameObject player)
    {
        isOccupied = false;
        PlayerHider hider = player.GetComponent<PlayerHider>();
        
        if (hider != null)
        {
            hider.SetHidden(false);
        }

        // Çıkışta pozisyonu biraz ileri alıyoruz ki dolabın içinde sıkışmasın
        player.transform.position = hidePosition.position + new Vector3(0, 0, 1);
        player.layer = normalLayer;
    }

    public bool IsPlayerHere(GameObject player)
    {
        PlayerHider hider = player.GetComponent<PlayerHider>();
        
        return isOccupied && hider != null && hider.isHidden &&
            player.transform.position == hidePosition.position;
    }
}