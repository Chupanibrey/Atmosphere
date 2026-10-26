using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerGait
{
    Walk,
    Sprint,
    Crouch,
    Climb
}

/// <summary>
/// Движение FPS-персонажа: ходьба, бег, присед, прыжок, лестницы, обзор.
/// </summary>
[DefaultExecutionOrder(200)]
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    enum MoveState
    {
        Walking,
        Climbing
    }

    [SerializeField] PlayerLookSettings look = new();
    [SerializeField] PlayerMoveSettings move = new();
    [SerializeField] PlayerJumpSettings jump = new();
    [SerializeField] PlayerCrouchSettings crouch = new();
    [SerializeField] PlayerClimbSettings climb = new();

    CharacterController _controller;
    InputAction _moveAction;
    InputAction _lookAction;
    InputAction _jumpAction;
    InputAction _sprintAction;
    InputAction _crouchAction;

    MoveState _state = MoveState.Walking;
    Vector3 _velocity;
    Vector3 _horizontalVelocity;
    float _pitch;
    float _coyoteTimer;
    float _jumpBufferTimer;
    float _targetHeight;
    bool _grounded;
    bool _isCrouching;
    bool _wantCrouch;
    bool _isSprinting;
    Ladder _nearbyLadder;
    Ladder _activeLadder;
    Vector3 _cameraRestLocalPos;
    float _cameraHeightY;
    float _landingImpactSpeed;
    Vector3 _bobOffset;
    Vector3 _bobEuler;
    Vector3 _leanOffset;
    Vector3 _leanEuler;

    public bool HasCamera => look.cameraPivot != null && look.cameraPivot != transform;
    public bool IsGrounded => _grounded;
    public bool IsCrouching => _isCrouching;
    public bool IsClimbing => _state == MoveState.Climbing;
    public bool IsSprinting => _isSprinting;
    public Vector3 Velocity => _velocity;
    public float PlanarSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;
    public float Pitch => _pitch;
    public float CameraHeightY => _cameraHeightY;
    public Vector3 CameraRestLocalPos => _cameraRestLocalPos;

    public PlayerGait CurrentGait
    {
        get
        {
            if (IsClimbing)
                return PlayerGait.Climb;
            if (_isCrouching)
                return PlayerGait.Crouch;
            if (_isSprinting)
                return PlayerGait.Sprint;
            return PlayerGait.Walk;
        }
    }

    public float CurrentGaitSpeed
    {
        get
        {
            return CurrentGait switch
            {
                PlayerGait.Climb => climb.speed,
                PlayerGait.Crouch => move.crouchSpeed,
                PlayerGait.Sprint => move.sprintSpeed,
                _ => move.walkSpeed
            };
        }
    }

    public float ConsumeLandingImpact()
    {
        // Один раз отдаём скорость удара о землю, чтобы bob не читал её каждый кадр.
        float impact = _landingImpactSpeed;
        _landingImpactSpeed = 0f;
        return impact;
    }

    public Transform CameraPivot => look.cameraPivot;

    public void SetCameraBob(Vector3 offset, Vector3 euler)
    {
        _bobOffset = offset;
        _bobEuler = euler;
    }

    public void SetCameraLean(Vector3 offset, Vector3 euler)
    {
        _leanOffset = offset;
        _leanEuler = euler;
    }

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        CacheActions();

        if (TryGetComponent(out CapsuleCollider capsuleCollider))
            capsuleCollider.enabled = false; // второй коллайдер на той же капсуле выталкивает из пола

        if (look.cameraPivot == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null)
                look.cameraPivot = cam.transform;
        }

        _targetHeight = crouch.standingHeight;
        ApplyCapsuleHeight(crouch.standingHeight);
        SnapBottomToGround();
        _grounded = true;

        if (HasCamera)
        {
            _cameraRestLocalPos = look.cameraPivot.localPosition;
            _cameraHeightY = crouch.standingCameraY;
        }
    }

    void OnEnable()
    {
        _moveAction?.Enable();
        _lookAction?.Enable();
        _jumpAction?.Enable();
        _sprintAction?.Enable();
        _crouchAction?.Enable();

        if (look.lockCursorOnStart)
            SetCursorLocked(true);
    }

    void OnDisable()
    {
        _moveAction?.Disable();
        _lookAction?.Disable();
        _jumpAction?.Disable();
        _sprintAction?.Disable();
        _crouchAction?.Disable();
        SetCursorLocked(false);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector2 moveInput = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
        Vector2 lookInput = _lookAction != null ? _lookAction.ReadValue<Vector2>() : Vector2.zero;

        Vector3 euler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, euler.y, 0f);

        HandleLook(lookInput);
        HandleCrouchInput();
        UpdateJumpBuffer();

        if (_state == MoveState.Climbing)
            TickClimbing(moveInput, dt);
        else
            TickWalking(moveInput, dt);

        _isSprinting = _sprintAction != null && _sprintAction.IsPressed() && !_isCrouching && !IsClimbing;
        UpdateCrouchVisuals(dt);
    }

    void LateUpdate()
    {
        if (!HasCamera)
            return;

        Vector3 pos = _cameraRestLocalPos;
        pos.y = _cameraHeightY;
        look.cameraPivot.localPosition = pos + _bobOffset + _leanOffset;
        look.cameraPivot.localRotation = Quaternion.Euler(
            _pitch + _bobEuler.x + _leanEuler.x,
            _bobEuler.y + _leanEuler.y,
            _bobEuler.z + _leanEuler.z);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out Ladder ladder))
            _nearbyLadder = ladder;
    }

    void OnTriggerStay(Collider other)
    {
        if (other.TryGetComponent(out Ladder ladder))
            _nearbyLadder = ladder;
    }

    void OnTriggerExit(Collider other)
    {
        if (_nearbyLadder != null && other.TryGetComponent(out Ladder ladder) && ladder == _nearbyLadder)
            _nearbyLadder = null;

        if (_activeLadder != null && other.TryGetComponent(out Ladder active) && active == _activeLadder)
            ExitLadder();
    }

    void CacheActions()
    {
        var actions = InputSystem.actions;
        if (actions == null)
        {
            Debug.LogError("PlayerController: не найден Input Action Asset.");
            return;
        }

        _moveAction = actions.FindAction("Player/Move", throwIfNotFound: false);
        _lookAction = actions.FindAction("Player/Look", throwIfNotFound: false);
        _jumpAction = actions.FindAction("Player/Jump", throwIfNotFound: false);
        _sprintAction = actions.FindAction("Player/Sprint", throwIfNotFound: false);
        _crouchAction = actions.FindAction("Player/Crouch", throwIfNotFound: false);
    }

    void HandleLook(Vector2 lookInput)
    {
        if (!HasCamera)
            return;

        transform.Rotate(0f, lookInput.x * look.mouseSensitivity, 0f, Space.World);
        _pitch = Mathf.Clamp(_pitch - lookInput.y * look.mouseSensitivity, look.minPitch, look.maxPitch);
    }

    void HandleCrouchInput()
    {
        if (_crouchAction == null || _state == MoveState.Climbing)
            return;

        if (crouch.toggle)
        {
            if (_crouchAction.WasPressedThisFrame())
                _wantCrouch = !_wantCrouch;
        }
        else
        {
            _wantCrouch = _crouchAction.IsPressed();
        }
    }

    void UpdateJumpBuffer()
    {
        if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
            _jumpBufferTimer = jump.bufferTime;
        else
            _jumpBufferTimer -= Time.deltaTime;
    }

    void TickWalking(Vector2 moveInput, float dt)
    {
        bool wasGrounded = _grounded;
        float fallSpeed = _velocity.y; // до Move: после приземления вертикаль уже обнулена
        _coyoteTimer = _grounded ? jump.coyoteTime : _coyoteTimer - dt;

        TryAttachToLadder(moveInput);
        if (_state == MoveState.Climbing)
            return;

        UpdateCrouchState();

        float targetSpeed = GetTargetSpeed(moveInput);
        Vector3 wishDir = GetWorldMoveDirection(moveInput);
        float control = _grounded ? 1f : move.airControl;
        Vector3 targetHorizontal = wishDir * targetSpeed;
        _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetHorizontal, move.acceleration * control * dt);

        if (_jumpBufferTimer > 0f && _coyoteTimer > 0f && !_isCrouching)
        {
            PerformJump();
        }
        else if (_grounded)
        {
            _velocity.y = jump.groundedStick; // лёгкое вдавливание, чтобы не дребезжать на плоскости
        }
        else
        {
            _velocity.y += jump.gravity * dt;
        }

        _velocity.x = _horizontalVelocity.x;
        _velocity.z = _horizontalVelocity.z;

        CollisionFlags flags = _controller.Move(_velocity * dt);
        _grounded = (flags & CollisionFlags.Below) != 0 || ProbeGround(); // isGrounded на Plane часто врёт

        if (!wasGrounded && _grounded)
            _landingImpactSpeed = fallSpeed;

        if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f)
            _velocity.y = 0f;
    }

    void TickClimbing(Vector2 moveInput, float dt)
    {
        if (_activeLadder == null)
        {
            ExitLadder();
            return;
        }

        if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
        {
            ExitLadder();
            PerformJump();
            return;
        }

        Vector3 up = _activeLadder.ClimbUp;
        Vector3 right = _activeLadder.ClimbRight;
        Vector3 climbVelocity = up * moveInput.y * climb.speed + right * moveInput.x * climb.strafeSpeed;

        _horizontalVelocity = Vector3.ProjectOnPlane(climbVelocity, Vector3.up);
        _velocity = climbVelocity;
        _controller.Move(_velocity * dt);
        _grounded = false;
    }

    void TryAttachToLadder(Vector2 moveInput)
    {
        if (_nearbyLadder == null || moveInput.sqrMagnitude < 0.01f)
            return;

        Vector3 wishDir = GetWorldMoveDirection(moveInput);
        float towardsLadder = Vector3.Dot(wishDir, _nearbyLadder.AttachDirection);
        float lookingAtLadder = Vector3.Dot(GetPlanarForward(), _nearbyLadder.AttachDirection);
        // Не садимся на лестницу, если просто проходим рядом боком.
        if (towardsLadder < climb.attachDot && lookingAtLadder < climb.attachDot && moveInput.y <= 0.1f)
            return;

        _activeLadder = _nearbyLadder;
        _state = MoveState.Climbing;
        _wantCrouch = false;
        _isCrouching = false;
        _velocity = Vector3.zero;
        _horizontalVelocity = Vector3.zero;
        _targetHeight = crouch.standingHeight;
    }

    void ExitLadder()
    {
        _activeLadder = null;
        _state = MoveState.Walking;
        _velocity.y = 0f;
    }

    void UpdateCrouchState()
    {
        if (_wantCrouch)
        {
            _isCrouching = true;
            _targetHeight = crouch.height;
            return;
        }

        if (_isCrouching && CanStandUp())
        {
            _isCrouching = false;
            _targetHeight = crouch.standingHeight;
        }
    }

    void UpdateCrouchVisuals(float dt)
    {
        float blend = 1f - Mathf.Exp(-crouch.lerpSpeed * dt);
        float nextHeight = Mathf.Lerp(_controller.height, _targetHeight, blend);
        if (!Mathf.Approximately(nextHeight, _controller.height))
            ApplyCapsuleHeight(nextHeight);

        float targetY = _isCrouching ? crouch.cameraY : crouch.standingCameraY;
        _cameraHeightY = Mathf.Lerp(_cameraHeightY, targetY, blend);
    }

    float GetTargetSpeed(Vector2 moveInput)
    {
        if (moveInput.sqrMagnitude < 0.0001f)
            return 0f;

        bool sprinting = _sprintAction != null && _sprintAction.IsPressed() && !_isCrouching && moveInput.y > 0.1f;
        if (_isCrouching)
            return move.crouchSpeed;
        return sprinting ? move.sprintSpeed : move.walkSpeed;
    }

    Vector3 GetWorldMoveDirection(Vector2 moveInput)
    {
        Vector3 dir = GetPlanarRight() * moveInput.x + GetPlanarForward() * moveInput.y;
        dir.y = 0f;
        if (dir.sqrMagnitude > 1f)
            dir.Normalize();
        return dir;
    }

    Vector3 GetPlanarForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    Vector3 GetPlanarRight()
    {
        Vector3 right = transform.right;
        right.y = 0f;
        return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
    }

    Vector3 GetCapsuleBottom()
    {
        return transform.position + _controller.center + Vector3.down * (_controller.height * 0.5f);
    }

    bool ProbeGround()
    {
        float radius = _controller.radius * 0.9f;
        Vector3 origin = GetCapsuleBottom() + Vector3.up * (radius + _controller.skinWidth);
        float distance = _controller.skinWidth + 0.12f;
        return Physics.SphereCast(
            origin,
            radius,
            Vector3.down,
            out _,
            distance,
            jump.groundMask,
            QueryTriggerInteraction.Ignore);
    }

    void SnapBottomToGround()
    {
        Vector3 bottom = GetCapsuleBottom();
        Vector3 origin = bottom + Vector3.up * 3f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f, jump.groundMask, QueryTriggerInteraction.Ignore))
            return;

        float delta = hit.point.y - bottom.y;
        if (Mathf.Abs(delta) > 0.001f)
            transform.position += Vector3.up * (delta + _controller.skinWidth);
    }

    void PerformJump()
    {
        _jumpBufferTimer = 0f;
        _coyoteTimer = 0f;
        _grounded = false;
        _velocity.y = Mathf.Sqrt(jump.height * -2f * jump.gravity); // из высоты, не «сила прыжка»
    }

    bool CanStandUp()
    {
        float extraHeight = crouch.standingHeight - _controller.height;
        if (extraHeight <= 0.01f)
            return true;

        Vector3 origin = transform.position + _controller.center + Vector3.up * (_controller.height * 0.5f);
        return !Physics.SphereCast(
            origin,
            _controller.radius * 0.9f,
            Vector3.up,
            out _,
            extraHeight + 0.05f,
            jump.groundMask,
            QueryTriggerInteraction.Ignore);
    }

    void ApplyCapsuleHeight(float height)
    {
        float clamped = Mathf.Max(height, _controller.radius * 2f + 0.05f);
        _controller.height = clamped;
        // Pivot у стандартной Capsule в центре: сдвигаем center, ноги на уровне земли.
        float centerY = (clamped - crouch.standingHeight) * 0.5f;
        _controller.center = new Vector3(0f, centerY, 0f);
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

[Serializable]
public class PlayerLookSettings
{
    public Transform cameraPivot;
    public float mouseSensitivity = 0.12f;
    public float minPitch = -85f;
    public float maxPitch = 85f;
    public bool lockCursorOnStart = true;
}

[Serializable]
public class PlayerMoveSettings
{
    public float walkSpeed = 8f;
    public float sprintSpeed = 16f;
    public float crouchSpeed = 4f;
    public float acceleration = 40f;
    public float airControl = 0.35f;
}

[Serializable]
public class PlayerJumpSettings
{
    public float height = 2f;
    public float gravity = -30f;
    public float groundedStick = -8f;
    public float coyoteTime = 0.12f;
    public float bufferTime = 0.12f;
    public LayerMask groundMask = ~0;
}

[Serializable]
public class PlayerCrouchSettings
{
    public bool toggle;
    public float standingHeight = 2f;
    public float height = 1f;
    public float standingCameraY = 0.7f;
    public float cameraY = 0.25f;
    public float lerpSpeed = 12f;
}

[Serializable]
public class PlayerClimbSettings
{
    public float speed = 4f;
    public float strafeSpeed = 1.6f;
    public float attachDot = 0.25f;
}
