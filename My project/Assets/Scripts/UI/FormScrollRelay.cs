using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Relays wheel events from a control that steals scroll (e.g. TMP multiline input)
/// to an outer <see cref="ScrollRect"/>.
/// </summary>
public sealed class FormScrollRelay : MonoBehaviour, IScrollHandler
{
    ScrollRect _target;

    public void Init(ScrollRect target) => _target = target;

    public void OnScroll(PointerEventData eventData)
    {
        if (_target != null)
            _target.OnScroll(eventData);
    }
}
