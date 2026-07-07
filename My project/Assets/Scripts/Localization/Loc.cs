using UnityEngine.Localization.Settings;

public static class Loc
{
    public const string Table = "GameStrings";

    public static string Get(string key) =>
        LocalizationSettings.StringDatabase.GetLocalizedString(Table, key);

    public static string Format(string key, params object[] args) =>
        LocalizationSettings.StringDatabase.GetLocalizedString(Table, key, args);
}
