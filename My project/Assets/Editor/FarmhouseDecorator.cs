#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Furnishes the farmhouse built by <see cref="FarmhouseBuilder"/> and switches the whole house to the kit's
/// worn material set. Coordinates are in the farmhouse's local frame (see FarmhouseBuilder): ground floor y 0,
/// upstairs y 3, front facade on z = 9. Kit wall props such as counters and the fireplace pivot on the wall
/// grid line; mirrors, curtains, paintings and decals pivot on the wall surface 0.1 m in front of it.
/// </summary>
public static class FarmhouseDecorator
{
    public const string DecorName = "Decor";
    const string Props = "Assets/AtmosphericHouse/Prefabs/Props/";
    const string Decals = "Assets/AtmosphericHouse/Prefabs/Decals/";
    const string Haven = PolyHavenImporter.Root + "/";
    const string FarmMaterials = "Assets/Materials/Farmhouse/";
    static readonly string[] MaterialFolders =
    {
        "Assets/AtmosphericHouse/Materials/", "Assets/AtmosphericHouse/Materials/Materials_tiled/",
        "Assets/AtmosphericHouse/Materials/Materials_additional/", FarmMaterials,
    };

    // Tinted copies of the kit wallpapers; the stock colours (lilac, grey) read too modern for the farmhouse.
    static readonly (string name, string source, Color tint)[] Wallpapers =
    {
        ("Wall_wallpaper_sage_worn", "Wall_B_wallpaper_B_worn", new Color(0.78f, 0.95f, 0.58f)),
        ("Wall_wallpaper_beige_worn", "Wall_B_wallpaper_A_worn", new Color(1f, 0.9f, 0.7f)),
    };
    const string WallpaperTint = "Color_BFCEDFC";

    const float Up = 3f;          // upstairs floor height
    const float Surface = 0.1f;   // interior wall surface offset from the grid line
    const float CurtainTop = 2.42f;

    struct Room
    {
        public int story;
        public Rect area;
        public string wall, floor;
        public Room(int story, float x0, float z0, float x1, float z1, string wall, string floor)
        {
            this.story = story; area = Rect.MinMaxRect(x0, z0, x1, z1); this.wall = wall; this.floor = floor;
        }
    }

    static readonly Room[] Rooms =
    {
        new Room(0, 0f, 6f, 6f, 12f, "Wall_wallpaper_sage_worn", null),       // living room
        new Room(0, 0f, 0f, 6f, 6f, "Wall_wallpaper_beige_worn", null),       // study
        new Room(0, 12f, 6f, 21f, 12f, "Wall_wallpaper_beige_worn", null),    // dining room
        new Room(0, 12f, 0f, 21f, 6f, null, "Floor_tiles_A_worn"),            // kitchen
        new Room(1, 0f, 6f, 6f, 12f, "Wall_B_wallpaper_A_worn", null),        // main bedroom
        new Room(1, 0f, 0f, 6f, 6f, "Wall_wallpaper_sage_worn", null),        // sewing room
        new Room(1, 9f, 0f, 12f, 6f, "Wall_tiles_worn", "Floor_tiles_B_worn"), // bathroom
    };

    static Transform _house, _decor, _group;
    static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

    [MenuItem("Tools/Environment/Redecorate Farmhouse")]
    static void RedecorateMenu()
    {
        var env = GameObject.Find("_Environment");
        var house = env ? env.transform.Find(FarmhouseBuilder.RootName) : null;
        if (!house) { Debug.LogError("No farmhouse to decorate."); return; }
        Debug.Log(Redecorate(house));
    }

