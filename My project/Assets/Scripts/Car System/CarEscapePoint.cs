using UnityEngine;

public class CarEscapePoint : MonoBehaviour, IInteractable
{

    float _nextSuccessTime;
    float _nextFailTime;

    public void Interact()
    {
        var slots = transform.root.GetComponentsInChildren<CarPartSlot>(true);
        if (slots == null || slots.Length == 0)
        {
            Debug.LogWarning("⚠️ No CarPartSlot components found under CarEscapePoint.");
            return;
        }

        bool allInstalled = true;
        foreach (var slot in slots)
        {
            if (!slot.IsInstalled())
            {
                Debug.Log($"❌ Missing part: {slot.GetRequiredItemName()}");
                allInstalled = false;
            }
        }

        if (allInstalled)
        {
            var clip = SoundManager.Instance.sfxBank[2]; // success / car start
            // 1) block if already playing at this spot, 2) respect cooldown
            if (!SoundManager.Instance.IsClipPlayingAt(clip, transform.position))
            {
                Debug.Log("✅ All parts installed! You escaped!");
                SoundManager.Instance.PlaySFXAt(clip, transform.position, 0.7f);
            }

            // TODO: trigger win screen, fade, load scene etc.
        }
        else
        {
            var clip = SoundManager.Instance.sfxBank[1]; // fail / locked
            if (!SoundManager.Instance.IsClipPlayingAt(clip, transform.position))
            {
                Debug.Log("❌ You still need some parts to escape.");
                SoundManager.Instance.PlaySFXAt(clip, transform.position);

            }
        }
    }
}
