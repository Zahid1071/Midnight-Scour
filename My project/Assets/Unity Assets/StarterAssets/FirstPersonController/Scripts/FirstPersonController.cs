using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

		[Tooltip("Sneak speed of the character in m/s")]
		public float SneakSpeed = 1.5f;

		[Header("Directional Sprint")]
		[Tooltip("Sprint speed multiplier when running diagonally or sideways (any left/right key held).")]
		[Range(0f, 1f)] public float SprintStrafeMultiplier = 0.9f;
		[Tooltip("Sprint speed multiplier when running straight backwards.")]
		[Range(0f, 1f)] public float SprintBackwardMultiplier = 0.6f;

		[Header("Direction Change Spam")]
		[Tooltip("How quickly the movement direction follows the keys (per second). Lower = heavier turns. 0 = instant.")]
		public float DirectionResponsiveness = 12f;
		[Tooltip("Smallest key-direction change that counts (W -> W+A is 45, W+A -> W+D is 90, A -> D is 180).")]
		public float DirectionChangeMinAngle = 60f;
		[Tooltip("Speed lost per recent direction change. The first change in a while costs nothing; each quick one after it costs more.")]
		[Range(0f, 1f)] public float DirectionChangeDropPerStreak = 0.15f;
		[Tooltip("Most speed direction-change spam can take away.")]
		[Range(0f, 1f)] public float DirectionChangeMaxDrop = 0.5f;
		[Tooltip("How fast recent direction changes are forgotten (changes per second). 1 = a change counts for about a second.")]
		public float DirectionChangeStreakDecay = 1f;
		[Tooltip("How fast the speed climbs back to full after a spam penalty (fraction per second).")]
		public float DirectionChangeRecovery = 0.6f;

		[Tooltip("Safety cap on how fast the CharacterController is allowed to be shoved by colliders (e.g. a door closing on the player) in a single frame. Prevents physics depenetration from ever reading as an instant 'launch' - the player still gets pushed out, just never faster than this speed.")]
		public float MaxExternalPushSpeed = 5.0f;

		private bool _isSneaking = false;

		public bool IsSneaking => _isSneaking;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		private float _speed;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;

		// direction change
		private Vector3 _moveDirection;
		private Vector2 _lastInputDirection;
		private float _lastInputTime = -1f;
		private float _directionChangeStreak;
		private float _speedCap = 1f;
		private bool _directionChangedThisFrame;
		private const float InputMemorySeconds = 0.3f;

		// control gate
		private bool _wasInputBlocked;
		private bool _resumeFromRest;
		private const float ResumeMaxDeltaTime = 1f / 30f;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;


