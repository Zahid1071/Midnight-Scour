/// <summary>String table collection names and entry keys for Unity Localization.</summary>
public static class LocalizationTables
{
    public const string MainMenu = "Main Menu";
    public const string Gameplay = "Gameplay";

    public static class MainMenuKeys
    {
        public const string BtnContinue = "btn_continue";
        public const string BtnNewGame = "btn_new_game";
        public const string BtnSettings = "btn_settings";
        public const string BtnCredits = "btn_credits";
        public const string BtnQuit = "btn_quit";
        public const string SettingsDisplayMode = "settings_display_mode";
        public const string SettingsBrightness = "settings_brightness";
        public const string SettingsMasterVolume = "settings_master_volume";
        public const string SettingsMouseSensitivity = "settings_mouse_sensitivity";
        public const string SettingsVsync = "settings_vsync";
        public const string SettingsAutosave = "settings_autosave";
        public const string SettingsChooseValue = "settings_choose_value";
        public const string SettingsLanguage = "settings_language";
        public const string SettingsTitle = "settings_title";
        public const string SettingsResolution = "settings_resolution";
        public const string SettingsQuality = "settings_quality";
        public const string SettingsMusicVolume = "settings_music_volume";
        public const string SettingsSfxVolume = "settings_sfx_volume";
        public const string TabDisplay = "tab_display";
        public const string TabAudio = "tab_audio";
        public const string TabControls = "tab_controls";
        public const string TabGameplay = "tab_gameplay";
        public const string TabSound = "tab_sound";
        public const string TabGeneral = "tab_general";
        public const string BtnApply = "btn_apply";
        public const string BtnBack = "btn_back";
        public const string BtnDefault = "btn_default";
        public const string BtnResume = "btn_resume";
        public const string BtnSaveGame = "btn_save_game";
        public const string PauseTitle = "pause_title";
        public const string LoadingStatus = "loading_status";
        public const string DialogDefaultTitle = "dialog_default_title";
        public const string DialogYes = "dialog_yes";
        public const string DialogNo = "dialog_no";
        public const string DialogQuitMessage = "dialog_quit_message";
        public const string DialogQuitTitle = "dialog_quit_title";
        public const string DialogQuitConfirm = "dialog_quit_confirm";
        public const string DialogReturnMenuMessage = "dialog_return_menu_message";
        public const string DialogReturnMenuTitle = "dialog_return_menu_title";
        public const string DialogReturnMenuConfirm = "dialog_return_menu_confirm";
    }

    public static class GameplayKeys
    {
        public const string HideWait = "hide_wait";
        public const string HideAlready = "hide_already";
        public const string CannotUseHere = "cannot_use_here";
        public const string NothingInReach = "nothing_in_reach";
        public const string CannotDropItem = "cannot_drop_item";
        public const string DroppedItem = "dropped_item";
        public const string InventoryFull = "inventory_full";
        public const string PickedUpItem = "picked_up_item";
        public const string NothingToStore = "nothing_to_store";
        public const string StoredItems = "stored_items";
        public const string InteractionNotConfigured = "interaction_not_configured";
        public const string ItemWasUsed = "item_was_used";
        public const string NeedItem = "need_item";
        public const string SlotNotConfigured = "slot_not_configured";
        public const string PartAlreadyInstalled = "part_already_installed";
        public const string SelectCarPartFirst = "select_car_part_first";
        public const string PartDoesNotFit = "part_does_not_fit";
        public const string DoNotHaveItem = "do_not_have_item";
        public const string PartInstalled = "part_installed";
        public const string EscapeNotSetup = "escape_not_setup";
        public const string AllPartsEscaped = "all_parts_escaped";
        public const string StillMissingParts = "still_missing_parts";
        public const string DoorLocked = "door_locked";
        public const string ItemCannotUseHere = "item_cannot_use_here";
        public const string DoNotHaveThatItem = "do_not_have_that_item";
        public const string ItemUnlockedDoor = "item_unlocked_door";
        public const string TipHide = "tip_hide";
        public const string TipExplore = "tip_explore";
        public const string TipSound = "tip_sound";
        public const string TipFlashlight = "tip_flashlight";
        public const string TipKeys = "tip_keys";
        public const string TipRun = "tip_run";
        public const string TipNotes = "tip_notes";
        public const string TipDark = "tip_dark";
    }

    /// <summary>Locale codes matching Assets/Languages locale assets.</summary>
    public static readonly string[] SupportedLocaleCodes = { "en", "tr", "zh", "ru", "es", "de", "pt-BR" };

    public static readonly string[] LanguageDisplayNames =
    {
        "English",
        "Türkçe",
        "中文",
        "Русский",
        "Español",
        "Deutsch",
        "Português (BR)"
    };
}
