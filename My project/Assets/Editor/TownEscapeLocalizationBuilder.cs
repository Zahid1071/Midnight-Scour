using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

/// <summary>
/// Populates Main Menu and Gameplay string tables with all project locales.
/// Menu: Town Escape → Populate Localization Tables
/// </summary>
public static class TownEscapeLocalizationBuilder
{
    private const string LanguagesFolder = "Assets/Languages";

    [MenuItem("Town Escape/Populate Localization Tables")]
    public static void PopulateAll()
    {
        PopulateCollection(LocalizationTables.MainMenu, TownEscapeLocalizationData.MainMenu);
        EnsureAndPopulateGameplay();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Town Escape] Localization tables populated (en, tr, zh, ru, es, de, pt-BR).");
    }

    private static void EnsureAndPopulateGameplay()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(LocalizationTables.Gameplay);
        if (collection == null)
        {
            collection = LocalizationEditorSettings.CreateStringTableCollection(
                LocalizationTables.Gameplay,
                LanguagesFolder);
            Debug.Log("[Town Escape] Created Gameplay string table collection.");
        }

        PopulateCollection(LocalizationTables.Gameplay, TownEscapeLocalizationData.Gameplay);
    }

    private static void PopulateCollection(
        string collectionName,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> rows)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(collectionName);
        if (collection == null)
        {
            Debug.LogError($"[Town Escape] String table collection not found: {collectionName}");
            return;
        }

        var tablesToDirty = new HashSet<StringTable>();

        foreach (var row in rows)
        {
            string key = row.Key;
            var translations = row.Value;

            foreach (var stringTable in collection.StringTables)
            {
                if (stringTable == null)
                    continue;

                string code = stringTable.LocaleIdentifier.Code;
                if (!translations.TryGetValue(code, out string text))
                    text = translations.TryGetValue("en", out var en) ? en : key;

                Undo.RecordObject(stringTable, "Populate Localization");
                Undo.RecordObject(stringTable.SharedData, "Populate Localization");

                var entry = stringTable.GetEntry(key);
                if (entry == null)
                    entry = stringTable.AddEntry(key, text);
                else
                    entry.Value = text;

                tablesToDirty.Add(stringTable);
            }
        }

        foreach (var table in tablesToDirty)
        {
            EditorUtility.SetDirty(table);
            EditorUtility.SetDirty(table.SharedData);
        }

        EditorUtility.SetDirty(collection);
        EditorUtility.SetDirty(collection.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, collection);
    }
}
