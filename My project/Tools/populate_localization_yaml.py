#!/usr/bin/env python3
"""Populate Unity Localization string table YAML assets (run from project root)."""

from __future__ import annotations
import os
from pathlib import Path

LOCALES = ["en", "tr", "zh", "ru", "es", "de", "pt-BR"]
LOCALE_FILE_SUFFIX = {
    "en": "en",
    "tr": "tr",
    "zh": "zh",
    "ru": "ru",
    "es": "es",
    "de": "de",
    "pt-BR": "pt-BR",
}

MAIN_MENU = {
    "btn_continue": ["Continue", "Devam Et", "继续", "Продолжить", "Continuar", "Fortsetzen", "Continuar"],
    "btn_new_game": ["New Game", "Yeni Oyun", "新游戏", "Новая игра", "Nueva partida", "Neues Spiel", "Novo jogo"],
    "btn_settings": ["Settings", "Ayarlar", "设置", "Настройки", "Ajustes", "Einstellungen", "Configurações"],
    "btn_credits": ["Credits", "Jenerik", "制作人员", "Титры", "Créditos", "Credits", "Créditos"],
    "btn_quit": ["Quit Game", "Oyundan Çık", "退出游戏", "Выйти из игры", "Salir del juego", "Spiel beenden", "Sair do jogo"],
    "settings_display_mode": ["Display Mode", "Görüntü Modu", "显示模式", "Режим экрана", "Modo de pantalla", "Anzeigemodus", "Modo de exibição"],
    "settings_brightness": ["Brightness", "Parlaklık", "亮度", "Яркость", "Brillo", "Helligkeit", "Brilho"],
    "settings_master_volume": ["Master Volume", "Ana Ses", "主音量", "Общая громкость", "Volumen general", "Gesamtlautstärke", "Volume principal"],
    "settings_mouse_sensitivity": ["Mouse Sensitivity", "Fare Hassasiyeti", "鼠标灵敏度", "Чувствительность мыши", "Sensibilidad del ratón", "Mausempfindlichkeit", "Sensibilidade do mouse"],
    "settings_vsync": ["VSync", "VSync", "垂直同步", "VSync", "VSync", "VSync", "VSync"],
    "settings_autosave": ["Auto-Save", "Otomatik Kayıt", "自动保存", "Автосохранение", "Autoguardado", "Autom. Speichern", "Salvamento automático"],
    "settings_choose_value": ["Choose Value", "Değer Seç", "选择数值", "Выберите значение", "Elegir valor", "Wert wählen", "Escolher valor"],
    "settings_language": ["Language", "Dil", "语言", "Язык", "Idioma", "Sprache", "Idioma"],
    "dialog_default_title": ["Confirm", "Onay", "确认", "Подтверждение", "Confirmar", "Bestätigen", "Confirmar"],
    "dialog_yes": ["Yes", "Evet", "是", "Да", "Sí", "Ja", "Sim"],
    "dialog_no": ["No", "Hayır", "否", "Нет", "No", "Nein", "Não"],
    "dialog_quit_message": [
        "Are you sure you want to quit?\nUnsaved progress will be lost.",
        "Oyundan çıkmak istediğinize emin misiniz?\nKaydedilmemiş ilerleme kaybolacak.",
        "确定要退出游戏吗？\n未保存的进度将丢失。",
        "Выйти из игры?\nНесохранённый прогресс будет потерян.",
        "¿Seguro que quieres salir?\nSe perderá el progreso no guardado.",
        "Spiel wirklich beenden?\nUngespeicherter Fortschritt geht verloren.",
        "Sair do jogo?\nO progresso não salvo será perdido.",
    ],
    "dialog_quit_title": ["Quit", "Çıkış", "退出", "Выход", "Salir", "Beenden", "Sair"],
    "dialog_quit_confirm": ["Quit", "Çık", "退出", "Выйти", "Salir", "Beenden", "Sair"],
    "dialog_return_menu_message": [
        "Return to the main menu?\nUnsaved progress will be lost.",
        "Ana menüye dönmek istediğinize emin misiniz?\nKaydedilmemiş ilerleme kaybolacak.",
        "返回主菜单？\n未保存的进度将丢失。",
        "Вернуться в главное меню?\nНесохранённый прогресс будет потерян.",
        "¿Volver al menú principal?\nSe perderá el progreso no guardado.",
        "Zum Hauptmenü zurück?\nUngespeicherter Fortschritt geht verloren.",
        "Voltar ao menu principal?\nO progresso não salvo será perdido.",
    ],
    "dialog_return_menu_title": ["Main Menu", "Ana Menü", "主菜单", "Главное меню", "Menú principal", "Hauptmenü", "Menu principal"],
    "dialog_return_menu_confirm": ["Return", "Dön", "返回", "Вернуться", "Volver", "Zurück", "Voltar"],
    "loading_status": ["Loading...", "Yükleniyor...", "加载中...", "Загрузка...", "Cargando...", "Lädt...", "Carregando..."],
}

