using UnityEngine;

public class CarEscapePoint : MonoBehaviour, IInteractable
{
    public void Interact()
    {
        var slots = transform.root.GetComponentsInChildren<CarPartSlot>(true);
        if (slots == null || slots.Length == 0)
        {
            HudManager.Instance?.ShowSubtitle("Escape sequence is not set up.", 3f);
            Debug.LogWarning("⚠️ No CarPartSlot components found under CarEscapePoint.");
            return;
        }

        var missing = new System.Collections.Generic.List<string>();
        foreach (var slot in slots)
        {
            if (!slot.IsInstalled())
                missing.Add(slot.GetRequiredItemName());
        }

        if (missing.Count == 0)
        {
            var clip = SoundManager.Instance.sfxBank[2]; // success / car start
            if (!SoundManager.Instance.IsClipPlayingAt(clip, transform.position))
                SoundManager.Instance.PlaySFXAt(clip, transform.position, 0.7f);

            HudManager.Instance?.ShowSubtitle("All parts installed. You escaped!", 4f);
            Debug.Log("✅ All parts installed! You escaped!");

            // TODO: trigger win screen, fade, load scene etc.
        }
        else
        {
            var clip = SoundManager.Instance.sfxBank[1]; // fail / locked
            if (!SoundManager.Instance.IsClipPlayingAt(clip, transform.position))
                SoundManager.Instance.PlaySFXAt(clip, transform.position);

            string list = string.Join(", ", missing);
            HudManager.Instance?.ShowSubtitle($"Still missing: {list}.", 4f);
            Debug.Log("❌ You still need some parts to escape.");
        }
    }
}
