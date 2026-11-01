using UnityEngine;

public class KnockScareEvent : MonoBehaviour
{
    [SerializeField] private AudioClip knockClip;
    [SerializeField] private Transform soundLocation;

    private void OnDestroy()
    {
        // Prevents the sound from triggering when the scene unloads or the game quits
        if (!gameObject.scene.isLoaded) return;

        if (knockClip != null && soundLocation != null)
        {
            // Replace 'SoundManager.Instance' and 'PlaySound' with your actual Singleton and method names
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFXAt(knockClip, soundLocation.position);
            }
        }
    }
}