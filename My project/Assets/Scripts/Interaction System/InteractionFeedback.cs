using UnityEngine;

/// <summary>
/// Shared interact feedback: subtitle, crosshair shake, and optional SFX.
/// </summary>
public static class InteractionFeedback
{
    public static void Fail(string message = null, float duration = 2.5f)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            var hud = HudManager.Instance;
            if (hud != null)
                hud.ShowSubtitle(message, duration, hud.WarningColor);
        }

        SoundManager.Instance?.PlayActionFail();
        InteractionUIManager.Instance?.PlayFailShake();
    }

    public static void Success(string message = null, AudioClip sound = null, Vector3? soundPosition = null, float duration = 3f)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            var hud = HudManager.Instance;
            if (hud != null)
                hud.ShowSubtitle(message, duration, hud.SuccessColor);
        }

        PlaySound(sound, soundPosition);
        InteractionUIManager.Instance?.PlaySuccessShake();
    }

    public static void PlaySound(AudioClip clip, Vector3? worldPosition = null)
    {
        if (clip == null || SoundManager.Instance == null)
            return;

        if (worldPosition.HasValue)
            SoundManager.Instance.PlaySFXAt(clip, worldPosition.Value);
        else
            SoundManager.Instance.PlaySFX(clip);
    }
}