#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

			// reset our timeouts on start
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;

			CaptureContinue.RememberPlayerSpawn(transform);
		}

		private void Update()
		{

			if (PlayerControlGate.IsInputBlocked)
			{
				_wasInputBlocked = true;
				return;
			}
			if (_wasInputBlocked)
			{
				_wasInputBlocked = false;
				ResumeAfterBlock();
			}
			JumpAndGravity();
			GroundedCheck();
			Move();
		}

		// Nothing called Move while the gate was held, so the controller's velocity and the cached move direction
		// are still the ones from before it, and a cinematic may have turned the body since. Start again from
		// standing, on the keys actually held now.
		private void ResumeAfterBlock()
		{
			_speed = 0f;
			_moveDirection = Vector3.zero;
			_speedCap = 1f;
			_directionChangeStreak = 0f;
			_lastInputTime = -1f;
			_resumeFromRest = true;

#if ENABLE_INPUT_SYSTEM
			if (_input == null || _playerInput == null || _playerInput.actions == null)
				return;
			InputAction move = _playerInput.actions.FindAction("Move");
			if (move != null)
				_input.move = move.enabled ? move.ReadValue<Vector2>() : Vector2.zero;
			InputAction sprint = _playerInput.actions.FindAction("Sprint");
			if (sprint != null)
				_input.sprint = sprint.enabled && sprint.IsPressed();
			_input.jump = false;
#endif
		}

		private void LateUpdate()
		{
			//CameraRotation();
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void CameraRotation()
		{
			// if there is an input
			if (_input.look.sqrMagnitude >= _threshold)
			{
				//Don't multiply mouse input by Time.deltaTime
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;
				
				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// Update Cinemachine camera target pitch
				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}
		}

		private void Move()
		{
			// Sneak while holding Ctrl; sprint overrides sneak
#if ENABLE_INPUT_SYSTEM
			_isSneaking = Keyboard.current.leftCtrlKey.isPressed && !Keyboard.current.leftShiftKey.isPressed;
#else
			_isSneaking = Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.LeftShift);
#endif

			// set target speed based on move speed, sprint speed and if sprint is pressed
			float targetSpeed;
			if (_isSneaking)
				targetSpeed = SneakSpeed;
			else if (_input.sprint)
				targetSpeed = Mathf.Max(MoveSpeed, SprintSpeed * SprintDirectionMultiplier(_input.move));
			else
				targetSpeed = MoveSpeed;

			TrackDirectionChange();
			targetSpeed *= _speedCap;


			// a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

			// note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is no input, set the target speed to 0
			if (_input.move == Vector2.zero) targetSpeed = 0.0f;

			// The first frame back from the gate also carries whatever hitch ended it (the auto-save writes right as
			// the wall reveal hands control back), so it doesn't get to cover the whole stalled frame in one step.
			float dt = _resumeFromRest ? Mathf.Min(Time.deltaTime, ResumeMaxDeltaTime) : Time.deltaTime;

			// a reference to the players current horizontal velocity
			float currentHorizontalSpeed = _resumeFromRest ? 0f : new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
			_resumeFromRest = false;

			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			// accelerate or decelerate to target speed
			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				// creates curved result rather than a linear one giving a more organic speed change
				// note T in Lerp is clamped, so we don't need to clamp our speed
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, dt * SpeedChangeRate);

				// round speed to 3 decimal places
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed;
			}

			if (_directionChangedThisFrame)
				_speed = Mathf.Min(_speed, targetSpeed * inputMagnitude);

			// The movement direction eases toward the keys instead of snapping. It stays unit length: the
			// speed is read back from the controller's velocity, so a shortened direction would compound.
			Vector3 inputDirection = Vector3.zero;
			if (_input.move != Vector2.zero)
				inputDirection = (transform.right * _input.move.x + transform.forward * _input.move.y).normalized;
			if (DirectionResponsiveness > 0f && _input.move != Vector2.zero && _moveDirection != Vector3.zero)
			{
				Vector3 blended = Vector3.Lerp(_moveDirection, inputDirection, 1f - Mathf.Exp(-DirectionResponsiveness * dt));
				_moveDirection = blended.sqrMagnitude > 0.01f ? blended.normalized : inputDirection;
			}
			else
			{
				_moveDirection = inputDirection;
			}

			// move the player
			Vector3 horizontalMove = _moveDirection * (_speed * dt);
			Vector3 positionBeforeMove = transform.position;

			_controller.Move(horizontalMove + new Vector3(0.0f, _verticalVelocity, 0.0f) * dt);

			ClampSuddenPush(positionBeforeMove, horizontalMove, dt);
		}

		// Full sprint straight ahead; any left/right key held costs a flat amount, straight back costs more.
		private float SprintDirectionMultiplier(Vector2 move)
		{
			if (move == Vector2.zero)
				return 1f;
			if (Mathf.Abs(move.x) > 0.01f)
				return SprintStrafeMultiplier;
			return move.y < 0f ? SprintBackwardMultiplier : 1f;
		}

		// Spam guard: a sharp change of key direction (including a quick release and press of another key) is
		// free on its own, but each one that follows soon after cuts the speed cap harder. The cap climbs back
		// over time and the count of recent changes wears off.
		private void TrackDirectionChange()
		{
			float dt = Time.deltaTime;
			_directionChangedThisFrame = false;
			_directionChangeStreak = Mathf.Max(0f, _directionChangeStreak - DirectionChangeStreakDecay * dt);
			_speedCap = Mathf.Min(1f, _speedCap + DirectionChangeRecovery * dt);

			if (_input.move == Vector2.zero)
				return;

			Vector2 direction = _input.move.normalized;
			if (_lastInputTime >= 0f && Time.time - _lastInputTime <= InputMemorySeconds
				&& Vector2.Angle(_lastInputDirection, direction) >= DirectionChangeMinAngle)
			{
				float drop = Mathf.Min(DirectionChangeMaxDrop, DirectionChangeDropPerStreak * _directionChangeStreak);
				if (drop > 0f)
				{
					float currentSpeed = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z).magnitude;
					float speedFactor = SprintSpeed > 0f ? Mathf.Clamp01(currentSpeed / SprintSpeed) : 1f;
					_speedCap = Mathf.Min(_speedCap, 1f - drop * speedFactor);
					_directionChangedThisFrame = true;
				}
				float maxStreak = DirectionChangeDropPerStreak > 0f ? DirectionChangeMaxDrop / DirectionChangeDropPerStreak : 0f;
				_directionChangeStreak = Mathf.Min(maxStreak, _directionChangeStreak + 1f);
			}
			_lastInputDirection = direction;
			_lastInputTime = Time.time;
		}

		// Colliders that are animated directly (e.g. a closing door) can sweep through the
		// CharacterController without going through the physics/velocity pipeline. Unity resolves
		// that overlap by shoving the controller out in a single Move() call, which - since it
		// ignores frame time - can look like the player got instantly launched at absurd speed.
		// Here we let the push happen (so the player still gets physically ejected, and never gets
		// stuck overlapping geometry), but clamp how far it's allowed to move us in one frame so it
		// never exceeds a sane speed.
		private void ClampSuddenPush(Vector3 positionBeforeMove, Vector3 intendedHorizontalMove, float dt)
		{
			Vector3 actualDelta = transform.position - positionBeforeMove;
			Vector3 actualHorizontal = new Vector3(actualDelta.x, 0.0f, actualDelta.z);

			float maxHorizontal = intendedHorizontalMove.magnitude + MaxExternalPushSpeed * dt;
			float maxVertical = Mathf.Abs(_verticalVelocity) * dt + MaxExternalPushSpeed * dt;

			Vector3 clamped = actualDelta;

			if (actualHorizontal.sqrMagnitude > maxHorizontal * maxHorizontal)
			{
				Vector3 limitedH = actualHorizontal.normalized * maxHorizontal;
				clamped.x = limitedH.x;
				clamped.z = limitedH.z;
			}

			if (Mathf.Abs(actualDelta.y) > maxVertical)
				clamped.y = Mathf.Sign(actualDelta.y) * maxVertical;

			if ((clamped - actualDelta).sqrMagnitude <= 0.0000001f)
				return;

			// Never write transform.position on a live CharacterController — that itself can floor-punch.
			bool wasEnabled = _controller.enabled;
			_controller.enabled = false;
			transform.position = positionBeforeMove + clamped;
			Physics.SyncTransforms();
			_controller.enabled = wasEnabled;
		}

		private void JumpAndGravity()
		{
			if (Grounded)
			{
				// reset the fall timeout timer
				_fallTimeoutDelta = FallTimeout;

				// stop our velocity dropping infinitely when grounded
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				// Jump
				if (_input.jump && _jumpTimeoutDelta <= 0.0f)
				{
					// the square root of H * -2 * G = how much velocity needed to reach desired height
					_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
				}

				// jump timeout
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}
			else
			{
				// reset the jump timeout timer
				_jumpTimeoutDelta = JumpTimeout;

				// fall timeout
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				// if we are not grounded, do not jump
				_input.jump = false;
			}

			// apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}
		}

		public void ResetMovementState()
		{
			_verticalVelocity = -2f;
			_speed = 0f;
			_moveDirection = Vector3.zero;
			_speedCap = 1f;
			_directionChangeStreak = 0f;
			_lastInputTime = -1f;
			Grounded = true;
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;

			if (_input != null)
			{
				_input.move = Vector2.zero;
				_input.jump = false;
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}