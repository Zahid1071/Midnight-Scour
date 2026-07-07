using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ReadableLetter : MonoBehaviour, IInteractable
{
    [SerializeField] private string letterKey;
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

        string text = !string.IsNullOrWhiteSpace(letterKey) ? Loc.Get(letterKey) : letterText;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        InteractionFeedback.PlaySound(interactSFX, transform.position);
        ReadPanelUI.Instance.Show(text);
        return true;
    }
}