GAMEPLAY = {
    "hide_wait": ["Wait a moment.", "Biraz bekleyin.", "请稍候。", "Подождите немного.", "Espera un momento.", "Einen Moment warten.", "Aguarde um momento."],
    "hide_already": ["You are already hiding.", "Zaten saklanıyorsunuz.", "你已经在躲藏了。", "Вы уже прячетесь.", "Ya te estás escondiendo.", "Du versteckst dich bereits.", "Você já está se escondendo."],
    "cannot_use_here": ["You cannot use that here.", "Bunu burada kullanamazsınız.", "无法在这里使用。", "Нельзя использовать здесь.", "No puedes usar eso aquí.", "Das kann hier nicht benutzt werden.", "Não pode usar isso aqui."],
    "nothing_in_reach": ["Nothing in reach.", "Erişilebilir bir şey yok.", "范围内没有可互动物体。", "Ничего в зоне досягаемости.", "Nada al alcance.", "Nichts in Reichweite.", "Nada ao alcance."],
    "cannot_drop_item": ["You cannot drop that item.", "Bu eşyayı bırakamazsınız.", "无法丢弃该物品。", "Нельзя выбросить этот предмет.", "No puedes soltar ese objeto.", "Dieser Gegenstand kann nicht abgelegt werden.", "Não pode largar esse item."],
    "dropped_item": ["Dropped {item}.", "{item} bırakıldı.", "已丢弃 {item}。", "Выброшено: {item}.", "Soltaste {item}.", "{item} abgelegt.", "Largou {item}."],
    "inventory_full": ["Your inventory is full.", "Envanteriniz dolu.", "背包已满。", "Инвентарь полон.", "El inventario está lleno.", "Inventar ist voll.", "O inventário está cheio."],
    "picked_up_item": ["Picked up {item}.", "{item} alındı.", "拾取了 {item}。", "Подобрано: {item}.", "Recogiste {item}.", "{item} aufgenommen.", "Pegou {item}."],
    "nothing_to_store": ["You have nothing to store.", "Depolanacak bir şeyiniz yok.", "没有可存放的物品。", "Нечего положить на хранение.", "No tienes nada que guardar.", "Nichts zum Einlagern.", "Nada para guardar."],
    "stored_items": ["Stored: {names}.", "Depolandı: {names}.", "已存放：{names}。", "Сохранено: {names}.", "Guardado: {names}.", "Eingelagert: {names}.", "Armazenado: {names}."],
    "interaction_not_configured": ["This interaction is not configured.", "Bu etkileşim yapılandırılmamış.", "此交互未配置。", "Взаимодействие не настроено.", "Esta interacción no está configurada.", "Interaktion nicht konfiguriert.", "Esta interação não está configurada."],
    "item_was_used": ["{item} was used.", "{item} kullanıldı.", "已使用 {item}。", "Использовано: {item}.", "Se usó {item}.", "{item} wurde benutzt.", "{item} foi usado."],
    "need_item": ["You need {item}.", "{item} gerekiyor.", "需要 {item}。", "Нужно: {item}.", "Necesitas {item}.", "Du brauchst {item}.", "Você precisa de {item}."],
    "slot_not_configured": ["This slot is not configured.", "Bu yuva yapılandırılmamış.", "此槽位未配置。", "Слот не настроен.", "Esta ranura no está configurada.", "Slot nicht konfiguriert.", "Este slot não está configurado."],
    "part_already_installed": ["{item} is already installed.", "{item} zaten takılı.", "{item} 已安装。", "{item} уже установлена.", "{item} ya está instalada.", "{item} ist bereits eingebaut.", "{item} já está instalada."],
    "select_car_part_first": ["Select a car part from your inventory first.", "Önce envanterden bir araba parçası seçin.", "请先从背包选择汽车零件。", "Сначала выберите деталь в инвентаре.", "Primero elige una pieza del coche en el inventario.", "Wähle zuerst ein Autoteil im Inventar.", "Selecione uma peça do carro no inventário primeiro."],
    "part_does_not_fit": [
        "{selected} does not fit here. You need {required}.",
        "{selected} buraya uymuyor. {required} gerekiyor.",
        "{selected} 无法安装在这里。需要 {required}。",
        "{selected} не подходит. Нужно: {required}.",
        "{selected} no encaja aquí. Necesitas {required}.",
        "{selected} passt nicht. Du brauchst {required}.",
        "{selected} não serve aqui. Você precisa de {required}.",
    ],
    "do_not_have_item": ["You do not have {item}.", "{item} yok.", "你没有 {item}。", "У вас нет {item}.", "No tienes {item}.", "Du hast {item} nicht.", "Você não tem {item}."],
    "part_installed": ["{item} installed.", "{item} takıldı.", "已安装 {item}。", "Установлено: {item}.", "{item} instalada.", "{item} eingebaut.", "{item} instalada."],
    "escape_not_setup": ["Escape sequence is not set up.", "Kaçış sekansı ayarlanmamış.", "逃脱流程未配置。", "Побег не настроен.", "La secuencia de escape no está configurada.", "Fluchtsequenz nicht eingerichtet.", "Sequência de fuga não configurada."],
    "all_parts_escaped": ["All parts installed. You escaped!", "Tüm parçalar takıldı. Kaçtınız!", "所有零件已安装。你逃出来了！", "Все детали установлены. Вы сбежали!", "¡Todas las piezas instaladas. Escapaste!", "Alle Teile eingebaut. Du bist entkommen!", "Todas as peças instaladas. Você escapou!"],
    "still_missing_parts": ["Still missing: {list}.", "Eksik: {list}.", "仍缺少：{list}。", "Не хватает: {list}.", "Faltan: {list}.", "Fehlt noch: {list}.", "Ainda faltam: {list}."],
    "door_locked": ["The door is locked.", "Kapı kilitli.", "门已锁上。", "Дверь заперта.", "La puerta está cerrada.", "Die Tür ist verschlossen.", "A porta está trancada."],
    "item_cannot_use_here": ["This item cannot be used here.", "Bu eşya burada kullanılamaz.", "此物品无法在这里使用。", "Этот предмет нельзя использовать здесь.", "Este objeto no se puede usar aquí.", "Dieser Gegenstand kann hier nicht benutzt werden.", "Este item não pode ser usado aqui."],
    "do_not_have_that_item": ["You do not have that item.", "Bu eşyaya sahip değilsiniz.", "你没有该物品。", "У вас нет этого предмета.", "No tienes ese objeto.", "Du hast diesen Gegenstand nicht.", "Você não tem esse item."],
    "item_unlocked_door": ["{item} unlocked it.", "{item} kilidi açtı.", "{item} 打开了锁。", "{item} открыл замок.", "{item} abrió la cerradura.", "{item} hat es geöffnet.", "{item} destrancou."],
    "tip_hide": ["TIP: Use hiding spots to escape enemies.", "İPUCU: Saklanma noktalarını kullanarak düşmanlardan kaçabilirsiniz.", "提示：利用藏身点躲避敌人。", "СОВЕТ: Используйте укрытия, чтобы спрятаться от врагов.", "CONSEJO: Usa escondites para escapar de los enemigos.", "TIPP: Nutze Verstecke, um Feinden zu entkommen.", "DICA: Use esconderijos para fugir dos inimigos."],
    "tip_explore": ["TIP: Explore carefully — you may find important clues.", "İPUCU: Etrafı dikkatli araştırın, önemli ipuçları bulabilirsiniz.", "提示：仔细探索，可能发现重要线索。", "СОВЕТ: Исследуйте внимательно — можно найти важные подсказки.", "CONSEJO: Explora con cuidado; puedes encontrar pistas importantes.", "TIPP: Erkunde gründlich — wichtige Hinweise warten.", "DICA: Explore com cuidado — você pode achar pistas importantes."],
    "tip_sound": ["TIP: Stay quiet — enemies can hear you.", "İPUCU: Sesinizi kısık tutun, düşmanlar sizi duyabilir.", "提示：保持安静，敌人会听到你。", "СОВЕТ: Будьте тихи — враги вас услышат.", "CONSEJO: Mantente en silencio; los enemigos te oyen.", "TIPP: Sei leise — Feinde hören dich.", "DICA: Fique em silêncio — os inimigos podem ouvir você."],
    "tip_flashlight": ["TIP: Your flashlight has limited battery — use it wisely.", "İPUCU: El feneriniz sınırlı pile sahip, dikkatli kullanın.", "提示：手电筒电量有限，请谨慎使用。", "СОВЕТ: У фонарика ограничен заряд — берегите батарею.", "CONSEJO: La linterna tiene poca batería; úsala con cuidado.", "TIPP: Die Taschenlampe hat begrenzte Batterie.", "DICA: A lanterna tem bateria limitada — use com cuidado."],
    "tip_keys": ["TIP: Some doors require special keys.", "İPUCU: Bazı kapılar özel anahtarlar gerektirir.", "提示：有些门需要特殊钥匙。", "СОВЕТ: Некоторые двери требуют особых ключей.", "CONSEJO: Algunas puertas necesitan llaves especiales.", "TIPP: Manche Türen brauchen besondere Schlüssel.", "DICA: Algumas portas exigem chaves especiais."],
    "tip_run": ["TIP: Running may attract enemies.", "İPUCU: Koşmak düşmanların dikkatini çekebilir.", "提示：奔跑可能引起敌人注意。", "СОВЕТ: Бег может привлечь врагов.", "CONSEJO: Correr puede atraer enemigos.", "TIPP: Rennen kann Feinde aufmerksam machen.", "DICA: Correr pode atrair inimigos."],
    "tip_notes": ["TIP: Read notes and documents to uncover the story.", "İPUCU: Notları ve dökümanları okuyarak hikayeyi keşfedin.", "提示：阅读笔记和文件以了解剧情。", "СОВЕТ: Читайте записки и документы, чтобы узнать историю.", "CONSEJO: Lee notas y documentos para descubrir la historia.", "TIPP: Lies Notizen und Dokumente für die Geschichte.", "DICA: Leia notas e documentos para descobrir a história."],
    "tip_dark": ["TIP: Dark corners can help you stay hidden.", "İPUCU: Karanlık köşelerde saklanmak düşmanlardan korunmanıza yardımcı olur.", "提示：躲在暗处有助于隐蔽。", "СОВЕТ: Тёмные углы помогают оставаться незамеченным.", "CONSEJO: Las esquinas oscuras ayudan a permanecer oculto.", "TIPP: Dunkle Ecken helfen beim Verstecken.", "DICA: Cantos escuros ajudam a ficar escondido."],
}

