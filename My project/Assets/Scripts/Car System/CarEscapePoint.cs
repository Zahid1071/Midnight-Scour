using UnityEngine;

public class CarEscapePoint : MonoBehaviour, IInteractable
{
    public bool Interact()
    {
        var slots = transform.root.GetComponentsInChildren<CarPartSlot>(true);
        if (slots == null || slots.Length == 0)
        {
            HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.escape_not_setup"), 3f);
            Debug.LogWarning("⚠️ No CarPartSlot components found under CarEscapePoint.");
            return false;
        }

        var missing = new System.Collections.Generic.List<string>();
        foreach (var slot in slots)
        {
            if (!slot.IsInstalled())
                missing.Add(slot.GetRequiredItemName());
        }

        if (missing.Count == 0)
        {
            var clip = SoundManager.Instance.sfxBank[2];
            if (!SoundManager.Instance.IsClipPlayingAt(clip, transform.position))
                SoundManager.Instance.PlaySFXAt(clip, transform.position, 0.7f);

            HudManager.Instance?.ShowSubtitle(
                Loc.Get("gameplay.all_parts_installed"), 4f, HudManager.Instance.SuccessColor);
            Debug.Log("✅ All parts installed! You escaped!");
            return true;
        }

        var failClip = SoundManager.Instance.sfxBank[1];
        if (!SoundManager.Instance.IsClipPlayingAt(failClip, transform.position))
            SoundManager.Instance.PlaySFXAt(failClip, transform.position);

        string list = string.Join(", ", missing);
        HudManager.Instance?.ShowSubtitle(Loc.Format("gameplay.still_missing", list), 4f);
        Debug.Log("❌ You still need some parts to escape.");
        return false;
    }
}
