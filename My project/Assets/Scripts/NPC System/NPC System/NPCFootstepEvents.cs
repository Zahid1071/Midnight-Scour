using UnityEngine;

public class NPCFootstepEvent : MonoBehaviour
{
    public FootstepController footstepController;

    public void PlayFootstepAnimEvent()
    {
        if (footstepController == null) return;
        footstepController.PlayFootstep(footstepController.walkVolume);
    }
}