SHARED_GUID = {
    "Main Menu": "fb191b04a216d438ea6cb7b142ddd4ef",
    "Gameplay": "a1b2c3d4e5f6478990a1b2c3d4e5f678",
}


def key_id(key: str, salt: int = 0) -> int:
    h = salt
    for ch in key:
        h = (h * 31 + ord(ch)) & 0xFFFFFFFFFFFFFFFF
    return (h % 900_000_000_000_000) + 100_000_000_000_000


def yaml_escape(s: str) -> str:
    # Always quote: {placeholders} and ':' break Unity's YAML parser if unquoted.
    s = s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n")
    return f'"{s}"'


def write_shared(path: Path, collection_name: str, keys: list[str], guid: str):
    entries = []
    for k in keys:
        kid = key_id(k)
        entries.append(f"  - m_Id: {kid}\n    m_Key: {k}\n    m_Metadata:\n      m_Items: []")
    content = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 5b11a58205ec3474ca216360e9fa74a8, type: 3}}
  m_Name: {collection_name} Shared Data
  m_EditorClassIdentifier: 
  m_TableCollectionName: {collection_name}
  m_TableCollectionNameGuidString: {guid}
  m_Entries:
{chr(10).join(entries)}
  m_Metadata:
    m_Items: []
  m_KeyGenerator:
    rid: 1491214261936455969
  references:
    version: 2
    RefIds:
    - rid: 1491214261936455969
      type: {{class: DistributedUIDGenerator, ns: UnityEngine.Localization.Tables, asm: Unity.Localization}}
      data:
        m_CustomEpoch: 1779482999953
