using System.Collections.Generic;

/// <summary>Source strings for all supported locales (en, tr, zh, ru, es, de, pt-BR).</summary>
internal static class TownEscapeLocalizationData
{
    private static readonly string[] Locales = LocalizationTables.SupportedLocaleCodes;

    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> MainMenu =>
        Build(MainMenuRows);

    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Gameplay =>
        Build(GameplayRows);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Build(
        IReadOnlyDictionary<string, string[]> rows)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var row in rows)
        {
            var map = new Dictionary<string, string>();
            for (int i = 0; i < Locales.Length; i++)
                map[Locales[i]] = row.Value[i];
            result[row.Key] = map;
        }
        return result;
    }

    // Order: en, tr, zh, ru, es, de, pt-BR
    private static readonly IReadOnlyDictionary<string, string[]> MainMenuRows =
        new Dictionary<string, string[]>
        {
            [LocalizationTables.MainMenuKeys.BtnContinue] = new[]
            {
                "Continue", "Devam Et", "继续", "Продолжить", "Continuar", "Fortsetzen", "Continuar"
            },
            [LocalizationTables.MainMenuKeys.BtnNewGame] = new[]
            {
                "New Game", "Yeni Oyun", "新游戏", "Новая игра", "Nueva partida", "Neues Spiel", "Novo jogo"
            },
            [LocalizationTables.MainMenuKeys.BtnSettings] = new[]
            {
                "Settings", "Ayarlar", "设置", "Настройки", "Ajustes", "Einstellungen", "Configurações"
            },
            [LocalizationTables.MainMenuKeys.BtnCredits] = new[]
            {
                "Credits", "Jenerik", "制作人员", "Титры", "Créditos", "Credits", "Créditos"
            },
            [LocalizationTables.MainMenuKeys.BtnQuit] = new[]
            {
                "Quit Game", "Oyundan Çık", "退出游戏", "Выйти из игры", "Salir del juego", "Spiel beenden", "Sair do jogo"
            },
            [LocalizationTables.MainMenuKeys.SettingsDisplayMode] = new[]
            {
                "Display Mode", "Görüntü Modu", "显示模式", "Режим экрана", "Modo de pantalla", "Anzeigemodus", "Modo de exibição"
            },
            [LocalizationTables.MainMenuKeys.SettingsBrightness] = new[]
            {
                "Brightness", "Parlaklık", "亮度", "Яркость", "Brillo", "Helligkeit", "Brilho"
            },
            [LocalizationTables.MainMenuKeys.SettingsMasterVolume] = new[]
            {
                "Master Volume", "Ana Ses", "主音量", "Общая громкость", "Volumen general", "Gesamtlautstärke", "Volume principal"
            },
            [LocalizationTables.MainMenuKeys.SettingsMouseSensitivity] = new[]
            {
                "Mouse Sensitivity", "Fare Hassasiyeti", "鼠标灵敏度", "Чувствительность мыши", "Sensibilidad del ratón", "Mausempfindlichkeit", "Sensibilidade do mouse"
            },
            [LocalizationTables.MainMenuKeys.SettingsVsync] = new[]
            {
                "VSync", "VSync", "垂直同步", "VSync", "VSync", "VSync", "VSync"
            },
            [LocalizationTables.MainMenuKeys.SettingsAutosave] = new[]
            {
                "Auto-Save", "Otomatik Kayıt", "自动保存", "Автосохранение", "Autoguardado", "Autom. Speichern", "Salvamento automático"
            },
            [LocalizationTables.MainMenuKeys.SettingsChooseValue] = new[]
            {
                "Choose Value", "Değer Seç", "选择数值", "Выберите значение", "Elegir valor", "Wert wählen", "Escolher valor"
            },
            [LocalizationTables.MainMenuKeys.SettingsLanguage] = new[]
            {
                "Language", "Dil", "语言", "Язык", "Idioma", "Sprache", "Idioma"
            },
            [LocalizationTables.MainMenuKeys.SettingsTitle] = new[]
            {
                "Settings", "Ayarlar", "设置", "Настройки", "Ajustes", "Einstellungen", "Configurações"
            },
            [LocalizationTables.MainMenuKeys.SettingsResolution] = new[]
            {
                "Resolution", "Çözünürlük", "分辨率", "Разрешение", "Resolución", "Auflösung", "Resolução"
            },
            [LocalizationTables.MainMenuKeys.SettingsQuality] = new[]
            {
                "Quality", "Kalite", "画质", "Качество", "Calidad", "Qualität", "Qualidade"
            },
            [LocalizationTables.MainMenuKeys.SettingsMusicVolume] = new[]
            {
                "Music Volume", "Müzik Sesi", "音乐音量", "Громкость музыки", "Volumen de música", "Musiklautstärke", "Volume da música"
            },
            [LocalizationTables.MainMenuKeys.SettingsSfxVolume] = new[]
            {
                "SFX Volume", "Efekt Sesi", "音效音量", "Громкость эффектов", "Volumen de efectos", "Effektlautstärke", "Volume de efeitos"
            },
            [LocalizationTables.MainMenuKeys.TabDisplay] = new[]
            {
                "Display", "Görüntü", "显示", "Экран", "Pantalla", "Anzeige", "Tela"
            },
            [LocalizationTables.MainMenuKeys.TabAudio] = new[]
            {
                "Audio", "Ses", "音频", "Звук", "Audio", "Audio", "Áudio"
            },
            [LocalizationTables.MainMenuKeys.TabControls] = new[]
            {
                "Controls", "Kontroller", "控制", "Управление", "Controles", "Steuerung", "Controles"
            },
            [LocalizationTables.MainMenuKeys.TabGameplay] = new[]
            {
                "Gameplay", "Oynanış", "游戏", "Геймплей", "Jugabilidad", "Spiel", "Jogabilidade"
            },
            [LocalizationTables.MainMenuKeys.TabSound] = new[]
            {
                "Sound", "Ses", "声音", "Звук", "Sonido", "Ton", "Som"
            },
            [LocalizationTables.MainMenuKeys.TabGeneral] = new[]
            {
                "General", "Genel", "常规", "Общие", "General", "Allgemein", "Geral"
            },
            [LocalizationTables.MainMenuKeys.BtnApply] = new[]
            {
                "Apply", "Uygula", "应用", "Применить", "Aplicar", "Anwenden", "Aplicar"
            },
            [LocalizationTables.MainMenuKeys.BtnBack] = new[]
            {
                "Back", "Geri", "返回", "Назад", "Atrás", "Zurück", "Voltar"
            },
            [LocalizationTables.MainMenuKeys.BtnDefault] = new[]
            {
                "Default", "Varsayılan", "默认", "По умолчанию", "Predeterminado", "Standard", "Padrão"
            },
            [LocalizationTables.MainMenuKeys.BtnResume] = new[]
            {
                "Resume", "Devam Et", "继续", "Продолжить", "Reanudar", "Fortsetzen", "Continuar"
            },
            [LocalizationTables.MainMenuKeys.BtnSaveGame] = new[]
            {
                "Save Game", "Oyunu Kaydet", "保存游戏", "Сохранить игру", "Guardar partida", "Spiel speichern", "Salvar jogo"
            },
            [LocalizationTables.MainMenuKeys.PauseTitle] = new[]
            {
                "Pause", "Duraklat", "暂停", "Пауза", "Pausa", "Pause", "Pausar"
            },
            [LocalizationTables.MainMenuKeys.DialogDefaultTitle] = new[]
            {
                "Confirm", "Onay", "确认", "Подтверждение", "Confirmar", "Bestätigen", "Confirmar"
            },
            [LocalizationTables.MainMenuKeys.DialogYes] = new[]
            {
                "Yes", "Evet", "是", "Да", "Sí", "Ja", "Sim"
            },
            [LocalizationTables.MainMenuKeys.DialogNo] = new[]
            {
                "No", "Hayır", "否", "Нет", "No", "Nein", "Não"
            },
            [LocalizationTables.MainMenuKeys.DialogQuitMessage] = new[]
            {
                "Are you sure you want to quit?\nUnsaved progress will be lost.",
                "Oyundan çıkmak istediğinize emin misiniz?\nKaydedilmemiş ilerleme kaybolacak.",
                "确定要退出游戏吗？\n未保存的进度将丢失。",
                "Выйти из игры?\nНесохранённый прогресс будет потерян.",
                "¿Seguro que quieres salir?\nSe perderá el progreso no guardado.",
                "Spiel wirklich beenden?\nUngespeicherter Fortschritt geht verloren.",
                "Sair do jogo?\nO progresso não salvo será perdido."
            },
            [LocalizationTables.MainMenuKeys.DialogQuitTitle] = new[]
            {
                "Quit", "Çıkış", "退出", "Выход", "Salir", "Beenden", "Sair"
            },
            [LocalizationTables.MainMenuKeys.DialogQuitConfirm] = new[]
            {
                "Quit", "Çık", "退出", "Выйти", "Salir", "Beenden", "Sair"
            },
            [LocalizationTables.MainMenuKeys.DialogReturnMenuMessage] = new[]
            {
                "Return to the main menu?\nUnsaved progress will be lost.",
                "Ana menüye dönmek istediğinize emin misiniz?\nKaydedilmemiş ilerleme kaybolacak.",
                "返回主菜单？\n未保存的进度将丢失。",
                "Вернуться в главное меню?\nНесохранённый прогресс будет потерян.",
                "¿Volver al menú principal?\nSe perderá el progreso no guardado.",
                "Zum Hauptmenü zurück?\nUngespeicherter Fortschritt geht verloren.",
                "Voltar ao menu principal?\nO progresso não salvo será perdido."
            },
            [LocalizationTables.MainMenuKeys.DialogReturnMenuTitle] = new[]
            {
                "Main Menu", "Ana Menü", "主菜单", "Главное меню", "Menú principal", "Hauptmenü", "Menu principal"
            },
            [LocalizationTables.MainMenuKeys.DialogReturnMenuConfirm] = new[]
            {
                "Return", "Dön", "返回", "Вернуться", "Volver", "Zurück", "Voltar"
            },
            [LocalizationTables.MainMenuKeys.LoadingStatus] = new[]
            {
                "Loading...", "Yükleniyor...", "加载中...", "Загрузка...", "Cargando...", "Lädt...", "Carregando..."
            },
        };

    private static readonly IReadOnlyDictionary<string, string[]> GameplayRows =
        new Dictionary<string, string[]>
        {
            [LocalizationTables.GameplayKeys.HideWait] = new[]
            {
                "Wait a moment.", "Biraz bekleyin.", "请稍候。", "Подождите немного.", "Espera un momento.", "Einen Moment warten.", "Aguarde um momento."
            },
            [LocalizationTables.GameplayKeys.HideAlready] = new[]
            {
                "You are already hiding.", "Zaten saklanıyorsunuz.", "你已经在躲藏了。", "Вы уже прячетесь.", "Ya te estás escondiendo.", "Du versteckst dich bereits.", "Você já está se escondendo."
            },
            [LocalizationTables.GameplayKeys.CannotUseHere] = new[]
            {
                "You cannot use that here.", "Bunu burada kullanamazsınız.", "无法在这里使用。", "Нельзя использовать здесь.", "No puedes usar eso aquí.", "Das kann hier nicht benutzt werden.", "Não pode usar isso aqui."
            },
            [LocalizationTables.GameplayKeys.NothingInReach] = new[]
            {
                "Nothing in reach.", "Erişilebilir bir şey yok.", "范围内没有可互动物体。", "Ничего в зоне досягаемости.", "Nada al alcance.", "Nichts in Reichweite.", "Nada ao alcance."
            },
            [LocalizationTables.GameplayKeys.CannotDropItem] = new[]
            {
                "You cannot drop that item.", "Bu eşyayı bırakamazsınız.", "无法丢弃该物品。", "Нельзя выбросить этот предмет.", "No puedes soltar ese objeto.", "Dieser Gegenstand kann nicht abgelegt werden.", "Não pode largar esse item."
            },
            [LocalizationTables.GameplayKeys.DroppedItem] = new[]
            {
                "Dropped {item}.", "{item} bırakıldı.", "已丢弃 {item}。", "Выброшено: {item}.", "Soltaste {item}.", "{item} abgelegt.", "Largou {item}."
            },
            [LocalizationTables.GameplayKeys.InventoryFull] = new[]
            {
                "Your inventory is full.", "Envanteriniz dolu.", "背包已满。", "Инвентарь полон.", "El inventario está lleno.", "Inventar ist voll.", "O inventário está cheio."
            },
            [LocalizationTables.GameplayKeys.PickedUpItem] = new[]
            {
                "Picked up {item}.", "{item} alındı.", "拾取了 {item}。", "Подобрано: {item}.", "Recogiste {item}.", "{item} aufgenommen.", "Pegou {item}."
            },
            [LocalizationTables.GameplayKeys.NothingToStore] = new[]
            {
                "You have nothing to store.", "Depolanacak bir şeyiniz yok.", "没有可存放的物品。", "Нечего положить на хранение.", "No tienes nada que guardar.", "Nichts zum Einlagern.", "Nada para guardar."
            },
            [LocalizationTables.GameplayKeys.StoredItems] = new[]
            {
                "Stored: {names}.", "Depolandı: {names}.", "已存放：{names}。", "Сохранено: {names}.", "Guardado: {names}.", "Eingelagert: {names}.", "Armazenado: {names}."
            },
            [LocalizationTables.GameplayKeys.InteractionNotConfigured] = new[]
            {
                "This interaction is not configured.", "Bu etkileşim yapılandırılmamış.", "此交互未配置。", "Взаимодействие не настроено.", "Esta interacción no está configurada.", "Interaktion nicht konfiguriert.", "Esta interação não está configurada."
            },
            [LocalizationTables.GameplayKeys.ItemWasUsed] = new[]
            {
                "{item} was used.", "{item} kullanıldı.", "已使用 {item}。", "Использовано: {item}.", "Se usó {item}.", "{item} wurde benutzt.", "{item} foi usado."
            },
            [LocalizationTables.GameplayKeys.NeedItem] = new[]
            {
                "You need {item}.", "{item} gerekiyor.", "需要 {item}。", "Нужно: {item}.", "Necesitas {item}.", "Du brauchst {item}.", "Você precisa de {item}."
            },
            [LocalizationTables.GameplayKeys.SlotNotConfigured] = new[]
            {
                "This slot is not configured.", "Bu yuva yapılandırılmamış.", "此槽位未配置。", "Слот не настроен.", "Esta ranura no está configurada.", "Slot nicht konfiguriert.", "Este slot não está configurado."
            },
            [LocalizationTables.GameplayKeys.PartAlreadyInstalled] = new[]
            {
                "{item} is already installed.", "{item} zaten takılı.", "{item} 已安装。", "{item} уже установлена.", "{item} ya está instalada.", "{item} ist bereits eingebaut.", "{item} já está instalada."
            },
            [LocalizationTables.GameplayKeys.SelectCarPartFirst] = new[]
            {
                "Select a car part from your inventory first.", "Önce envanterden bir araba parçası seçin.", "请先从背包选择汽车零件。", "Сначала выберите деталь в инвентаре.", "Primero elige una pieza del coche en el inventario.", "Wähle zuerst ein Autoteil im Inventar.", "Selecione uma peça do carro no inventário primeiro."
            },
            [LocalizationTables.GameplayKeys.PartDoesNotFit] = new[]
            {
                "{selected} does not fit here. You need {required}.",
                "{selected} buraya uymuyor. {required} gerekiyor.",
                "{selected} 无法安装在这里。需要 {required}。",
                "{selected} не подходит. Нужно: {required}.",
                "{selected} no encaja aquí. Necesitas {required}.",
                "{selected} passt nicht. Du brauchst {required}.",
                "{selected} não serve aqui. Você precisa de {required}."
            },
            [LocalizationTables.GameplayKeys.DoNotHaveItem] = new[]
            {
                "You do not have {item}.", "{item} yok.", "你没有 {item}。", "У вас нет {item}.", "No tienes {item}.", "Du hast {item} nicht.", "Você não tem {item}."
            },
            [LocalizationTables.GameplayKeys.PartInstalled] = new[]
            {
                "{item} installed.", "{item} takıldı.", "已安装 {item}。", "Установлено: {item}.", "{item} instalada.", "{item} eingebaut.", "{item} instalada."
            },
            [LocalizationTables.GameplayKeys.EscapeNotSetup] = new[]
            {
                "Escape sequence is not set up.", "Kaçış sekansı ayarlanmamış.", "逃脱流程未配置。", "Побег не настроен.", "La secuencia de escape no está configurada.", "Fluchtsequenz nicht eingerichtet.", "Sequência de fuga não configurada."
            },
            [LocalizationTables.GameplayKeys.AllPartsEscaped] = new[]
            {
                "All parts installed. You escaped!", "Tüm parçalar takıldı. Kaçtınız!", "所有零件已安装。你逃出来了！", "Все детали установлены. Вы сбежали!", "¡Todas las piezas instaladas. Escapaste!", "Alle Teile eingebaut. Du bist entkommen!", "Todas as peças instaladas. Você escapou!"
            },
            [LocalizationTables.GameplayKeys.StillMissingParts] = new[]
            {
                "Still missing: {list}.", "Eksik: {list}.", "仍缺少：{list}。", "Не хватает: {list}.", "Faltan: {list}.", "Fehlt noch: {list}.", "Ainda faltam: {list}."
            },
            [LocalizationTables.GameplayKeys.DoorLocked] = new[]
            {
                "The door is locked.", "Kapı kilitli.", "门已锁上。", "Дверь заперта.", "La puerta está cerrada.", "Die Tür ist verschlossen.", "A porta está trancada."
            },
            [LocalizationTables.GameplayKeys.ItemCannotUseHere] = new[]
            {
                "This item cannot be used here.", "Bu eşya burada kullanılamaz.", "此物品无法在这里使用。", "Этот предмет нельзя использовать здесь.", "Este objeto no se puede usar aquí.", "Dieser Gegenstand kann hier nicht benutzt werden.", "Este item não pode ser usado aqui."
            },
            [LocalizationTables.GameplayKeys.DoNotHaveThatItem] = new[]
            {
                "You do not have that item.", "Bu eşyaya sahip değilsiniz.", "你没有该物品。", "У вас нет этого предмета.", "No tienes ese objeto.", "Du hast diesen Gegenstand nicht.", "Você não tem esse item."
            },
            [LocalizationTables.GameplayKeys.ItemUnlockedDoor] = new[]
            {
                "{item} unlocked it.", "{item} kilidi açtı.", "{item} 打开了锁。", "{item} открыл замок.", "{item} abrió la cerradura.", "{item} hat es geöffnet.", "{item} destrancou."
            },
            [LocalizationTables.GameplayKeys.TipHide] = new[]
            {
                "TIP: Use hiding spots to escape enemies.",
                "İPUCU: Saklanma noktalarını kullanarak düşmanlardan kaçabilirsiniz.",
                "提示：利用藏身点躲避敌人。",
                "СОВЕТ: Используйте укрытия, чтобы спрятаться от врагов.",
                "CONSEJO: Usa escondites para escapar de los enemigos.",
                "TIPP: Nutze Verstecke, um Feinden zu entkommen.",
                "DICA: Use esconderijos para fugir dos inimigos."
            },
            [LocalizationTables.GameplayKeys.TipExplore] = new[]
            {
                "TIP: Explore carefully — you may find important clues.",
                "İPUCU: Etrafı dikkatli araştırın, önemli ipuçları bulabilirsiniz.",
                "提示：仔细探索，可能发现重要线索。",
                "СОВЕТ: Исследуйте внимательно — можно найти важные подсказки.",
                "CONSEJO: Explora con cuidado; puedes encontrar pistas importantes.",
                "TIPP: Erkunde gründlich — wichtige Hinweise warten.",
                "DICA: Explore com cuidado — você pode achar pistas importantes."
            },
            [LocalizationTables.GameplayKeys.TipSound] = new[]
            {
                "TIP: Stay quiet — enemies can hear you.",
                "İPUCU: Sesinizi kısık tutun, düşmanlar sizi duyabilir.",
                "提示：保持安静，敌人会听到你。",
                "СОВЕТ: Будьте тихи — враги вас услышат.",
                "CONSEJO: Mantente en silencio; los enemigos te oyen.",
                "TIPP: Sei leise — Feinde hören dich.",
                "DICA: Fique em silêncio — os inimigos podem ouvir você."
            },
            [LocalizationTables.GameplayKeys.TipFlashlight] = new[]
            {
                "TIP: Your flashlight has limited battery — use it wisely.",
                "İPUCU: El feneriniz sınırlı pile sahip, dikkatli kullanın.",
                "提示：手电筒电量有限，请谨慎使用。",
                "СОВЕТ: У фонарика ограничен заряд — берегите батарею.",
                "CONSEJO: La linterna tiene poca batería; úsala con cuidado.",
                "TIPP: Die Taschenlampe hat begrenzte Batterie.",
                "DICA: A lanterna tem bateria limitada — use com cuidado."
            },
            [LocalizationTables.GameplayKeys.TipKeys] = new[]
            {
                "TIP: Some doors require special keys.",
                "İPUCU: Bazı kapılar özel anahtarlar gerektirir.",
                "提示：有些门需要特殊钥匙。",
                "СОВЕТ: Некоторые двери требуют особых ключей.",
                "CONSEJO: Algunas puertas necesitan llaves especiales.",
                "TIPP: Manche Türen brauchen besondere Schlüssel.",
                "DICA: Algumas portas exigem chaves especiais."
            },
            [LocalizationTables.GameplayKeys.TipRun] = new[]
            {
                "TIP: Running may attract enemies.",
                "İPUCU: Koşmak düşmanların dikkatini çekebilir.",
                "提示：奔跑可能引起敌人注意。",
                "СОВЕТ: Бег может привлечь врагов.",
                "CONSEJO: Correr puede atraer enemigos.",
                "TIPP: Rennen kann Feinde aufmerksam machen.",
                "DICA: Correr pode atrair inimigos."
            },
            [LocalizationTables.GameplayKeys.TipNotes] = new[]
            {
                "TIP: Read notes and documents to uncover the story.",
                "İPUCU: Notları ve dökümanları okuyarak hikayeyi keşfedin.",
                "提示：阅读笔记和文件以了解剧情。",
                "СОВЕТ: Читайте записки и документы, чтобы узнать историю.",
                "CONSEJO: Lee notas y documentos para descubrir la historia.",
                "TIPP: Lies Notizen und Dokumente für die Geschichte.",
                "DICA: Leia notas e documentos para descobrir a história."
            },
            [LocalizationTables.GameplayKeys.TipDark] = new[]
            {
                "TIP: Dark corners can help you stay hidden.",
                "İPUCU: Karanlık köşelerde saklanmak düşmanlardan korunmanıza yardımcı olur.",
                "提示：躲在暗处有助于隐蔽。",
                "СОВЕТ: Тёмные углы помогают оставаться незамеченным.",
                "CONSEJO: Las esquinas oscuras ayudan a permanecer oculto.",
                "TIPP: Dunkle Ecken helfen beim Verstecken.",
                "DICA: Cantos escuros ajudam a ficar escondido."
            },
        };
}
