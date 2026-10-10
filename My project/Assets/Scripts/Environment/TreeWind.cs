using UnityEngine;

/// <summary>
/// Drives the sway of every material using the "Midnight Scour/Lit Wind" shader. The wind blows along this
/// object's forward axis. Without an enabled TreeWind in the scene the trees stand still.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class TreeWind : MonoBehaviour
{
    [Tooltip("How far a point 10 m up a tree sways, in meters. Taller trees bend more, the base never moves.")]
    [SerializeField, Range(0f, 1f)] float strength = 0.08f;
    [Tooltip("At 1 a tree sways back and forth about once every 6 seconds.")]
    [SerializeField, Range(0f, 3f)] float speed = 0.8f;
    [Tooltip("0 keeps the sway steady, 1 lets it swell and drop in slow gusts.")]
    [SerializeField, Range(0f, 1f)] float gustiness = 0.6f;
    [Tooltip("How far leaf cards flutter, in meters.")]
    [SerializeField, Range(0f, 0.1f)] float leafFlutter = 0.02f;

    static readonly int WindId = Shader.PropertyToID("_MSTreeWind");
    static readonly int WindParamsId = Shader.PropertyToID("_MSTreeWindParams");

    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void Update() => Apply();

    void OnDisable()
    {
        Shader.SetGlobalVector(WindId, Vector4.zero);
        Shader.SetGlobalVector(WindParamsId, Vector4.zero);
    }

    void Apply()
    {
        var forward = transform.forward;
        var direction = new Vector2(forward.x, forward.z);
        direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector2.right;
        Shader.SetGlobalVector(WindId, new Vector4(direction.x, direction.y, strength, speed));
        Shader.SetGlobalVector(WindParamsId, new Vector4(gustiness, leafFlutter, 0f, 0f));
    }
}