"""
    path.write_text(content, encoding="utf-8")


def write_locale_table(path: Path, collection_name: str, locale: str, keys: list[str], rows: dict, shared_guid: str):
    loc_idx = LOCALES.index(locale)
    table_rows = []
    for k in keys:
        kid = key_id(k)
        text = yaml_escape(rows[k][loc_idx])
        table_rows.append(f"  - m_Id: {kid}\n    m_Localized: {text}\n    m_Metadata:\n      m_Items: []")
    suffix = LOCALE_FILE_SUFFIX[locale]
    content = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: e9620f8c34305754d8cc9a7e49e852d9, type: 3}}
  m_Name: {collection_name}_{suffix}
  m_EditorClassIdentifier: 
  m_LocaleId:
    m_Code: {locale}
  m_SharedData: {{fileID: 11400000, guid: {shared_guid}, type: 2}}
  m_Metadata:
    m_Items: []
  m_TableData:
{chr(10).join(table_rows)}
  references:
    version: 2
    RefIds: []
"""
    path.write_text(content, encoding="utf-8")


def write_collection_asset(path: Path, collection_name: str, table_guids: list[str]):
    tables_yaml = "\n".join(f"  - {{fileID: 11400000, guid: {g}, type: 2}}" for g in table_guids)
    content = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 5be51871efa6c3e4eae1703925c8f5ac, type: 3}}
  m_Name: {collection_name}
  m_EditorClassIdentifier: 
  m_SharedTableData: {{fileID: 11400000, guid: {SHARED_GUID.get(collection_name, '')}, type: 2}}
  m_Tables:
{tables_yaml}
  m_Extensions: []
  m_Group: String Table
  references:
    version: 2
    RefIds: []
