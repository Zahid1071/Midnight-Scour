using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the corpse's pounce (crouch, leap at the camera, grab) into a humanoid clip. Each key starts from a pose
/// sampled out of an existing Mixamo clip (ScreamJS's crouch, Running's push-off and flight) with hand-set overrides.
/// Body travel is baked into the pose: the character starts at its origin facing +Z and ends about 1.35 m ahead,
/// head at eye height. Muscle signs (Unity humanoid): Arm Front-Back negative = forward, Arm Down-Up positive = up,
/// Upper Leg Front-Back negative = forward, Stretch negative = bent, Nod positive = up, Jaw Close negative = open.
/// </summary>
public static class ZombiePounceClipBuilder
{
    public const string ClipPath = "Assets/Animation/Animations/Zombie_Pounce.anim";
    const string ScreamPath = "Assets/Animation/Animations/Adam@ScreamJS.fbx";
    const string RunPath = "Assets/Animation/Animations/Adam@Running.fbx";

    sealed class Key
    {
        public readonly float Time;
        public readonly Vector3 Body;   // RootT (x right, y up, z forward)
        public readonly float Pitch;    // forward lean, degrees
        public readonly Dictionary<string, float> Curves;

        public Key(float time, Vector3 body, float pitch, Dictionary<string, float> basePose)
        {
            Time = time;
            Body = body;
            Pitch = pitch;
            Curves = new Dictionary<string, float>(basePose);
        }

        public Key Set(string muscle, float value) { Curves[muscle] = value; return this; }

        public Key Both(string muscle, float value) => Set("Left " + muscle, value).Set("Right " + muscle, value);

        // Finger curves are named "LeftHand.Index.1 Stretched", "LeftHand.Index.Spread", ...
        public Key Fingers(float stretched, float spread)
        {
            var names = new List<string>(Curves.Keys);
            foreach (string n in names)
            {
                if (!n.Contains("Hand."))
                    continue;
                if (n.EndsWith("Stretched"))
                    Curves[n] = stretched;
                else if (n.EndsWith("Spread"))
                    Curves[n] = spread;
            }
            return this;
        }

        public Key Legs(Dictionary<string, float> from)
        {
            foreach (KeyValuePair<string, float> kv in from)
                if (kv.Key.Contains(" Leg ") || kv.Key.Contains("Foot") || kv.Key.Contains("Toes"))
                    Curves[kv.Key] = kv.Value;
            return this;
        }
    }