    public static string Redecorate(Transform house)
    {
        int n = Decorate(house);
        FarmhouseBuilder.FinishOpenablesAndStatics(house.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(house.gameObject.scene);
        return $"Farmhouse decorated with {n} props.";
    }

    /// Rebuilds the Decor group and applies worn materials, room surfaces and door grime. Returns the prop count.
    public static int Decorate(Transform house)
    {
        _house = house;
        _materials.Clear();
        var old = house.Find(DecorName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);
        _decor = new GameObject(DecorName).transform;
        Undo.RegisterCreatedObjectUndo(_decor.gameObject, "Decorate Farmhouse");
        _decor.SetParent(house, false);
        Physics.SyncTransforms();

        Group("Living Room"); LivingRoom();
        Group("Study"); Study();
        Group("Hall"); Hall();
        Group("Dining Room"); DiningRoom();
        Group("Kitchen"); Kitchen();
        Group("Main Bedroom"); MainBedroom();
        Group("Sewing Room"); SewingRoom();
        Group("Child Room"); ChildRoom();
        Group("Storage Room"); StorageRoom();
        Group("Bathroom"); Bathroom();
        Group("Porches and Yard"); Exterior();
        Group("Door Grime"); DoorGrime();

        int props = 0;
        foreach (Transform g in _decor) props += g.childCount;

        ApplyWorn(house.gameObject);
        EnsureWallpapers();
        ApplyRoomSurfaces();
        foreach (var light in _decor.GetComponentsInChildren<Light>(true)) light.enabled = false;
        foreach (var anim in _decor.GetComponentsInChildren<Animation>(true)) { anim.playAutomatically = false; anim.enabled = false; }
        return props;
    }

    // ---------- Rooms ----------

    // Doorways to keep clear (centre of each 3 m module): ground x = 6 at z 7.5 (arch), x = 12 at z 1.5 (door)
    // and 7.5 (arch), z = 6 at x 1.5 (door) and 16.5 (arch); upstairs x = 6 at z 7.5, x = 12 at z 1.5 and 7.5,
    // z = 6 at x 1.5, 10.5 and 16.5 (arch). Front door x 9..12, back door x 15..18.

    static void LivingRoom()
    {
        // Fireplace on the side wall, sofa group facing it; the arch to the hall stays clear behind the sofa.
        K("Furniture/Fireplace", 0f, 0f, 7.5f, 90f);
        K("Carpets/Carpet_persian_A", 2.75f, 0f, 7.9f, 0f);
        K("Furniture/Sofa_3seat", 3.75f, 0f, 7.9f, 270f);
        K("Furniture/Table_coffee", 2.45f, 0f, 7.9f, 90f);
        K("Small_table_props/Magazine_A", 2.35f, 0.42f, 7.7f, 70f);
        K("Kitchen_supplies/Coffee_mug", 2.55f, 0.42f, 8.25f, 0f);
        // The locker hiding spot (under the Living_Room anchor) stands on the hall wall at z 11.3.
        K("Furniture/Sofa_1seat", 1.6f, 0f, 9.9f, 210f);
        H("Rockingchair_01", 3.5f, 0f, 11.1f, 190f);
        K("Furniture/Side_table_E", 0.6f, 0f, 11.2f, 90f);
        K("Lamps/Lamp_table_shade", 0.6f, 0.57f, 11.2f, 0f);
        H("vintage_grandfather_clock_01", 6f - Surface - 0.23f, 0f, 9.75f, 270f);
        K("Lamps/CeilingFan", 3f, 2.8f, 9f, 0f);
        Curtains(0, 'N', 12f, 1.5f, true); Curtains(0, 'N', 12f, 4.5f, true);
        Curtains(0, 'W', 0f, 10.5f, true);
        D("Decal_plaster_floor_B", 1.4f, 0.003f, 11.2f, 30f);
        D("Decal_papers_C", 4.6f, 0.003f, 9.9f, 200f);
        WallDecal("Decal_leaking_A", 'N', 12f, 0.6f, 2.75f);
        WallDecal("Decal_damage_crack_B", 'E', 6f, 11.4f, 2.3f);
    }

    static void Study()
    {
        // Farmer's office: desk under the back windows, bookcase on the hall wall.
        K("Furniture/Bookcase_B", 6f - Surface - 0.15f, 0f, 3f, 270f);
        K("Books/Book_set_A", 5.7f, 0.81f, 3f, 270f);
        K("Furniture/Table_B", 3f, 0f, 1.05f, 0f);
        K("Furniture/Chair_wooden", 3f, 0f, 1.85f, 175f);
        K("Small_table_props/Magazine_A", 2.75f, 0.78f, 1.0f, 20f);
        H("vintage_oil_lamp", 3.45f, 0.78f, 0.9f, 0f);
        K("Furniture/Side_table_E", 0.6f, 0f, 4.5f, 90f);
        K("Lamps/Lamp_table_shade", 0.6f, 0.57f, 4.5f, 0f);
        K("Furniture/Dresser_C", 4.45f, 0f, 6f - Surface - 0.27f, 180f);
        K("Small_table_props/Painting_table_A", 4.1f, 0.86f, 5.6f, 180f);
        K("Paintings/Painting_H", 4.45f, 1.75f, 6f - Surface - 0.02f, 180f);
        K("Storage_props/Storage_box_wicker", 1.0f, 0f, 2.9f, 15f);
        K("Lamps/Lamp_ceiling_round", 3f, 2.8f, 3f, 0f);
        Curtains(0, 'S', 0f, 1.5f, true); Curtains(0, 'S', 0f, 4.5f, true);
        Curtains(0, 'W', 0f, 1.5f, true);
        D("Decal_papers_E", 2.2f, 0.003f, 3.6f, 300f);
        WallDecal("Decal_leaking_B", 'W', 0f, 4.4f, 2.75f);
    }

    static void Hall()
    {
        K("Carpets/Carpet_A", 10.5f, 0f, 9.6f, 0f);
        K("Furniture/Side_table_D", 12f - Surface - 0.19f, 0f, 10.6f, 270f);
        H("mantel_clock_01", 11.72f, 0.87f, 10.85f, 270f);
        K("Paintings/Painting_E", 12f - Surface - 0.02f, 1.65f, 10.6f, 270f);
        // Back of the hall, toward the kitchen door. The bottom stair steps face +x at z 0..1.2, so keep
        // x 8.3..9.6 there clear.
        K("Furniture/Cabinet_B", 12f - Surface - 0.31f, 0f, 4.5f, 270f);
        K("Storage_props/Storage_box_wicker", 8.75f, 0f, 3.2f, 10f);
        K("Lamps/Lamp_ceiling_round", 9f, 2.8f, 9f, 0f);
        K("Lamps/Lamp_ceiling_round", 10.5f, 2.8f, 3f, 0f);
        K("Lamps/Lamp_ceiling_ball", 9f, Up + 2.8f, 9f, 0f);
        Curtains(0, 'S', 0f, 10.5f, false);
        D("Decal_papers_A", 9.8f, 0.003f, 10.9f, 15f);
        D("Decal_plaster_floor_D", 10.6f, 0.003f, 4.2f, 80f);
    }

    static void DiningRoom()
    {
        // Table centred at (16.5, 9); the arches to the hall (x = 12) and kitchen (z = 6, x 15..18) stay clear.
        const float tx = 16.5f, tz = 9f;
        K("Carpets/Carpet_persian_B", tx, 0f, tz, 90f);
        K("Furniture/Table_A_tablecloth", tx, 0f, tz, 0f);
        K("Furniture/Chair_wooden", tx - 0.6f, 0f, tz - 0.9f, 0f);
        K("Furniture/Chair_wooden", tx + 0.6f, 0f, tz - 0.9f, 0f);
        K("Furniture/Chair_wooden", tx - 0.6f, 0f, tz + 0.9f, 180f);
        K("Furniture/Chair_wooden", tx + 0.75f, 0f, tz + 1.25f, 160f);
        K("Furniture/Chair_wooden", tx - 1.45f, 0f, tz, 90f);
        K("Furniture/Chair_wooden", tx + 1.45f, 0f, tz, 270f);
        K("Small_table_props/China_set_B", tx - 0.1f, 0.79f, tz, 0f);
        K("Small_table_props/Candelabra", tx + 0.6f, 0.79f, tz - 0.05f, 0f);
        K("Lamps/Lamp_chandelier", tx, 2.8f, tz, 0f);
        K("Furniture/Cabinet_B", 21f - Surface - 0.31f, 0f, 10.5f, 270f);
        K("Furniture/Dresser_C", 13.6f, 0f, 6f + Surface + 0.27f, 0f);
        K("Small_table_props/Candelabra", 13.0f, 0.86f, 6.35f, 0f);
        K("Small_table_props/China_set_A", 14.15f, 0.86f, 6.35f, 0f);
        K("Paintings/Painting_H", 13.6f, 1.75f, 6f + Surface + 0.02f, 0f);
        K("Furniture/Side_table_D", 19.6f, 0f, 6f + Surface + 0.19f, 0f);
        Curtains(0, 'N', 12f, 13.5f, true); Curtains(0, 'N', 12f, 16.5f, true); Curtains(0, 'N', 12f, 19.5f, true);
        Curtains(0, 'E', 21f, 7.5f, true);
        WallDecal("Decal_leaking_B", 'E', 21f, 11.1f, 2.75f);
        D("Decal_plaster_floor_D", 19.8f, 0.003f, 11.2f, 120f);
    }

    static void Kitchen()
    {
        // Counter run on the dining-room partition (z = 6) east of the arch, facing -z; modules span +1 m in x
        // from their pivot. Stove on the east wall, fridge on the back wall, table in the middle.
        K("Kitchen_cabinets/Kitchen_counter_A", 17.9f, 0f, 6f, 180f);
        K("Kitchen_cabinets/Kitchen_counter_sink", 18.9f, 0f, 6f, 180f);
        K("Kitchen_cabinets/Kitchen_counter_B", 19.9f, 0f, 6f, 180f);
        K("Kitchen_cabinets/Kitchen_cabinet_A", 17.9f, 0f, 6f, 180f);
        K("Kitchen_cabinets/Kitchen_cabinet_C", 18.9f, 0f, 6f, 180f);
        K("Kitchen_cabinets/Kitchen_cabinet_B", 19.9f, 0f, 6f, 180f);
        K("Electronics/Stove", 21f, 0f, 1.5f, 270f);
        K("Kitchen_supplies/Pot", 20.5f, Top(20.5f, 1.75f, 1.2f), 1.75f, 0f);
        K("Kitchen_supplies/Frying_pan", 20.5f, Top(20.5f, 1.25f, 1.2f), 1.25f, 120f);
        K("Electronics/Fridge", 13.4f, 0f, 0f, 0f);
        K("Furniture/Table_B", 16.4f, 0f, 3.1f, 0f);
        K("Furniture/Chair_kitchen", 16.4f, 0f, 2.3f, 0f);
        K("Furniture/Chair_kitchen", 15.6f, 0f, 3.15f, 80f);
        K("Furniture/Chair_kitchen", 17.3f, 0f, 3.6f, 230f);
        K("Kitchen_supplies/Plate_dirty", 16.3f, 0.78f, 2.9f, 0f);
        K("Kitchen_supplies/Coffee_mug", 16.65f, 0.78f, 3.25f, 0f);
        K("Kitchen_supplies/Trashcan_large", 12.45f, 0f, 4.4f, 0f);
        K("Kitchen_supplies/Cutting_board", 18.35f, 0.95f, 5.6f, 180f);
        K("Kitchen_supplies/Jar_A", 20.35f, 0.95f, 5.75f, 0f);
        K("Kitchen_supplies/Jar_C", 20.55f, 0.95f, 5.72f, 0f);
        K("Kitchen_supplies/Cutlery_holder", 20.1f, 0.95f, 5.75f, 0f);
        K("Storage_props/Dish_soap", 19.75f, 0.95f, 5.78f, 0f);
        K("Lamps/Lamp_ceiling_round", 16.5f, 2.8f, 3f, 0f);
        Curtains(0, 'S', 0f, 19.5f, false);
        D("Decal_damage_tiles", 14.4f, 0.003f, 2.3f, 0f);
        D("Decal_damage_tiles", 19.2f, 0.003f, 3.9f, 90f);
    }

    static void MainBedroom()
    {
        // Bed on the side wall; the king bed itself is the player's hiding spot under the Main_Bedroom_Room anchor.
        if (!HasGameplay("Bed_king")) K("Furniture/Bed_king", 1.17f, Up, 9f, 90f);
        K("Furniture/Side_table_C", 0.36f, Up, 7.45f, 90f);
        K("Furniture/Side_table_C", 0.36f, Up, 10.55f, 90f);
        H("vintage_oil_lamp", 0.36f, Up + 0.69f, 7.45f, 0f);
        K("Lamps/Lamp_table_shade", 0.36f, Up + 0.69f, 10.55f, 0f);
        K("Paintings/Painting_K", Surface + 0.02f, Up + 1.85f, 9f, 90f);
        K("Furniture/Dresser_B", 4.3f, Up, 6f + Surface + 0.27f, 0f);
        K("Small_table_props/Hair_brush", 4.0f, Up + 0.86f, 6.35f, 20f);
        K("Small_table_props/Lotion_set_A", 4.6f, Up + 0.86f, 6.3f, 0f);
        K("Bathroom_props/Bathroom_mirror", 4.3f, Up + 1.6f, 6f + Surface, 0f);
        K("Carpets/Carpet_persian_B", 3.2f, Up, 9f, 90f);
        K("Furniture/Chair_wooden", 2.7f, Up, 11.35f, 200f);
        K("Lamps/CeilingFan", 3f, Up + 2.8f, 9f, 0f);
        Curtains(1, 'N', 12f, 1.5f, true); Curtains(1, 'N', 12f, 4.5f, true);
        WallDecal("Decal_leaking_A", 'N', 12f, 3f, Up + 2.75f);
        D("Decal_plaster_floor_F", 4.3f, Up + 0.003f, 8.6f, 60f);
    }

    static void SewingRoom()
    {
        K("Furniture/Wardrobe", 6f - Surface - 0.34f, Up, 4.4f, 270f);
        K("Furniture/Table_B", 1.2f, Up, 3.9f, 90f);
        K("Furniture/Chair_wooden", 2.0f, Up, 3.9f, 270f);
        K("Storage_props/Laundry_basket_A", 0.6f, Up, 2.4f, 20f);
        K("Storage_props/Storage_box_wicker", 4.9f, Up, 0.5f, 350f);
        K("Furniture/Side_table_C", 0.36f, Up, 0.5f, 90f);
        K("Lamps/Lamp_ceiling_round", 3f, Up + 2.8f, 3f, 0f);
        Curtains(1, 'S', 0f, 1.5f, true); Curtains(1, 'S', 0f, 4.5f, true);
        Curtains(1, 'W', 0f, 1.5f, true);
        WallDecal("Decal_leaking_A", 'S', 0f, 3f, Up + 2.75f);
        D("Decal_papers_C", 3.2f, Up + 0.003f, 4.6f, 40f);
    }

    static void ChildRoom()
    {
        // The bed is a hiding spot entered from its west side (FarmhouseGameplay), so the night stand goes east.
        if (!HasGameplay("Bed_twin")) K("Furniture/Bed_twin", 19.5f, Up, 6f + Surface + 1.04f, 0f);
        K("Furniture/Side_table_E", 20.55f, Up, 6.4f, 0f);
        K("Lamps/Lamp_table_shade", 20.55f, Up + 0.57f, 6.4f, 0f);
        K("Paintings/Painting_N", 19.5f, Up + 1.85f, 6f + Surface + 0.02f, 0f);
        K("Furniture/Dresser_A", 14.4f, Up, 6f + Surface + 0.17f, 0f);
        K("Furniture/Bookcase_A", 12f + Surface + 0.15f, Up, 10.5f, 90f);
        K("Books/Book_set_A", 12.35f, Up + 0.81f, 10.5f, 90f);
        K("Furniture/Chair_wooden", 17.6f, Up, 11.2f, 210f);
        K("Carpets/Carpet_C", 16.5f, Up, 9f, 90f);
        K("Lamps/Lamp_ceiling_round", 16.5f, Up + 2.8f, 9f, 0f);
        Curtains(1, 'N', 12f, 13.5f, false); Curtains(1, 'N', 12f, 16.5f, false); Curtains(1, 'N', 12f, 19.5f, false);
        Curtains(1, 'E', 21f, 7.5f, false);
        D("Decal_papers_E", 15.2f, Up + 0.003f, 8.4f, 300f);
    }

    static void StorageRoom()
    {
        // Junk room: crates and furniture break line of sight between the bathroom door and the arch.
        K("Furniture/Wardrobe", 21f - Surface - 0.34f, Up, 1.6f, 270f);
        K("Furniture/Bookcase_B", 16.5f, Up, Surface + 0.15f, 0f);
        H("wooden_crate_02", 14.4f, Up, 0.8f, 10f);
        H("wooden_crate_02", 14.5f, Up, 1.75f, 95f);
        H("barrel_03", 20.3f, Up, 4.9f, 0f);
        H("wooden_ladder", 19.2f, Up, 0.45f, 0f);
        K("Furniture/Chair_wooden", 18.6f, Up, 3.4f, 40f);
        K("Furniture/Sofa_1seat", 13.4f, Up, 4.6f, 120f);
        K("Storage_props/Storage_box_wicker", 17.8f, Up, 4.9f, 15f);
        K("Storage_props/Laundry_basket_A", 15.0f, Up, 3.5f, 60f);
        K("Lamps/Lamp_ceiling_round", 16.5f, Up + 2.8f, 3f, 0f);
        Curtains(1, 'S', 0f, 13.5f, false); Curtains(1, 'S', 0f, 19.5f, false);
        D("Decal_plaster_floor_H", 16.9f, Up + 0.003f, 2.2f, 0f);
        WallDecal("Decal_leaking_B", 'E', 21f, 4.4f, Up + 2.75f);
    }

    static void Bathroom()
    {
        // Tub and curtain rail need a wall at both ends: they fill the 2.8 m between the back wall and the
        // stairwell partition along the x = 9 wall (tub local x -2.9..-0.1 runs along +z, local z -0.4 on the wall).
        K("Bathroom_props/Bathtub", 9f + Surface + 0.4f, Up, 0f, 90f);
        K("Bathroom_props/Shower_curtain", 9f + Surface + 0.4f, Up, 0f, 90f);
        if (!HasGameplay("Toilet")) K("Bathroom_props/Toilet", 12f - Surface - 0.37f, Up, 3.7f, 270f);
        K("Bathroom_props/Toiletpaper_set", 11.75f, Up, 3.05f, 270f);
        K("Bathroom_props/Bathroom_sink", 9f + Surface, Up, 3.7f, 90f);
        K("Bathroom_props/Bathroom_mirror", 9f + Surface, Up + 1.6f, 3.7f, 90f);
        K("Lamps/Lamp_bathroom_wall", 9f + Surface, Up + 2.2f, 3.7f, 90f);
        K("Bathroom_props/Trashcan", 9.35f, Up, 4.6f, 0f);
        D("Decal_plaster_floor_H", 10.6f, Up + 0.003f, 2.2f, 0f);
    }

    static void Exterior()
    {
        // Front porch (deck z 12..14.9, steps at x 9..12) and back porch (x 13.4..19.6, z -3..0, steps around x 16.5).
        H("Rockingchair_01", 3.2f, Ground(3.2f, 13.4f), 13.4f, 15f);
        H("wooden_stool_01", 4.1f, Ground(4.1f, 13.1f), 13.1f, 30f);
        H("wicker_basket_01", 4.1f, Ground(4.1f, 13.1f) + 0.44f, 13.1f, 10f);
        H("painted_wooden_bench", 15.5f, Ground(15.5f, 12.5f), 12.5f, 0f);
        H("watering_can_metal_01", 6.6f, Ground(6.6f, 14.4f), 14.4f, 120f);
        H("wooden_bucket_01", 19.2f, Ground(19.2f, 14.3f), 14.3f, 0f);

        K("Lamps/Lamp_exterior_wall", 17.6f, 1.95f, -0.1f, 180f);
        H("garden_hose_wall_mounted_01", 18.9f, 0.9f, -0.1f - 0.13f, 180f, snap: false);
        H("wooden_stool_01", 17.5f, Ground(17.5f, -0.6f), -0.6f, 200f);
        H("metal_jerrycan", 18.9f, Ground(18.9f, -2.4f), -2.4f, 160f);
        H("barrel_03", 21.45f, Ground(21.45f, -0.45f), -0.45f, 0f);
        H("wooden_crate_02", 6.5f, Ground(6.5f, -0.6f), -0.6f, 180f);
        H("rusted_spade_01", 8.25f, Ground(8.25f, -0.25f), -0.25f, 180f, pitch: -12f);
        H("sledgehammer_01", 8.0f, Ground(8.0f, -0.22f), -0.22f, 170f, pitch: -10f);
        H("wooden_ladder", 3f, Ground(3f, -0.9f), -0.9f, 180f);
        H("propane_tank", -0.45f, Ground(-0.45f, 0.6f), 0.6f, 0f);
        H("old_tyre", -0.32f, Ground(-0.32f, 2.1f), 2.1f, 90f, pitch: -8f);
        H("tree_stump_02", 3f, Ground(3f, -5.5f) - 0.15f, -5.5f, 40f);
        H("wooden_axe", 3.05f, Ground(3f, -5.5f) + 0.28f, -5.45f, 100f, pitch: 28f);
    }

    /// Grime decals from the kit's "spawn additional" list, on every interior face of a door wall.
    static void DoorGrime()
    {
        foreach (var groupName in new[] { "Interior Walls", "Exterior Walls" })
        {
            var g = _house.Find(groupName);
            if (!g) continue;
            foreach (Transform piece in g)
            {
                var name = piece.name;
                if (!name.StartsWith("WallIn_") || !(name.EndsWith("_3x_door") || name.EndsWith("_3x_doordouble"))) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Decals + "Decals_corner/Decal_wallIn" + name.Substring("WallIn".Length) + ".prefab");
                if (!prefab) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _group);
                go.transform.localPosition = piece.localPosition;
                go.transform.localRotation = piece.localRotation;
                NoShadows(go);
            }
        }
    }

    // ---------- Materials ----------

    static void ApplyWorn(GameObject root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i]) continue;
                var worn = Worn(mats[i]);
                if (worn != mats[i]) { mats[i] = worn; changed = true; }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    /// X_clean -> X_worn, lamps switched off (Lamps_A_on_clean -> Lamps_A_off_worn, CeilingFan_clean -> CeilingFan_off_worn).
    static Material Worn(Material m)
    {
        var n = m.name;
        if (!n.Contains("_clean") && !n.Contains("_on_")) return m;
        var w = n.Replace("_on_", "_off_").Replace("_clean", "_worn");
        return (n.Contains("_off") ? null : Mat(w.Replace("_worn", "_off_worn"))) ?? Mat(w) ?? m;
    }

    static void EnsureWallpapers()
    {
        if (!AssetDatabase.IsValidFolder(FarmMaterials.TrimEnd('/')))
            AssetDatabase.CreateFolder("Assets/Materials", "Farmhouse");
        foreach (var (name, source, tint) in Wallpapers)
        {
            var path = FarmMaterials + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Mat(source)) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor(WallpaperTint, tint);
            EditorUtility.SetDirty(mat);
            _materials[name] = mat;
        }
        AssetDatabase.SaveAssets();
    }

    static void ApplyRoomSurfaces()
    {
        foreach (var groupName in new[] { "Interior Walls", "Exterior Walls", "Floors" })
        {
            var g = _house.Find(groupName);
            if (!g) continue;
            bool floors = groupName == "Floors";
            foreach (Transform piece in g)
            {
                var forward = floors ? Vector3.zero : _house.InverseTransformDirection(piece.forward);
                foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
                {
                    var probe = _house.InverseTransformPoint(r.bounds.center) + forward * 0.5f;
                    int story = probe.y > (floors ? Up * 0.5f : Up - 0.1f) ? 1 : 0;
                    foreach (var room in Rooms)
                    {
                        if (room.story != story || !room.area.Contains(new Vector2(probe.x, probe.z))) continue;
                        var target = floors ? room.floor : room.wall;
                        if (target == null) break;
                        var replacement = Mat(target);
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                            if (mats[i] && mats[i].name.StartsWith(floors ? "Floor_" : "Wall_")) mats[i] = replacement;
                        r.sharedMaterials = mats;
                        break;
                    }
                }
            }
        }
    }

    static Material Mat(string name)
    {
        if (_materials.TryGetValue(name, out var cached)) return cached;
        Material found = null;
        foreach (var folder in MaterialFolders)
        {
            found = AssetDatabase.LoadAssetAtPath<Material>(folder + name + ".mat");
            if (found) break;
        }
        _materials[name] = found;
        return found;
    }

    // ---------- Placement helpers ----------

    static void Group(string name)
    {
        _group = new GameObject(name).transform;
        _group.SetParent(_decor, false);
    }

    /// True when the Gameplay group or a farmhouse room anchor already holds an object whose name starts with
    /// prefix (e.g. the bed hiding spot).
    static bool HasGameplay(string prefix)
    {
        var gameplay = _house.Find(FarmhouseBuilder.GameplayName);
        if (gameplay)
            foreach (var t in gameplay.GetComponentsInChildren<Transform>(true))
                if (t != gameplay && t.name.StartsWith(prefix)) return true;
        return FarmhouseGameplay.RoomsHold(prefix);
    }

    static GameObject Instantiate(string assetPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (!prefab) throw new System.ArgumentException("Missing decor asset " + assetPath);
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab, _group);
    }

    /// Kit prop at its own pivot.
    static GameObject K(string rel, float x, float y, float z, float yaw)
    {
        var go = Instantiate(Props + rel + ".prefab");
        var prefabRot = go.transform.localRotation;
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefabRot;
        return go;
    }

    /// Poly Haven model centred on (x, z); with snap its lowest point sits at y, otherwise its centre is at y.
    static GameObject H(string id, float x, float y, float z, float yaw, float pitch = 0f, bool snap = true)
    {
        var go = Instantiate($"{Haven}{id}/{id}_1k.fbx");
        var prefabRot = go.transform.localRotation;
        go.transform.localRotation = Quaternion.Euler(pitch, yaw, 0f) * prefabRot;
        go.transform.localPosition = new Vector3(x, y, z);
        var b = WorldBounds(go);
        var target = _house.TransformPoint(new Vector3(x, y, z));
        go.transform.position += new Vector3(target.x - b.center.x, target.y - (snap ? b.min.y : b.center.y), target.z - b.center.z);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh && mf.sharedMesh.bounds.size.magnitude > 0f && !mf.GetComponent<Collider>()) mf.gameObject.AddComponent<BoxCollider>();
        return go;
    }

    static GameObject D(string name, float x, float y, float z, float yaw)
    {
        var go = Instantiate(Decals + name + ".prefab");
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.localRotation;
        NoShadows(go);
        return go;
    }

    static void WallDecal(string name, char side, float line, float along, float y)
    {
        var p = OnWall(side, line, along, y, Surface + 0.005f, out float yaw);
        D(name, p.x, p.y, p.z, yaw);
    }

    /// Curtain pair centred on a window. Story windows run from 0.65 to 2.28 m above the floor.
    static void Curtains(int story, char side, float line, float along, bool tall)
    {
        var p = OnWall(side, line, along, story * Up + CurtainTop, 0.08f, out float yaw);
        K(tall ? "Curtains/Curtains_long_singlewindow" : "Curtains/Curtains_short_singlewindow", p.x, p.y, p.z, yaw);
    }

    /// Point in front of a wall. 'S': wall on z = line with the room on +z; 'N': room on -z; 'W': wall on x = line, room on +x; 'E': room on -x.
    static Vector3 OnWall(char side, float line, float along, float y, float offset, out float yaw)
    {
        switch (side)
        {
            case 'S': yaw = 0f; return new Vector3(along, y, line + offset);
            case 'N': yaw = 180f; return new Vector3(along, y, line - offset);
            case 'W': yaw = 90f; return new Vector3(line + offset, y, along);
            default: yaw = 270f; return new Vector3(line - offset, y, along);
        }
    }

    /// Height (farmhouse-local) of the porch deck or terrain under (x, z), ignoring decor.
    static float Ground(float x, float z)
    {
        Physics.SyncTransforms();
        var origin = _house.TransformPoint(new Vector3(x, 2.4f, z));
        float best = float.MaxValue, y = 0f;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(_decor) || hit.distance >= best) continue;
            best = hit.distance;
            y = _house.InverseTransformPoint(hit.point).y;
        }
        return y;
    }

    /// Height (farmhouse-local) of the highest decor surface under (x, z) below fromY, e.g. a cooktop.
    static float Top(float x, float z, float fromY)
    {
        Physics.SyncTransforms();
        var origin = _house.TransformPoint(new Vector3(x, fromY, z));
        float best = float.MaxValue, y = 0f;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, fromY + 1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(_decor) || hit.distance >= best) continue;
            best = hit.distance;
            y = _house.InverseTransformPoint(hit.point).y;
        }
        return y;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void NoShadows(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
#endif
