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
    [Tooltip("Обычный шаг, м/с.")]
    public float walkSpeed = 1.5f;
    [Tooltip("Бег, м/с.")]
    public float sprintSpeed = 4.5f;
    [Tooltip("Шаг в приседе, м/с.")]
    public float crouchSpeed = 0.75f;
    [Tooltip("Разгон, м/с².")]
    public float acceleration = 16f;
    [Tooltip("Торможение на земле, м/с².")]
    public float deceleration = 40f;
    [Tooltip("Доля управления в воздухе, 0–1.")]
    public float airControl = 0.15f;
}

[Serializable]
public class PlayerJumpSettings
{
    [Tooltip("Высота прыжка, м.")]
    public float height = 0.5f;
    [Tooltip("Ускорение свободного падения, м/с².")]
    public float gravity = -9.81f;
    public float groundedStick = -8f;
    public float coyoteTime = 0.08f;
    public float bufferTime = 0.08f;
    public LayerMask groundMask = ~0;
}

[Serializable]
public class PlayerCrouchSettings
{
    public bool toggle;
    [Tooltip("Рост стоя, м.")]
    public float standingHeight = 1.8f;
    [Tooltip("Высота капсулы в приседе, м.")]
    public float height = 1.1f;
    [Tooltip("Доля роста до глаз.")]
    public float eyeHeightRatio = 0.97f;
    [Tooltip("Высота камеры от ног в полный рост, м.")]
    public float standingCameraY = 1.75f;
    [Tooltip("Высота камеры от ног в приседе, м.")]
    public float cameraY = 1.07f;
    public float lerpSpeed = 12f;
}

[Serializable]
public class PlayerClimbSettings
{
    [Tooltip("Скорость по лестнице, м/с.")]
    public float speed = 1.2f;
    [Tooltip("Смещение вбок на лестнице, м/с.")]
    public float strafeSpeed = 0.6f;
    public float attachDot = 0.25f;
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
    [SerializeField] Transform bodyVisual;

    CharacterController _controller;
    PlayerLean _lean;
    PlayerBody _body;
    InputAction _moveAction;
    InputAction _lookAction;
    InputAction _jumpAction;
    InputAction _sprintAction;
    InputAction _crouchAction;

    MoveState _state = MoveState.Walking;
    Vector3 _velocity;
    Vector3 _horizontalVelocity;
    Vector3 _planarForward = Vector3.forward;
    Vector3 _planarRight = Vector3.right;
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
    public float PlanarSpeed
    {
        get
        {
            float x = _velocity.x;
            float z = _velocity.z;
            return Mathf.Sqrt(x * x + z * z);
        }
    }
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

    /// <summary>Скорость падения в момент касания земли, м/с. Отрицательная.</summary>
    public event Action<float> Landed;

    public float ConsumeLandingImpact()
    {
        // Кадр удара забирает покачивание камеры. Шаги слушают Landed и этот вызов не делают.
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
        TryGetComponent(out _lean);
        TryGetComponent(out _body);
        CacheActions();

        if (TryGetComponent(out CapsuleCollider capsuleCollider))
            capsuleCollider.enabled = false; // второй коллайдер на той же капсуле выталкивает из пола

        if (look.cameraPivot == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null)
                look.cameraPivot = cam.transform;
        }

        if (bodyVisual == null)
        {
            Transform found = transform.Find("Body");
            if (found != null)
                bodyVisual = found;
        }

        crouch.standingHeight = Mathf.Max(_controller.height, _controller.radius * 2f + 0.05f);
        _targetHeight = crouch.standingHeight;
        ApplyCapsuleHeight(crouch.standingHeight);
        SnapBottomToGround();
        _grounded = true;

        if (HasCamera)
        {
            _cameraRestLocalPos = look.cameraPivot.localPosition;
            _cameraHeightY = EyeHeight(crouch.standingHeight);
        }
    }

    void OnValidate()
    {
        var cc = GetComponent<CharacterController>();
        if (cc == null)
            return;

        float h = Mathf.Max(crouch.standingHeight, cc.radius * 2f + 0.05f);
        crouch.standingHeight = h;
        cc.height = h;
        cc.center = new Vector3(0f, h * 0.5f, 0f);
        crouch.standingCameraY = EyeHeight(h);
        crouch.cameraY = EyeHeight(crouch.height);
        SyncBodyVisual(cc, h);
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
        CachePlanarAxes();
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
        // Глаза анимации, а не формула капсулы: в приседе голова остаётся выше 1.07 м.
        if (_body != null && _body.TryGetEyeLocal(out Vector3 eye))
            pos = eye;
        else
            pos.y = _cameraHeightY;
        look.cameraPivot.localPosition = pos + _bobOffset + _leanOffset;
        look.cameraPivot.localRotation = Quaternion.Euler(
            _pitch + _bobEuler.x + _leanEuler.x,
            _bobEuler.y + _leanEuler.y,
            _bobEuler.z + _leanEuler.z);

        _lean?.KeepCameraInsideWalls();
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
        PlayerActions.Ensure();
        _moveAction = PlayerActions.Move;
        _lookAction = PlayerActions.Look;
        _jumpAction = PlayerActions.Jump;
        _sprintAction = PlayerActions.Sprint;
        _crouchAction = PlayerActions.Crouch;
    }

    void CachePlanarAxes()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        _planarForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

        Vector3 right = transform.right;
        right.y = 0f;
        _planarRight = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
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
        Vector3 delta = _horizontalVelocity - targetHorizontal;
        bool slowing = Vector3.Dot(delta, _horizontalVelocity) > 0f;
        float rate = slowing ? move.deceleration : move.acceleration;
        _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetHorizontal, rate * control * dt);

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
        bool rising = _velocity.y > 0.1f;
        _grounded = !rising && ((flags & CollisionFlags.Below) != 0 || ProbeGround());

        if (!wasGrounded && _grounded)
        {
            _landingImpactSpeed = fallSpeed;
            Landed?.Invoke(fallSpeed);
        }

        // Иначе срыв со ступени уносит groundedStick (−8) в падение и звучит как удар.
        if (wasGrounded && !_grounded && _velocity.y < 0f)
            _velocity.y = 0f;

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

        float targetY = EyeHeight(_isCrouching ? crouch.height : crouch.standingHeight);
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
        return _planarForward;
    }

    Vector3 GetPlanarRight()
    {
        return _planarRight;
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

    float EyeHeight(float bodyHeight)
    {
        float ratio = crouch.eyeHeightRatio > 0.1f ? crouch.eyeHeightRatio : 0.91f;
        return bodyHeight * ratio;
    }

    void ApplyCapsuleHeight(float height)
    {
        float clamped = Mathf.Max(height, _controller.radius * 2f + 0.05f);
        _controller.height = clamped;
        _controller.center = new Vector3(0f, clamped * 0.5f, 0f);
        SyncBodyVisual(_controller, clamped);
    }

    void SyncBodyVisual(CharacterController cc, float height)
    {
        if (bodyVisual == null || bodyVisual == transform)
            return;

        Vector3 pos = bodyVisual.localPosition;
        pos.y = height * 0.5f;
        bodyVisual.localPosition = pos;

        float diameter = cc.radius * 2f;
        bodyVisual.localScale = new Vector3(diameter, height * 0.5f, diameter);
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}