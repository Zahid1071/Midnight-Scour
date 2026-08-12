using System;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ReadableLetter : MonoBehaviour, IInteractable
{
    const string PadlockHairpinLetterKey = "letter.padlock_hairpin";

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

        bool padlockLetter = string.Equals(letterKey, PadlockHairpinLetterKey, StringComparison.Ordinal);
        ReadPanelUI.Instance.Show(text, padlockLetter
            ? () => GameplayHintManager.Instance?.NotifyPadlockHairpinLetterClosed()
            : null);
        return true;
    }
}
