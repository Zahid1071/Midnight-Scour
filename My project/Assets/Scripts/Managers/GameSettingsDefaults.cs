using UnityEngine;

/// <summary>
/// Inspector-editable default game settings. Create via
/// Assets → Create → Game → Settings Defaults and assign to
/// <see cref="GameSettingsManager"/> (or place in Resources as GameSettingsDefaults).
/// </summary>
[CreateAssetMenu(fileName = "GameSettingsDefaults", menuName = "Game/Settings Defaults")]
public class GameSettingsDefaults : ScriptableObject
{
    [Tooltip("Values used on first launch, Reset to Defaults, and PlayerPrefs fallbacks.")]
    public GameSettingsManager.SettingsData defaults;
}
