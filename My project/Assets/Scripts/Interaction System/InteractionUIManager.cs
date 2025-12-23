using UnityEngine;
using UnityEngine.UI; // Image ve Sprite işlemleri için şart

public class InteractionUIManager : MonoBehaviour
{
    [Header("Detection Settings")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float detectionRange = 3f;
    [SerializeField] private LayerMask interactLayer;

    [Header("Crosshair (Nişangah) Ayarları")]
    [Tooltip("Canvas ortasındaki Image")]
    [SerializeField] private Image crosshairImage; 
    [Tooltip("Boş ikon (Nokta)")]
    [SerializeField] private Sprite defaultCrosshair;
    [Tooltip("Dolu ikon (El/Göz)")]
    [SerializeField] private Sprite interactCrosshair;
    
    [Header("Button Prompt (Tuş İpucu) Ayarları")]
    [Tooltip("Sağ alttaki 'E' harfinin görüneceği Image")]
    [SerializeField] private Image buttonPromptImage; 
    
    [Tooltip("Tuşun resmi (Örn: E harfi sprite'ı)")]
    [SerializeField] private Sprite buttonPromptSprite;

    [Header("Outline Settings")]
    [SerializeField] private Color outlineColor = Color.yellow;
    [SerializeField, Range(0f, 10f)] private float outlineWidth = 3f;

    private GameObject lastDetectedObject;
    private Outline lastOutline;

    private void Start()
    {
        // 1. Crosshair'i varsayılan hale getir
        if (crosshairImage && defaultCrosshair)
        {
            crosshairImage.sprite = defaultCrosshair;
            crosshairImage.enabled = true; // Görünür yap
        }

        // 2. Buton ipucunu başlangıçta GİZLE
        // GameObject'i değil, sadece resim bileşenini kapatıyoruz.
        if (buttonPromptImage) 
        {
            buttonPromptImage.enabled = false; 
        }
    }

    private void Update()
    {
        DetectInteractable();
    }

    private void DetectInteractable()
    {
        if (cameraTransform == null) return;

        if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, 
            out RaycastHit hit, detectionRange, interactLayer))
        {
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            
            if (interactable != null)
            {
                GameObject hitObject = hit.collider.gameObject;

                if (hitObject != lastDetectedObject)
                {
                    ClearDetection(false); 
                    HighlightObject(hitObject);
                }

                // UI GÖSTER
                ShowInteractionUI();
                return;
            }
        }

        // UI GİZLE / TEMİZLE
        ClearDetection(true); 
    }

    private void HighlightObject(GameObject obj)
    {
        lastDetectedObject = obj;
        lastOutline = obj.GetComponent<Outline>();

        if (lastOutline == null)
        {
            lastOutline = obj.AddComponent<Outline>();
            lastOutline.OutlineMode = Outline.Mode.OutlineVisible;
        }

        lastOutline.OutlineColor = outlineColor;
        lastOutline.OutlineWidth = outlineWidth;
        lastOutline.enabled = true;
    }

    private void ShowInteractionUI()
    {
        // A. Crosshair'i değiştir
        if (crosshairImage && interactCrosshair)
        {
            if (crosshairImage.sprite != interactCrosshair)
                crosshairImage.sprite = interactCrosshair;
        }

        // B. Button Prompt'u göster ve resmini ata
        if (buttonPromptImage && buttonPromptSprite) 
        {
            // Resmi ata (Belki ileride farklı tuşlar istersin diye her seferinde atıyoruz)
            buttonPromptImage.sprite = buttonPromptSprite;
            
            // Resmi görünür yap
            buttonPromptImage.enabled = true;
        }
    }

    private void ClearDetection(bool resetUI)
    {
        // 1. Crosshair'i varsayılana döndür
        if (resetUI && crosshairImage && defaultCrosshair)
        {
            if (crosshairImage.sprite != defaultCrosshair)
                crosshairImage.sprite = defaultCrosshair;
        }

        // 2. Button Prompt resmini kapat (Görünmez yap)
        if (buttonPromptImage) 
        {
            buttonPromptImage.enabled = false; 
        }

        // 3. Outline temizle
        if (lastOutline != null)
        {
            lastOutline.enabled = false;
            lastOutline = null;
        }

        lastDetectedObject = null;
    }

    private void OnDisable()
    {
        ClearDetection(true);
    }

    private void OnDrawGizmosSelected()
    {
        if (cameraTransform == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(cameraTransform.position, cameraTransform.forward * detectionRange);
    }
}
