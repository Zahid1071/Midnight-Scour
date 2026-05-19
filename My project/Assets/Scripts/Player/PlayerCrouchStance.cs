using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Sneak/crouch (hold Ctrl): shrinks CharacterController and matching CapsuleCollider.
/// Camera height is driven via <see cref="EyeBaseLocalY"/> — HeadBobController owns the transform.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(50)]
public class PlayerCrouchStance : MonoBehaviour
{
    [Header("References")]
    [SerializeField] CharacterController characterController;
    [SerializeField] CapsuleCollider bodyCapsule;

    [Header("Camera")]
    [Tooltip("How much of the capsule height loss is applied to eye height (local Y).")]
    [SerializeField, Range(0f, 1.2f)] float cameraDropFromCapsuleShrink = 0.85f;

    [Header("Crouch Size")]
    [SerializeField, Range(0.35f, 1f)] float crouchHeightRatio = 0.55f;
    [SerializeField] float crouchStepOffset = 0.15f;
    [SerializeField] float transitionSpeed = 12f;



    struct CapsuleDimensions
    {
        public float height;
        public Vector3 center;
        public float stepOffset;
    }

    CapsuleDimensions _standController;
    CapsuleDimensions _standBodyCapsule;
    bool _hasBodyCapsule;
    float _crouchControllerHeight;
    float _crouchBodyCapsuleHeight;
    float _blend;
    float _standEyeLocalY;
    float _crouchEyeLocalY;
    PlayerHider _hider;

    public float CrouchBlend => _blend;

    /// <summary>Eye anchor local Y (X/Z come from HeadBob default). Updated each frame.</summary>
    public float EyeBaseLocalY { get; private set; }

    void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (bodyCapsule == null)
            bodyCapsule = GetComponent<CapsuleCollider>();


        _hider = GetComponent<PlayerHider>();

        _standController = new CapsuleDimensions
        {
            height = characterController.height,
            center = characterController.center,
            stepOffset = characterController.stepOffset,
        };

        _crouchControllerHeight = _standController.height * crouchHeightRatio;

        _hasBodyCapsule = bodyCapsule != null;
        if (_hasBodyCapsule)
        {
            _standBodyCapsule = new CapsuleDimensions
            {
                height = bodyCapsule.height,
                center = bodyCapsule.center,
            };
            _crouchBodyCapsuleHeight = _standBodyCapsule.height * crouchHeightRatio;
        }

        var cameraRoot = transform.Find("PlayerCameraRoot");
        if (cameraRoot != null)
        {
            _standEyeLocalY = cameraRoot.localPosition.y;
            float capsuleShrink = _standController.height - _crouchControllerHeight;
            _crouchEyeLocalY = _standEyeLocalY - capsuleShrink * cameraDropFromCapsuleShrink;
            EyeBaseLocalY = _standEyeLocalY;
        }
    }

    void Update()
    {
        if (characterController == null)
            return;

        if (PlayerControlGate.IsInputBlocked || (_hider != null && _hider.isHidden))
        {
            ApplyStance(0f);
            return;
        }

        float targetBlend = WantsCrouch() ? 1f : 0f;
        _blend = Mathf.MoveTowards(_blend, targetBlend, transitionSpeed * Time.deltaTime);
        ApplyStance(_blend);
    }

    static bool WantsCrouch()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null
            && Keyboard.current.leftCtrlKey.isPressed
            && !Keyboard.current.leftShiftKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.LeftShift);
#endif
    }

    void ApplyStance(float blend)
    {
        float controllerHeight = Mathf.Lerp(_standController.height, _crouchControllerHeight, blend);
        characterController.height = controllerHeight;
        characterController.center = new Vector3(
            _standController.center.x,
            BottomFixedCenterY(_standController.center.y, _standController.height, controllerHeight),
            _standController.center.z);
        characterController.stepOffset = Mathf.Lerp(_standController.stepOffset, crouchStepOffset, blend);

        if (_hasBodyCapsule)
        {
            float capsuleHeight = Mathf.Lerp(_standBodyCapsule.height, _crouchBodyCapsuleHeight, blend);
            bodyCapsule.height = capsuleHeight;
            bodyCapsule.center = new Vector3(
                _standBodyCapsule.center.x,
                BottomFixedCenterY(_standBodyCapsule.center.y, _standBodyCapsule.height, capsuleHeight),
                _standBodyCapsule.center.z);
        }

        EyeBaseLocalY = Mathf.Lerp(_standEyeLocalY, _crouchEyeLocalY, blend);
    }

    static float BottomFixedCenterY(float standCenterY, float standHeight, float targetHeight)
    {
        float bottom = standCenterY - standHeight * 0.5f;
        return bottom + targetHeight * 0.5f;
    }
}