"""
    path.write_text(content, encoding="utf-8")


def main():
    root = Path(__file__).resolve().parents[1]
    lang = root / "Assets" / "Languages"

    # Main Menu
    mm_keys = list(MAIN_MENU.keys())
    write_shared(lang / "Main Menu Shared Data.asset", "Main Menu", mm_keys, SHARED_GUID["Main Menu"])
    table_guids = []
    for loc in LOCALES:
        p = lang / f"Main Menu_{LOCALE_FILE_SUFFIX[loc]}.asset"
        write_locale_table(p, "Main Menu", loc, mm_keys, MAIN_MENU, SHARED_GUID["Main Menu"])
        meta = lang / f"Main Menu_{LOCALE_FILE_SUFFIX[loc]}.asset.meta"
        if meta.exists():
            for line in meta.read_text().splitlines():
                if line.startswith("guid:"):
                    table_guids.append(line.split()[1])
                    break
    write_collection_asset(lang / "Main Menu.asset", "Main Menu", table_guids)

    # Gameplay - create new shared guid (generate stable)
    gp_shared_guid = "a1b2c3d4e5f6478990a1b2c3d4e5f678"
    gp_keys = list(GAMEPLAY.keys())
    write_shared(lang / "Gameplay Shared Data.asset", "Gameplay", gp_keys, gp_shared_guid)

    gp_table_guids = []
    for loc in LOCALES:
        suffix = LOCALE_FILE_SUFFIX[loc]
        p = lang / f"Gameplay_{suffix}.asset"
        write_locale_table(p, "Gameplay", loc, gp_keys, GAMEPLAY, gp_shared_guid)
        meta_path = lang / f"Gameplay_{suffix}.asset.meta"
        if not meta_path.exists():
            import uuid
            g = uuid.uuid4().hex
            meta_path.write_text(f"fileFormatVersion: 2\nguid: {g}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
            gp_table_guids.append(g)
        else:
            for line in meta_path.read_text().splitlines():
                if line.startswith("guid:"):
                    gp_table_guids.append(line.split()[1])
                    break

    coll_meta = lang / "Gameplay.asset.meta"
    if not coll_meta.exists():
        import uuid
        coll_guid = uuid.uuid4().hex
        coll_meta.write_text(f"fileFormatVersion: 2\nguid: {coll_guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    shared_meta = lang / "Gameplay Shared Data.asset.meta"
    if not shared_meta.exists():
        import uuid
        shared_meta.write_text(f"fileFormatVersion: 2\nguid: {gp_shared_guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")

    write_collection_asset(lang / "Gameplay.asset", "Gameplay", gp_table_guids)
    print("Localization YAML populated.")

if __name__ == "__main__":
    main()
