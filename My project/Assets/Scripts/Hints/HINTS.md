# Gameplay hints

All transient hints live under **`_UI > Gameplay Hint Box`** and are driven by **`GameplayHintManager`**.

## Panel registration

Each hint is a child panel with a **`CanvasGroup`**. Register it in the manager’s **Panels** list:

| Id | Prefab / object | Trigger |
|----|-----------------|---------|
| `Hiding Spot` | Hiding Spot Hint | First cage door open |
| `Sound` | Sound Hint | First Shift press |
| `Note` | Note Hint (nested prefab) | Every 120s |
| `Drop Item` | Drop Item Hint (nested prefab) | Vase pickup timer |

**Id** must match the string used in code (`Show("Drop Item")`, etc.).

Do not rely on runtime child discovery or auto-registration — wire panels explicitly in the inspector.

## Drop Item hint

**Prefab:** `Assets/Prefabs/UI/Drop Item Hint.prefab`  
**Nested under:** `Gameplay Hint Box` in `_UI.prefab`

### Hierarchy (authored manually)

```
Drop Item Hint          ← CanvasGroup, DropItemHintLabel
└── Hint Row            ← HorizontalLayoutGroup
    ├── Prefix Text     ← TMP + ContentSizeFitter + LayoutElement
    ├── G Key           ← key cap image (150×150, scale 0.5) — do not resize in code
    │   └── Text (TMP)  ← localized key letter
    └── Suffix Text     ← TMP + ContentSizeFitter + LayoutElement
```

Child order matters: Prefix → G Key → Suffix.

### Localization

- **`hint.drop_item`** — full sentence with `{0}` where the key badge sits  
  - EN: `Press {0} to drop selected item`  
  - JA: `{0}を押すと選択中のアイテムを落とします`
- **`controls.key.drop`** — letter shown inside the badge (e.g. `G`, `け`)

`DropItemHintLabel` only splits the string, updates text, hides empty prefix/suffix segments, and rebuilds the authored `Hint Row` layout after locale changes. It does **not** build or reparent UI at runtime.

**Do not** add a second Drop Item Hint object in the scene — use only the nested prefab under `_UI`. A leftover scene copy (from old runtime generation) will sit on top with broken spacing.

### Vase trigger (GameplayHintManager)

1. First vase pickup → wait **60s** → show hint  
2. Repeat every **60s** while vase is held  
3. When vase is dropped → hide and never show again  

Assign **Vase** item asset on `GameplayHintManager`.
