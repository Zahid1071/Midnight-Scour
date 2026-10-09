using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Short damped rotational wobble layered on top of a Cinemachine camera's final orientation.
/// Added at runtime by minigames; does not touch the camera's own settings.
/// </summary>
[AddComponentMenu("")]
public class CameraKick : CinemachineExtension
{
    Vector2 _direction;
    float _amplitude;
    float _frequency;
    float _duration;
    float _startTime = -1f;

    /// <param name="degrees">Peak rotation.</param>
    /// <param name="frequency">Wobble frequency in Hz.</param>
    /// <param name="duration">Seconds until fully settled.</param>
    public void Kick(float degrees, float frequency, float duration)
    {
        _direction = Random.insideUnitCircle.normalized;
        _amplitude = degrees;
        _frequency = frequency;
        _duration = Mathf.Max(0.01f, duration);
        _startTime = Time.time;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Noise || _startTime < 0f)
            return;

        float t = Time.time - _startTime;
        if (t >= _duration)
        {
            _startTime = -1f;
            return;
        }

        float envelope = 1f - t / _duration;
        float wobble = Mathf.Sin(t * _frequency * 2f * Mathf.PI) * _amplitude * envelope * envelope;
        Vector2 offset = _direction * wobble;
        state.OrientationCorrection *= Quaternion.Euler(offset.x, offset.y, 0f);
    }
}