    static List<Key> Keys()
    {
        Dictionary<string, float> crouch = Sample(ScreamPath, 1.9f);
        Dictionary<string, float> pushOff = Sample(RunPath, 0.1f);
        Dictionary<string, float> flight = Sample(RunPath, 0.15f);
        Dictionary<string, float> tuck = Sample(RunPath, 0.0f);

        var keys = new List<Key>();

        // Spawn: low crouch, head up at the player, mouth half open.
        keys.Add(new Key(0f, new Vector3(0f, 0.68f, 0f), 50f, crouch)
            .Set("Jaw Close", 0.2f).Fingers(0.2f, 0.3f));

        // Coils deeper, arms drawn back for the swing.
        keys.Add(new Key(0.26f, new Vector3(0f, 0.6f, -0.06f), 56f, crouch)
            .Set("Head Nod Down-Up", 1f).Set("Jaw Close", -0.6f)
            .Both("Arm Front-Back", 0.45f).Both("Arm Down-Up", -0.25f).Both("Forearm Stretch", 0.4f)
            .Fingers(0.3f, 0.5f));

        // Launch: back leg drives, arms swing forward and up.
        keys.Add(new Key(0.42f, new Vector3(0f, 0.88f, 0.4f), 38f, crouch)
            .Legs(pushOff)
            .Set("Spine Front-Back", -0.5f).Set("Chest Front-Back", -0.2f).Set("UpperChest Front-Back", -0.1f)
            .Set("Head Nod Down-Up", 0.8f).Set("Jaw Close", -1f)
            .Both("Arm Front-Back", -0.55f).Both("Arm Down-Up", 0.4f).Both("Forearm Stretch", 0.7f).Both("Hand Down-Up", 0.3f)
            .Fingers(0.9f, 0.8f));

        // In the air, legs tucked, reaching wide for the face.
        keys.Add(new Key(0.58f, new Vector3(0f, 1.22f, 0.95f), 28f, crouch)
            .Legs(flight)
            .Set("Left Lower Leg Stretch", -0.45f).Set("Right Lower Leg Stretch", -0.25f)
            .Set("Spine Front-Back", -0.15f).Set("Chest Front-Back", 0f).Set("UpperChest Front-Back", 0f)
            .Set("Neck Nod Down-Up", 0.3f).Set("Head Nod Down-Up", 0.6f).Set("Jaw Close", -1f)
            .Both("Arm Front-Back", -0.4f).Both("Arm Down-Up", 0.6f).Both("Forearm Stretch", 0.9f).Both("Hand Down-Up", 0.4f)
            .Fingers(1f, 1f));

        // Slams into the player: hands close in on either side of the head.
        keys.Add(new Key(0.72f, new Vector3(0f, 1.14f, 1.34f), 18f, crouch)
            .Legs(tuck)
            .Set("Left Lower Leg Stretch", -0.5f).Set("Right Lower Leg Stretch", -0.4f)
            .Set("Spine Front-Back", -0.3f).Set("Chest Front-Back", -0.15f).Set("UpperChest Front-Back", -0.1f)
            .Set("Neck Nod Down-Up", 0.35f).Set("Head Nod Down-Up", 0.75f).Set("Jaw Close", -0.9f)
            .Both("Arm Front-Back", -0.6f).Both("Arm Down-Up", 0.5f).Both("Forearm Stretch", 0.45f).Both("Hand Down-Up", 0.2f)
            .Fingers(-0.2f, 0.4f));

        // Clinging on, head lunging in to bite.
        keys.Add(new Key(0.95f, new Vector3(0f, 1.12f, 1.42f), 20f, crouch)
            .Legs(tuck)
            .Set("Left Lower Leg Stretch", -0.5f).Set("Right Lower Leg Stretch", -0.4f)
            .Set("Spine Front-Back", -0.4f).Set("Chest Front-Back", -0.2f).Set("UpperChest Front-Back", -0.15f)
            .Set("Neck Nod Down-Up", 0.3f).Set("Head Nod Down-Up", 0.55f).Set("Jaw Close", -1f)
            .Both("Arm Front-Back", -0.65f).Both("Arm Down-Up", 0.45f).Both("Forearm Stretch", 0.3f).Both("Hand Down-Up", 0.1f)
            .Fingers(-0.5f, 0.2f));

        keys.Add(new Key(1.25f, new Vector3(0f, 1.1f, 1.38f), 18f, crouch)
            .Legs(tuck)
            .Set("Left Lower Leg Stretch", -0.5f).Set("Right Lower Leg Stretch", -0.4f)
            .Set("Spine Front-Back", -0.35f).Set("Chest Front-Back", -0.2f).Set("UpperChest Front-Back", -0.15f)
            .Set("Neck Nod Down-Up", 0.35f).Set("Head Nod Down-Up", 0.7f).Set("Jaw Close", -0.6f)
            .Both("Arm Front-Back", -0.65f).Both("Arm Down-Up", 0.45f).Both("Forearm Stretch", 0.3f).Both("Hand Down-Up", 0.1f)
            .Fingers(-0.5f, 0.2f));

        return keys;
    }

    [MenuItem("Tools/Midnight Scour/Build Zombie Pounce Clip")]
    public static void Build()
    {
        List<Key> keys = Keys();
        var curveNames = new HashSet<string>();
        foreach (Key k in keys)
            foreach (string m in k.Curves.Keys)
                curveNames.Add(m);

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, ClipPath);
        }
        clip.ClearCurves();

        foreach (string name in curveNames)
        {
            var curve = new AnimationCurve();
            foreach (Key k in keys)
                curve.AddKey(k.Time, k.Curves.TryGetValue(name, out float v) ? v : 0f);
            Smooth(curve);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), name), curve);
        }

        string[] rootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        var rootCurves = new AnimationCurve[rootNames.Length];
        for (int i = 0; i < rootCurves.Length; i++)
            rootCurves[i] = new AnimationCurve();
        foreach (Key k in keys)
        {
            Quaternion q = Quaternion.Euler(k.Pitch, 0f, 0f);
            float[] values = { k.Body.x, k.Body.y, k.Body.z, q.x, q.y, q.z, q.w };
            for (int i = 0; i < values.Length; i++)
                rootCurves[i].AddKey(k.Time, values[i]);
        }
        for (int i = 0; i < rootNames.Length; i++)
        {
            Smooth(rootCurves[i]);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), rootNames[i]), rootCurves[i]);
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        Debug.Log($"Zombie pounce clip built: {ClipPath} ({clip.length:F2}s)");
    }

    // Every humanoid curve of the clip (muscles and fingers, not the root) at the given time.
    static Dictionary<string, float> Sample(string fbxPath, float time)
    {
        AnimationClip source = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview"))
                source = c;

        var pose = new Dictionary<string, float>();
        if (source == null)
        {
            Debug.LogError($"Pounce builder: no clip in {fbxPath}");
            return pose;
        }
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(source))
            if (b.type == typeof(Animator) && !b.propertyName.StartsWith("Root") && !b.propertyName.StartsWith("Motion"))
                pose[b.propertyName] = AnimationUtility.GetEditorCurve(source, b).Evaluate(time);
        return pose;
    }

    static void Smooth(AnimationCurve curve)
    {
        for (int i = 0; i < curve.length; i++)
            curve.SmoothTangents(i, 0f);
    }
}
