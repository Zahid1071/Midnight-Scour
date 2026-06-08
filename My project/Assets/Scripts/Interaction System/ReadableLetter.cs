using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ReadableLetter : MonoBehaviour, IInteractable
{
    [TextArea(4, 12)]
    [SerializeField] private string letterText = "Letter text goes here.";
    [SerializeField] private AudioClip interactSFX;

    public bool Interact()
    {
        if (ReadPanelUI.Instance != null && ReadPanelUI.IsBlockingPlayerInput)
            return false;

        if (ReadPanelUI.Instance == null)
        {
            Debug.LogWarning("[ReadableLetter] ReadPanelUI not found in scene.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(letterText))
            return false;

        InteractionFeedback.PlaySound(interactSFX, transform.position);
        ReadPanelUI.Instance.Show(letterText);
        return true;
    }
}
