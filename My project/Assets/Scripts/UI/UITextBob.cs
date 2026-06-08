using UnityEngine;

/// <summary>Gentle vertical sin bob for small UI prompts.</summary>
[RequireComponent(typeof(RectTransform))]
public class UITextBob : MonoBehaviour
{
    [SerializeField] private float amplitude = 5f;
    [SerializeField] private float speed = 2.2f;

    RectTransform _rect;
    Vector2 _restPosition;
    bool _hasRest;

    void Awake()
    {
        _rect = transform as RectTransform;
        CacheRest();
    }

    void OnEnable()
    {
        CacheRest();
    }

    void OnDisable()
    {
        if (_hasRest && _rect != null)
            _rect.anchoredPosition = _restPosition;
    }

    void Update()
    {
        if (_rect == null)
            return;

        float offset = Mathf.Sin(Time.unscaledTime * speed) * amplitude;
        _rect.anchoredPosition = _restPosition + new Vector2(0f, offset);
    }

    void CacheRest()
    {
        if (_rect == null)
            _rect = transform as RectTransform;

        if (_rect == null)
            return;

        _restPosition = _rect.anchoredPosition;
        _hasRest = true;
    }
}
