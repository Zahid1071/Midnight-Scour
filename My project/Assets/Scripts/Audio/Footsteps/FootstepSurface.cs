using System.Collections.Generic;
using UnityEngine;

public enum FootstepSurfaceType
{
    Wood,
    Carpet,
    Tile,
    Concrete,
    Metal,
    Dirt,
    Grass,
    Gravel,
}

/// <summary>
/// Forces the footstep surface of this collider (and its children). Without one, the surface is guessed from the
/// collider's physic material, render material and object name, and terrain counts as dirt.
/// </summary>
[DisallowMultipleComponent]
public class FootstepSurface : MonoBehaviour
{
    public FootstepSurfaceType type = FootstepSurfaceType.Wood;

    static readonly Dictionary<Collider, FootstepSurfaceType?> s_cache = new Dictionary<Collider, FootstepSurfaceType?>();
    static readonly RaycastHit[] s_hits = new RaycastHit[8];

    static readonly (string keyword, FootstepSurfaceType type)[] s_keywords =
    {
        ("carpet", FootstepSurfaceType.Carpet),
        ("rug", FootstepSurfaceType.Carpet),
        ("tile", FootstepSurfaceType.Tile),
        ("ceramic", FootstepSurfaceType.Tile),
        ("marble", FootstepSurfaceType.Tile),
        ("metal", FootstepSurfaceType.Metal),
        ("steel", FootstepSurfaceType.Metal),
        ("iron", FootstepSurfaceType.Metal),
        ("grate", FootstepSurfaceType.Metal),
        ("gravel", FootstepSurfaceType.Gravel),
        ("grass", FootstepSurfaceType.Grass),
        ("dirt", FootstepSurfaceType.Dirt),
        ("mud", FootstepSurfaceType.Dirt),
        ("soil", FootstepSurfaceType.Dirt),
        ("terrain", FootstepSurfaceType.Dirt),
        ("concrete", FootstepSurfaceType.Concrete),
        ("cement", FootstepSurfaceType.Concrete),
        ("asphalt", FootstepSurfaceType.Concrete),
        ("stone", FootstepSurfaceType.Concrete),
        ("brick", FootstepSurfaceType.Concrete),
        ("wood", FootstepSurfaceType.Wood),
        ("plank", FootstepSurfaceType.Wood),
        ("floorboard", FootstepSurfaceType.Wood),
        ("parquet", FootstepSurfaceType.Wood),
        ("laminate", FootstepSurfaceType.Wood),
    };

    /// <summary>Surface under <paramref name="feet"/>, ignoring colliders under <paramref name="ignoreRoot"/>.</summary>
    public static FootstepSurfaceType Detect(Vector3 feet, Transform ignoreRoot, FootstepSurfaceType fallback)
    {
        int count = Physics.RaycastNonAlloc(feet + Vector3.up * 0.5f, Vector3.down, s_hits, 1.5f, ~0,
            QueryTriggerInteraction.Ignore);

        Collider best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = s_hits[i].collider;
            if (c == null || s_hits[i].distance >= bestDistance)
                continue;
            if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot))
                continue;
            if (c is CharacterController)
                continue;
            best = c;
            bestDistance = s_hits[i].distance;
        }

        if (best == null)
            return fallback;
        return Classify(best) ?? fallback;
    }

    static FootstepSurfaceType? Classify(Collider c)
    {
        if (s_cache.TryGetValue(c, out FootstepSurfaceType? cached))
            return cached;

        FootstepSurfaceType? result = null;
        FootstepSurface tag = c.GetComponentInParent<FootstepSurface>();
        if (tag != null)
            result = tag.type;
        else if (c is TerrainCollider)
            result = FootstepSurfaceType.Dirt;
        else
        {
            result = Match(c.sharedMaterial != null ? c.sharedMaterial.name : null);
            if (result == null)
            {
                Renderer r = c.GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    result = Match(r.sharedMaterial.name);
            }
            if (result == null)
                result = Match(c.gameObject.name);
        }

        s_cache[c] = result;
        return result;
    }

    static FootstepSurfaceType? Match(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        name = name.ToLowerInvariant();
        foreach ((string keyword, FootstepSurfaceType type) in s_keywords)
        {
            if (name.Contains(keyword))
                return type;
        }
        return null;
    }
}
