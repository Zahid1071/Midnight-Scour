using UnityEngine.Localization.Settings;

public static class Loc
{
    public const string Table = "GameStrings";

    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        // Fetching before init is done can force a synchronous WaitForCompletion on the string
        // table load. Called from a safe (non-nested) context that's harmless, but if this ever
        // runs from inside another Addressables completion callback it can crash with
        // "Reentering the Update method is not allowed". Bail out to the raw key instead - callers
        // driven by LocaleManager.WhenReady/LocalizedUILabel refresh again once it's ready.
        if (!LocalizationSettings.InitializationOperation.IsDone)
            return key;

        return LocalizationSettings.StringDatabase.GetLocalizedString(Table, key);
    }

    public static string Format(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (!LocalizationSettings.InitializationOperation.IsDone)
            return key;

        return LocalizationSettings.StringDatabase.GetLocalizedString(Table, key, args);
    }
}
