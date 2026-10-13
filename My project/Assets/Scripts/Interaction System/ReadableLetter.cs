using System;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class ReadableLetter : MonoBehaviour, IInteractable
{
    const string PadlockHairpinLetterKey = "letter.padlock_hairpin";

    [SerializeField] private string letterKey;
    [TextArea(4, 12)]
    [SerializeField] private string letterText = "Letter text goes here.";
    [SerializeField] private AudioClip interactSFX;
    [Tooltip("Invoked every time the player closes the letter.")]
    [SerializeField] private UnityEvent onClosed;

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

        Action closed = null;
        if (string.Equals(letterKey, PadlockHairpinLetterKey, StringComparison.Ordinal))
            closed += () => GameplayHintManager.Instance?.NotifyPadlockHairpinLetterClosed();
        if (onClosed != null && onClosed.GetPersistentEventCount() > 0)
            closed += onClosed.Invoke;
        ReadPanelUI.Instance.Show(text, closed);
        return true;
    }
}
