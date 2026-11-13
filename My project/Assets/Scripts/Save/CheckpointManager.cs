using System.Collections;
using System.Collections.Generic;
using FS_Atmo;
using TMPro;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Auto-save for the gameplay scene. Saves a <see cref="Checkpoint"/> when the bar behind the man at the wall
/// (<see cref="CorpseVanishEvent"/>) is picked up, again once the escape door is shut on him after the chase and
/// the hide shot is over (<see cref="WallStanderChase"/>), and again as the player steps out of the basement, just
/// before the leave-basement car shot (<see cref="ButcherWindowShot"/>). On a load that asked for it (capture retry, main
/// menu Continue) puts the fresh scene back the way it was: the corpse scare finished, the bar in hand with him waiting
/// at the wall (or pounding on the shut door), items taken or dropped, doors, padlocks, lights, inventory, the player
/// where they stood. The butcher starts his day over; loaded outside the basement, the car shot plays again.
/// Installs itself in any scene with a CorpseVanishEvent. Loading that scene without a pending restore starts a new
/// run, so the old checkpoint is deleted.
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public class CheckpointManager : MonoBehaviour
{
    const string ObjectName = "Checkpoint Manager";
    const string IndicatorKey = "ui.settings.auto_save";

    [Header("Auto-save indicator")]
    [SerializeField] float indicatorFadeIn = 0.35f;
    [SerializeField] float indicatorHold = 1.8f;
    [SerializeField] float indicatorFadeOut = 0.6f;
    [SerializeField] float indicatorFontSize = 30f;
    [SerializeField] Color indicatorColor = new Color(0.92f, 0.9f, 0.88f, 1f);

    struct TrackedItem
    {
        public InteractableItem item;
        public string id;
        public bool startedActive;
    }

    readonly Dictionary<Component, string> _ids = new Dictionary<Component, string>();
    readonly List<TrackedItem> _items = new List<TrackedItem>();
    readonly List<SimpleOpenClose> _doors = new List<SimpleOpenClose>();
    readonly List<LightSwitch> _switches = new List<LightSwitch>();
    readonly List<LightableCandle> _candles = new List<LightableCandle>();
    readonly List<PadlockLockpickMinigame> _padlocks = new List<PadlockLockpickMinigame>();
    readonly Dictionary<Scene, GameObject[]> _roots = new Dictionary<Scene, GameObject[]>();

    CorpseVanishEvent _corpse;
    WallStanderChase _chase;
    ButcherWindowShot _windowShot;
    CanvasGroup _indicator;
    Coroutine _indicatorRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

    static void Install()
    {
        if (FindFirstObjectByType<CorpseVanishEvent>(FindObjectsInactive.Include) == null
            || FindFirstObjectByType<CheckpointManager>(FindObjectsInactive.Include) != null)
            return;
        new GameObject(ObjectName).AddComponent<CheckpointManager>();
    }

    void Awake()
    {
        if (!Checkpoint.RestorePending)
            Checkpoint.Delete();
    }

    // Runs after every other Start, so objects spawned at scene start (the cage padlock) are tracked too, and before
    // the first Update, so nothing has moved yet.
    void Start()
    {
        TrackSceneObjects();

        _corpse = FindFirstObjectByType<CorpseVanishEvent>(FindObjectsInactive.Include);
        if (_corpse != null)
            _corpse.BarPickedUp += Save;
        _chase = FindFirstObjectByType<WallStanderChase>(FindObjectsInactive.Include);
        if (_chase != null)
            _chase.ShutOut += Save;
        _windowShot = FindFirstObjectByType<ButcherWindowShot>(FindObjectsInactive.Include);
        if (_windowShot != null)
            _windowShot.Triggered += Save;

        if (Checkpoint.RestorePending)
        {
            if (Checkpoint.TryRead(out CheckpointData data) && data.scene == gameObject.scene.name)
                Restore(data);
            else
                Debug.LogWarning("[Checkpoint] Restore requested but the save is missing or from another scene.");
        }
        Checkpoint.CancelRestore();
    }

    void OnDestroy()
    {
        if (_corpse != null)
            _corpse.BarPickedUp -= Save;
        if (_chase != null)
            _chase.ShutOut -= Save;
        if (_windowShot != null)
            _windowShot.Triggered -= Save;
    }

    void Save()
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured) || JumpscareManager.CaptureUiActive)
            return;
        if (Checkpoint.Write(Capture()))
            ShowIndicator();
    }

    // ───────────────────────── IDs ─────────────────────────

    void TrackSceneObjects()
    {
        foreach (InteractableItem item in FindObjectsByType<InteractableItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!item.gameObject.scene.IsValid())
                continue;
            _items.Add(new TrackedItem { item = item, id = Track(item), startedActive = item.gameObject.activeSelf });
        }
        TrackAll(_doors);
        TrackAll(_switches);
        TrackAll(_candles);
        TrackAll(_padlocks);
        _roots.Clear();
    }

    void TrackAll<T>(List<T> list) where T : Component
    {
        foreach (T c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.gameObject.scene.IsValid())
                continue;
            list.Add(c);
            Track(c);
        }
    }

    // The object's place in the hierarchy (names, with same-named earlier siblings counted) and which component of its
    // type it is. Taken once at scene start, so it stays put however objects get destroyed or spawned later.
    string Track(Component c)
    {
        var parts = new List<string>();
        for (Transform t = c.transform; t != null; t = t.parent)
            parts.Add(t.name + "#" + SameNameOrdinal(t));
        parts.Reverse();

        int index = System.Array.IndexOf(c.GetComponents(c.GetType()), c);
        string id = string.Join("/", parts) + ":" + c.GetType().Name + (index > 0 ? "[" + index + "]" : "");
        _ids[c] = id;
        return id;
    }

    int SameNameOrdinal(Transform t)
    {
        int n = 0;
        if (t.parent != null)
        {
            int index = t.GetSiblingIndex();
            for (int i = 0; i < index; i++)
                if (t.parent.GetChild(i).name == t.name)
                    n++;
            return n;
        }

        Scene scene = t.gameObject.scene;
        if (!_roots.TryGetValue(scene, out GameObject[] roots))
            _roots[scene] = roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            if (root.transform == t)
                break;
            if (root.name == t.name)
                n++;
        }
        return n;
    }

    string IdOf(Component c) => c != null && _ids.TryGetValue(c, out string id) ? id : null;

    // ───────────────────────── Save ─────────────────────────

    CheckpointData Capture()
    {
        var d = new CheckpointData
        {
            id = System.Guid.NewGuid().ToString("N"),
            scene = gameObject.scene.name,
        };

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            d.playerPosition = player.transform.position;
            d.playerYaw = player.transform.eulerAngles.y;
        }
        CinemachineMouseLook look = FindFirstObjectByType<CinemachineMouseLook>();
        if (look != null)
        {
            float pitch = look.transform.localEulerAngles.x;
            d.cameraPitch = pitch > 180f ? pitch - 360f : pitch;
        }

        CaptureInventory(d);
        CaptureItems(d, player);

        foreach (SimpleOpenClose door in _doors)
        {
            if (door == null || !door.gameObject.activeInHierarchy)
                continue;
            DoorController ai = door.GetComponentInParent<DoorController>();
            d.doors.Add(new DoorRecord
            {
                id = IdOf(door),
                open = door.objectOpen,
                locked = door.isLocked,
                aiLocked = ai != null && ai.isLocked,
            });
        }
        foreach (LightSwitch s in _switches)
            if (s != null)
                d.lightSwitches.Add(new ActiveRecord { id = IdOf(s), on = s.IsOn });
        foreach (LightableCandle c in _candles)
            if (c != null)
                d.candles.Add(new ActiveRecord { id = IdOf(c), on = c.IsLit });
        foreach (PadlockLockpickMinigame p in _padlocks)
            if (p != null && !p.IsLocked)
                d.openPadlocks.Add(IdOf(p));

        d.wallStanderRevealed = _corpse != null && _corpse.WallRevealPlayed;
        d.barTaken = _corpse != null && _corpse.BarTaken;
        d.wallStanderShutOut = _chase != null && _chase.IsShutOut;
        CorridorTension corridor = FindFirstObjectByType<CorridorTension>();
        d.corridorLeft = corridor != null && corridor.HasLeftCorridor;
        TensionMusicCues music = FindFirstObjectByType<TensionMusicCues>();
        d.returnMusicPlaying = music != null && music.ReturnMusicPlaying;
        d.mapFound = FarmMapView.Instance != null && FarmMapView.Instance.HasMap;

        if (GameplayHintManager.Instance != null)
            d.hintsShown = GameplayHintManager.Instance.GetShownHints();
        d.tutorialsShown = new List<string>(TutorialPopupUI.ShownIds);
        return d;
    }

    void CaptureInventory(CheckpointData d)
    {
        InventoryManager inventory = InventoryManager.Instance;
        if (inventory != null)
        {
            List<InventoryItem> items = inventory.GetInventory();
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null)
                    d.inventory.Add(new InventorySlotRecord { item = items[i].name, count = inventory.GetCountAt(i) });
        }
        d.selectedSlot = InventoryUI.Instance != null ? InventoryUI.Instance.SelectedIndex : -1;

        HeldItemView held = HeldItemController.Instance != null ? HeldItemController.Instance.CurrentView : null;
        d.lighterLit = held != null && held.TryGetComponent(out HeldLighter heldLighter) && heldLighter.IsLit;
        HeldLighter lighter = FindFirstObjectByType<HeldLighter>(FindObjectsInactive.Include);
        if (lighter != null)
        {
            d.lighterMisfires = lighter.PendingMisfires;
            d.lighterStruckBefore = lighter.HasStruckBefore;
        }
    }

    void CaptureItems(CheckpointData d, GameObject player)
    {
        foreach (TrackedItem t in _items)
        {
            if (t.item == null)
                d.takenItems.Add(t.id);
            else if (t.item.gameObject.activeSelf != t.startedActive)
                d.itemActive.Add(new ActiveRecord { id = t.id, on = t.item.gameObject.activeSelf });
        }

        foreach (InteractableItem item in FindObjectsByType<InteractableItem>(FindObjectsSortMode.None))
        {
            if (_ids.ContainsKey(item) || item.itemData == null || item.itemData.worldPrefab == null)
                continue;
            Transform root = item.transform.root;
            if (player != null && root == player.transform.root)
                continue;
            d.droppedItems.Add(new DroppedItemRecord
            {
                item = item.itemData.name,
                position = root.position,
                rotation = root.rotation,
                lit = root.TryGetComponent(out WorldLighter lighter) && lighter.IsLit,
            });
        }
    }

    // ───────────────────────── Load ─────────────────────────

    void Restore(CheckpointData d)
    {
        SkipIntro();

        if (d.wallStanderRevealed && _corpse != null)
            _corpse.RestoreAfterWallReveal(d.barTaken);

        RestoreItems(d);

        var openPadlocks = new HashSet<string>(d.openPadlocks);
        foreach (PadlockLockpickMinigame p in _padlocks)
            if (p != null && openPadlocks.Contains(IdOf(p)))
                p.RestoreUnlocked();

        RestoreDoors(d);

        Dictionary<string, bool> switches = ToLookup(d.lightSwitches);
        foreach (LightSwitch s in _switches)
            if (s != null && switches.TryGetValue(IdOf(s), out bool on))
                s.SetOnInstant(on);
        Dictionary<string, bool> candles = ToLookup(d.candles);
        foreach (LightableCandle c in _candles)
            if (c != null && candles.TryGetValue(IdOf(c), out bool lit))
                c.SetLit(lit);

        if (d.corridorLeft)
            FindFirstObjectByType<CorridorTension>()?.RestoreLeft();
        if (d.returnMusicPlaying)
            FindFirstObjectByType<TensionMusicCues>()?.RestoreReturnMusic();
        if (d.mapFound)
            FarmMapView.Instance?.RestoreCollected();

        RestorePlayer(d);

        GameplayHintManager.Instance?.RestoreShownHints(d.hintsShown);
        foreach (string id in d.tutorialsShown)
            TutorialPopupUI.MarkShown(id);

        RestoreInventory(d);

        // After the doors and the player: he pounds on the door as it was saved, at the distance they stand from it.
        if (d.wallStanderShutOut && _chase != null && _chase.isActiveAndEnabled)
            _chase.RestoreShutOut();

        CinematicPlayerControlLock.KickNpcNaturalRoutines();
    }

    // The scene's intro plays on awake; a checkpoint picks up long after it.
    static void SkipIntro()
    {
        foreach (PlayableDirector director in FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (director.playableAsset != null
                && director.playableAsset.name.IndexOf("GameplayTimeline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                director.Stop();
        }

        while (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            PlayerControlGate.Release(PlayerControlGate.ReasonCinematic);
        GameplayHudVisibility.Instance?.ShowGameplayHud();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void RestoreItems(CheckpointData d)
    {
        var taken = new HashSet<string>(d.takenItems);
        Dictionary<string, bool> active = ToLookup(d.itemActive);
        foreach (TrackedItem t in _items)
        {
            if (t.item == null)
                continue;
            if (taken.Contains(t.id))
                Destroy(t.item.gameObject);
            else if (active.TryGetValue(t.id, out bool on))
                t.item.gameObject.SetActive(on);
        }

        foreach (DroppedItemRecord rec in d.droppedItems)
        {
            InventoryItem item = FindItem(rec.item);
            if (item == null || item.worldPrefab == null)
                continue;
            GameObject dropped = ItemDropHelper.SpawnPlaced(item.worldPrefab, rec.position, rec.rotation, 1f, item.dropLandSFX);
            if (dropped.TryGetComponent(out WorldLighter lighter))
                lighter.SetLit(rec.lit);
        }
    }

    void RestoreDoors(CheckpointData d)
    {
        var records = new Dictionary<string, DoorRecord>();
        foreach (DoorRecord rec in d.doors)
            if (!string.IsNullOrEmpty(rec.id))
                records[rec.id] = rec;

        foreach (SimpleOpenClose door in _doors)
        {
            if (door == null || !door.gameObject.activeInHierarchy || !records.TryGetValue(IdOf(door), out DoorRecord rec))
                continue;
            if (door.objectOpen != rec.open)
                door.SnapOpenState(rec.open);
            if (!rec.locked && door.isLocked)
                door.UnlockSilently();
            else
                door.SetLocked(rec.locked);
            DoorController ai = door.GetComponentInParent<DoorController>();
            if (ai != null)
                ai.SetLocked(rec.aiLocked);
        }

        foreach (Cage cage in FindObjectsByType<Cage>(FindObjectsSortMode.None))
            cage.SyncDoorState();
    }

    static void RestorePlayer(CheckpointData d)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return;

        var body = player.GetComponent<CharacterController>();
        if (body != null)
            body.enabled = false;
        player.transform.SetPositionAndRotation(d.playerPosition, Quaternion.Euler(0f, d.playerYaw, 0f));
        if (body != null)
            body.enabled = true;
        Physics.SyncTransforms();

        FindFirstObjectByType<CinemachineMouseLook>()?.SetLook(Quaternion.Euler(d.cameraPitch, d.playerYaw, 0f));

        // Never walked out of the cage this load, so start the butcher's basement grace from here.
        Cage.RebindAllAfterContinueTeleport(player);
        InteractionController interaction = FindFirstObjectByType<InteractionController>();
        InteractionController.ArmBasementGraceUntilCageExit(interaction != null ? interaction.BasementRoutineDelaySeconds : 30f);
    }

    static void RestoreInventory(CheckpointData d)
    {
        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null)
            return;

        inventory.ClearInventory();
        foreach (InventorySlotRecord rec in d.inventory)
        {
            InventoryItem item = FindItem(rec.item);
            if (item == null)
            {
                Debug.LogWarning($"[Checkpoint] Unknown item \"{rec.item}\" in the save.");
                continue;
            }
            for (int i = 0; i < Mathf.Max(1, rec.count); i++)
                inventory.AddItem(item);
        }

        HeldLighter lighter = FindFirstObjectByType<HeldLighter>(FindObjectsInactive.Include);
        if (lighter != null)
        {
            lighter.MisfireNextStrikes(d.lighterMisfires);
            if (d.lighterStruckBefore)
                lighter.MarkStruckBefore();
        }

        List<InventoryItem> items = inventory.GetInventory();
        if (d.lighterLit && d.selectedSlot >= 0 && d.selectedSlot < items.Count && HeldItemController.Instance != null
            && HeldItemController.Instance.TryGetView(items[d.selectedSlot], out HeldItemView view)
            && view.TryGetComponent(out HeldLighter heldLighter))
            heldLighter.DrawLitNextTime();

        InventoryUI.Instance?.SelectSlot(d.selectedSlot);
    }

    static InventoryItem FindItem(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        foreach (InventoryItem item in Resources.FindObjectsOfTypeAll<InventoryItem>())
            if (item.name == name)
                return item;
        return null;
    }

    static Dictionary<string, bool> ToLookup(List<ActiveRecord> records)
    {
        var lookup = new Dictionary<string, bool>();
        foreach (ActiveRecord rec in records)
            if (!string.IsNullOrEmpty(rec.id))
                lookup[rec.id] = rec.on;
        return lookup;
    }

    // ───────────────────────── Indicator ─────────────────────────

    void ShowIndicator()
    {
        if (_indicator == null)
            BuildIndicator();
        if (_indicatorRoutine != null)
            StopCoroutine(_indicatorRoutine);
        _indicatorRoutine = StartCoroutine(IndicatorRoutine());
    }

    void BuildIndicator()
    {
        var canvasGo = new GameObject("Auto-Save Indicator", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4000;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        _indicator = canvasGo.GetComponent<CanvasGroup>();
        _indicator.alpha = 0f;
        _indicator.interactable = false;
        _indicator.blocksRaycasts = false;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(canvasGo.transform, false);
        var rt = labelGo.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-48f, 40f);
        rt.sizeDelta = new Vector2(600f, 60f);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = indicatorFontSize;
        label.color = indicatorColor;
        label.alignment = TextAlignmentOptions.BottomRight;
        label.raycastTarget = false;
        UIFont.SetText(label, Loc.Get(IndicatorKey));
    }

    IEnumerator IndicatorRoutine()
    {
        UIFont.SetText(_indicator.GetComponentInChildren<TextMeshProUGUI>(), Loc.Get(IndicatorKey));
        for (float t = 0f; t < indicatorFadeIn; t += Time.unscaledDeltaTime)
        {
            _indicator.alpha = t / indicatorFadeIn;
            yield return null;
        }
        _indicator.alpha = 1f;
        yield return new WaitForSecondsRealtime(indicatorHold);
        for (float t = 0f; t < indicatorFadeOut; t += Time.unscaledDeltaTime)
        {
            _indicator.alpha = 1f - t / indicatorFadeOut;
            yield return null;
        }
        _indicator.alpha = 0f;
        _indicatorRoutine = null;
    }
}
