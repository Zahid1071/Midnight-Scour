using UnityEngine;

public class CinematicToggle : MonoBehaviour
{
    [Header("Components to Toggle")]
    [Tooltip("Drag the movement, look, and interaction scripts here.")]
    public MonoBehaviour[] playerScripts;

    /// <summary>
    /// Disables all scripts in the list. Call this at the START of a cinematic.
    /// </summary>
    public void DisablePlayerScripts()
    {
        ToggleScripts(false);
    }

    /// <summary>
    /// Enables all scripts in the list. Call this at the END of a cinematic.
    /// </summary>
    public void EnablePlayerScripts()
    {
        ToggleScripts(true);
    }

    private void ToggleScripts(bool state)
    {
        if (playerScripts == null) return;

        foreach (var script in playerScripts)
        {
            if (script != null)
            {
                script.enabled = state;
            }
        }
    }
}