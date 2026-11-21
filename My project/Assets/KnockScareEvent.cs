using UnityEngine;

public class KnockScareEvent : MonoBehaviour
{
    [SerializeField] private AudioClip knockClip;
    [SerializeField] private Transform soundLocation;

    private void OnDestroy()
    {
        // Scene unload / quit / checkpoint rebuilds destroy world pickups — never replay the knock then.
        if (!gameObject.scene.isLoaded || Checkpoint.IsRestoring)
            return;

        if (knockClip != null && soundLocation != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(knockClip, soundLocation.position);
    }
}