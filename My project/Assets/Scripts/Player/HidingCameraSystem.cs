using UnityEngine;
using Unity.Cinemachine; // CM3 namespace

public class HidingCameraSystem : MonoBehaviour
{
    public static HidingCameraSystem Instance { get; private set; }
    
    [HideInInspector]
    public CinemachineCamera cmCamera; // CM3 Component

    HidingPeekLook peekLook;

    private void Awake()
    {
        Instance = this;
        cmCamera = GetComponent<CinemachineCamera>();
        cmCamera.Prioritize(); // Başlangıçta düşük öncelik (Sıfırla) -> CM3'te Priority yönetimi farklı olabilir, aşağıda el ile yapacağız.
        cmCamera.Priority = 0; // Default düşük olsun

        peekLook = GetComponent<HidingPeekLook>();
        if (peekLook == null)
            peekLook = gameObject.AddComponent<HidingPeekLook>();
    }

    // Kamerayı hedef pivotun içine (child olarak) ışınlar
    public void AttachToPivot(Transform targetPivot)
    {
        transform.SetParent(targetPivot);
        transform.localPosition = Vector3.zero; // Pivotun tam merkezine otur
        transform.localRotation = Quaternion.identity; // Rotasyonu eşitle
        peekLook?.ResetPeekAngles();
    }

    // Kamerayı serbest bırakır
    public void Detach()
    {
        peekLook?.ResetPeekAngles();
        transform.SetParent(null);
    }

    public void SetActive(bool active)
    {
        // CM3'te aktif kameranın Priority'sini arttırarak geçiş sağlarız
        cmCamera.Priority = active ? 20 : 0; 
    }
}