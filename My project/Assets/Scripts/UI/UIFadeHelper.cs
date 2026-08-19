using UnityEngine;

/// <summary>
/// Smoothly fades a UI panel via CanvasGroup alpha.
/// Call <see cref="SetVisible"/> each frame; the helper lerps toward the target.
/// </summary>
public class UIFadeHelper
{
    readonly GameObject _root;
    readonly CanvasGroup _group;
    readonly float _fadeDuration;
    float _targetAlpha;

    public UIFadeHelper(GameObject root, float fadeDuration = 0.2f)
    {
        _root = root;
        _fadeDuration = Mathf.Max(0.01f, fadeDuration);

        if (root != null)
        {
            _group = root.GetComponent<CanvasGroup>();
            if (_group == null)
                _group = root.AddComponent<CanvasGroup>();

            _targetAlpha = root.activeSelf ? 1f : 0f;
            _group.alpha = _targetAlpha;
        }
    }

    public void SetVisible(bool visible, bool instant = false)
    {
        if (_root == null || _group == null)
            return;

        _targetAlpha = visible ? 1f : 0f;

        if (visible && !_root.activeSelf)
        {
            _root.SetActive(true);
            _group.alpha = instant ? 1f : 0f;
        }

        if (instant)
        {
            _group.alpha = _targetAlpha;
        }
        else
        {
            float speed = Time.unscaledDeltaTime / _fadeDuration;
            _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, speed);
        }

        _group.interactable = _group.alpha > 0.01f;
        _group.blocksRaycasts = _group.alpha > 0.01f;

        if (_group.alpha <= 0f && _root.activeSelf)
            _root.SetActive(false);
    }

    /// <summary>True while the alpha has not yet reached the target.</summary>
    public bool IsFading =>
        _group != null && Mathf.Abs(_group.alpha - _targetAlpha) > 0.001f;
}
