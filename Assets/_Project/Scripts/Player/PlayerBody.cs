using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Тело в мире. Камера стоит у глаз, голова не рисуется, взгляд вниз показывает грудь снаружи.
/// </summary>
[DefaultExecutionOrder(150)]
[RequireComponent(typeof(PlayerController))]
public class PlayerBody : MonoBehaviour
{
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int CrouchHash = Animator.StringToHash("Crouch");
    static readonly int CrouchState = Animator.StringToHash("Crouch");
    static readonly int BackHash = Animator.StringToHash("Back");
    static readonly int RateHash = Animator.StringToHash("Rate");
    static readonly int JumpHash = Animator.StringToHash("Jump");
    static readonly int FallHash = Animator.StringToHash("Fall");
    static readonly int GroundedHash = Animator.StringToHash("Grounded");
    static readonly int FallingIdleState = Animator.StringToHash("Falling Idle");
    static readonly int LocomotionState = Animator.StringToHash("Locomotion");
    static readonly int LandState = Animator.StringToHash("Land");
    static readonly int FallState = Animator.StringToHash("Fall");
    static readonly int AirRateHash = Animator.StringToHash("AirRate");
    const float FallDelay = 0.18f;
    const float FallDropSpeed = 3f;
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int ClipDistanceId = Shader.PropertyToID("_ClipDistance");

    const float HideNearCamera = 0.12f;
    const float EyeForward = 0.12f;
    const float AnkleClearanceFallback = 0.07f;
    const string HeadLayer = "PlayerHead";

    [SerializeField] GameObject model;
    [SerializeField] RuntimeAnimatorController controller;
    [SerializeField] float modelScale = 0.887f;
    [SerializeField] float feetLift = 0f;

    PlayerController _player;
    Animator _animator;
    Transform _character;
    bool _crouchSent;
    bool _backSent;
    bool _groundedSent;
    bool _fallSent;
    bool _hasPlant;
    bool _leftGround;
    float _rateSent = -1f;
    float _airRateSent = -1f;
    Vector3 _plantPosition;
    Transform _neck;
    Transform _head;
    Transform _hips;
    Transform _spine;
    Transform _chest;
    Transform _upperChest;
    Transform _leftUpper;
    Transform _leftLower;
    Transform _leftFoot;
    Transform _rightUpper;
    Transform _rightLower;
    Transform _rightFoot;
    Transform _leftToes;
    Transform _rightToes;
    static readonly Quaternion SolePitch = Quaternion.Euler(-20f, 0f, 0f);
    static readonly Quaternion ToePitch = Quaternion.Euler(20f, 0f, 0f);
    float _moveYaw;
    float _moveYawVelocity;
    int _yawParts = 1;
    bool _inTransition;
    AnimatorStateInfo _currentState;
    AnimatorStateInfo _nextState;
    float _ankleClearance = AnkleClearanceFallback;
    bool _ankleCalibrated;
    Transform _leftEye;
    Transform _rightEye;
    Material[] _bodyMaterials;
    Vector3 _eyeLocal;
    bool _hasEye;
    float _airTime;
    bool _airGraph;
    static Shader _bodyShader;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        Spawn();
    }

    void OnEnable()
    {
        _player.Jumped += OnJumped;
    }

    void OnDisable()
    {
        _player.Jumped -= OnJumped;
    }

    void OnDestroy()
    {
        if (_animator != null)
            _animator.speed = 1f;

        if (_bodyMaterials == null)
            return;

        for (int i = 0; i < _bodyMaterials.Length; i++)
        {
            if (_bodyMaterials[i] != null)
                Destroy(_bodyMaterials[i]);
        }
    }

    public bool TryGetEyeLocal(out Vector3 local)
    {
        local = _eyeLocal;
        return _hasEye;
    }

    public float MoveScale { get; private set; } = 1f;

    void Update()
    {
        if (_animator == null)
            return;

        ReadLayer();
        bool climbing = _player.IsClimbing;
        bool crouching = _player.IsCrouching && !climbing;
        float speed = climbing ? 0f : _player.PlanarSpeed;

        _animator.SetFloat(SpeedHash, crouching ? 0f : speed, 0.12f, Time.deltaTime);
        if (crouching != _crouchSent)
        {
            _crouchSent = crouching;
            _animator.SetBool(CrouchHash, crouching);
        }

        float rate = crouching ? speed : 1f;
        if (crouching)
            _animator.SetFloat(RateHash, rate, 0.12f, Time.deltaTime);
        else if (_rateSent != 1f)
            _animator.SetFloat(RateHash, 1f);
        _rateSent = rate;

        bool back = WantsBack(crouching, climbing);
        if (back != _backSent)
        {
            _backSent = back;
            _animator.SetBool(BackHash, back);
        }

        UpdateAir(climbing);
        MoveScale = LandingMoveScale();
    }

    bool WantsBack(bool crouching, bool climbing)
    {
        if (crouching || climbing || !_player.IsGrounded)
            return false;

        Vector3 velocity = _player.Velocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;
        if (speed < 0.2f)
            return false;

        // Назад тело не разворачиваем: для этого есть отдельный шаг.
        return Vector3.Dot(velocity / speed, transform.forward) < -0.2f;
    }

    void OnJumped()
    {
        if (_animator == null)
            return;

        _airGraph = true;
        _leftGround = false;
        _hasPlant = false;
        _groundedSent = false;
        _airRateSent = 1f;
        _airTime = 0f;
        _animator.SetBool(GroundedHash, false);
        _animator.SetFloat(AirRateHash, 1f);
        _animator.ResetTrigger(JumpHash);
        _animator.SetTrigger(JumpHash);
    }

    void UpdateAir(bool climbing)
    {
        bool grounded = _player.IsGrounded;
        if (grounded || climbing)
            _airTime = 0f;
        else
            _airTime += Time.deltaTime;

        // Короткая ступенька не дотягивает до падения. Длинный срыв — уже да.
        bool falling = _airTime > FallDelay && _player.Velocity.y < -FallDropSpeed;
        if (falling)
            _airGraph = true;

        if (grounded != _groundedSent)
        {
            _groundedSent = grounded;
            _animator.SetBool(GroundedHash, grounded);
        }

        if (falling != _fallSent)
        {
            _fallSent = falling;
            _animator.SetBool(FallHash, falling);
        }

        if (climbing && _airGraph)
        {
            _airGraph = false;
            _leftGround = false;
            _animator.ResetTrigger(JumpHash);
            SetAirRate(1f);
            _animator.CrossFadeInFixedTime(LocomotionState, 0.15f);
            return;
        }
    }

    void SetAirRate(float rate)
    {
        if (rate == _airRateSent)
            return;

        _airRateSent = rate;
        _animator.SetFloat(AirRateHash, rate);
    }

    void LateUpdate()
    {
        if (_animator != null)
            ReadLayer();

        FollowJump();
        PlantSoles();
        SettleCrouchFeet();
        TurnLegsToMove();
        CacheEyes();

        float pitch = Mathf.Clamp(_player.Pitch, -75f, 65f);
        if (Mathf.Abs(pitch) >= 0.01f)
        {
            Vector3 right = transform.right;
            if (_neck != null)
                _neck.rotation = Quaternion.AngleAxis(pitch * 0.25f, right) * _neck.rotation;
            if (_head != null)
                _head.rotation = Quaternion.AngleAxis(pitch * 0.45f, right) * _head.rotation;
        }

        ApplyPeekLean();
    }

    void ReadLayer()
    {
        _inTransition = _animator.IsInTransition(0);
        _currentState = _animator.GetCurrentAnimatorStateInfo(0);
        if (_inTransition)
            _nextState = _animator.GetNextAnimatorStateInfo(0);
    }

    void FollowJump()
    {
        if (!_airGraph || _animator == null)
            return;

        // Контроллер уже обновил землю в этом кадре. В Update флаг ещё прошлый.
        bool grounded = _player.IsGrounded;
        if (!grounded)
            _leftGround = true;

        if (_inTransition)
        {
            // Смешивание в полёт не должно переживать касание пола.
            if (_leftGround && grounded && _nextState.shortNameHash != LandState)
            {
                _airGraph = false;
                _animator.CrossFadeInFixedTime(LandState, 0.08f);
            }

            return;
        }

        int hash = _currentState.shortNameHash;
        if (hash != JumpHash && hash != FallState && hash != FallingIdleState)
            return;

        if (_leftGround && grounded)
        {
            _airGraph = false;
            _animator.CrossFadeInFixedTime(LandState, 0.08f);
            return;
        }

        // Отрыв доигрывается целиком. Полёт только если к концу клипа ноги ещё в воздухе.
        if (hash == JumpHash && _currentState.normalizedTime >= 0.98f)
            _animator.CrossFadeInFixedTime(FallState, 0.1f);
    }

    float LandingMoveScale()
    {
        if (_animator == null)
            return 1f;

        if (_inTransition)
        {
            if (_nextState.shortNameHash == LandState)
                return Recover(_nextState.normalizedTime);
            if (_currentState.shortNameHash == LandState)
                return Recover(_currentState.normalizedTime);
            return 1f;
        }

        if (_currentState.shortNameHash != LandState)
            return 1f;

        return Recover(_currentState.normalizedTime);
    }

    static float Recover(float norm)
    {
        // Шаг только когда подъём из удара уже закончен, не в середине клипа.
        float t = Mathf.InverseLerp(0.82f, 1f, norm);
        return t * t * (3f - 2f * t);
    }

    void PlantSoles()
    {
        if (_character == null || _player == null)
            return;

        if (!_player.IsGrounded || AirPose())
        {
            _hasPlant = false;
            return;
        }

        Vector3 position = transform.position;
        if (!_ankleCalibrated && !_player.IsCrouching && !InCrouchState())
            RememberAnkleClearance(position.y + _character.localPosition.y);

        // Стоя на месте пол не меняется. Сдвиг больше миллиметра снова сажает подошву.
        if (_hasPlant && (position - _plantPosition).sqrMagnitude < 0.000001f)
            return;

        Vector3 origin = position + Vector3.up * 0.35f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore))
            return;

        _hasPlant = true;
        _plantPosition = position;

        // Подошва в нуле модели. Капсула из-за skinWidth стоит выше коллайдера, поэтому сажаем кроссовок на сам пол.
        float gap = position.y + _character.localPosition.y - hit.point.y;
        if (Mathf.Abs(gap) < 0.001f)
            return;

        var pos = _character.localPosition;
        pos.y -= gap;
        _character.localPosition = pos;
    }

    void RememberAnkleClearance(float modelY)
    {
        if (_ankleCalibrated)
            return;

        float lowest = LowestAnkleY();
        if (lowest > 10000f)
            return;

        float clearance = lowest - modelY;
        if (clearance < 0.02f || clearance > 0.2f)
            return;

        _ankleClearance = clearance;
        _ankleCalibrated = true;
    }

    float LowestAnkleY()
    {
        float lowest = float.MaxValue;
        if (_leftFoot != null)
            lowest = _leftFoot.position.y;
        if (_rightFoot != null && _rightFoot.position.y < lowest)
            lowest = _rightFoot.position.y;
        return lowest;
    }

    bool InCrouchState()
    {
        if (_animator == null)
            return false;

        if (_inTransition)
        {
            if (_currentState.shortNameHash == CrouchState || _nextState.shortNameHash == CrouchState)
                return true;
            return false;
        }

        return _currentState.shortNameHash == CrouchState;
    }

    void SettleCrouchFeet()
    {
        if (_player == null || !_player.IsGrounded || AirPose())
            return;
        if (!_player.IsCrouching && !InCrouchState())
            return;

        SettleLeg(_leftUpper, _leftLower, _leftFoot, true);
        SettleLeg(_rightUpper, _rightLower, _rightFoot, false);
    }

    void SettleLeg(Transform hip, Transform knee, Transform foot, bool left)
    {
        if (hip == null || knee == null || foot == null)
            return;

        Vector3 ankle = foot.position;
        Vector3 origin = new Vector3(ankle.x, transform.position.y + 0.35f, ankle.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore))
            return;

        Vector3 ground = hit.normal.y > 0.65f ? hit.normal : Vector3.up;
        float floor = hit.point.y + _ankleClearance;
        bool near = ankle.y < floor + 0.2f;
        bool folded = Vector3.Dot(foot.up, Vector3.up) < -0.45f;
        if (!near && !folded && !FootFacesBack(foot))
            return;

        if (near && Mathf.Abs(ankle.y - floor) > 0.004f)
        {
            ankle.y = floor;
            BendKnee(hip, knee, foot, ankle);
        }

        LayFoot(foot, FootAim(left, ground), ground, left);
    }

    void TurnLegsToMove()
    {
        if (_hips == null || _spine == null || _player == null)
            return;

        float target = 0f;
        Vector3 velocity = _player.Velocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;
        bool steer = !_player.IsCrouching && !_player.IsClimbing && _player.IsGrounded && !AirPose();
        if (steer && speed > 0.2f)
        {
            Vector3 direction = velocity / speed;
            // Шаг назад смотрит в −forward. Его тоже доворачиваем к скорости, не разворачивая тело.
            Vector3 facing = Vector3.Dot(direction, transform.forward) < -0.2f
                ? -transform.forward
                : transform.forward;
            // Полный стрейф на 90° скручивает поясницу. Диагонали хватает меньшего угла.
            target = Vector3.SignedAngle(facing, direction, Vector3.up);
            target = Mathf.Clamp(target, -35f, 35f);
        }

        _moveYaw = Mathf.SmoothDampAngle(_moveYaw, target, ref _moveYawVelocity, 0.1f);
        if (Mathf.Abs(_moveYaw) < 0.05f)
            return;

        // Ноги чуть в сторону хода. Разворот делится по позвонкам, грудь остаётся к камере.
        Vector3 pivot = transform.position;
        _hips.RotateAround(pivot, Vector3.up, _moveYaw);
        float step = -_moveYaw / _yawParts;
        _spine.RotateAround(pivot, Vector3.up, step);
        if (_chest != null)
            _chest.RotateAround(pivot, Vector3.up, step);
        if (_upperChest != null)
            _upperChest.RotateAround(pivot, Vector3.up, step);
    }

    void ApplyPeekLean()
    {
        if (_player == null)
            return;

        // Прошлый крен крутил корень сетки, и кроссовки уезжали вместе с ним.
        if (_character != null)
            _character.localRotation = Quaternion.identity;

        if (_hips == null)
            return;

        float side = _player.LeanOffset.x;
        if (Mathf.Abs(side) < 0.0001f)
            return;

        float bend = Mathf.Atan2(side, 1.6f) * Mathf.Rad2Deg;
        Vector3 leftPos = default;
        Vector3 rightPos = default;
        Quaternion leftRot = default;
        Quaternion rightRot = default;
        if (_leftFoot != null)
        {
            leftPos = _leftFoot.position;
            leftRot = _leftFoot.rotation;
        }

        if (_rightFoot != null)
        {
            rightPos = _rightFoot.position;
            rightRot = _rightFoot.rotation;
        }

        _hips.RotateAround(transform.position, transform.forward, bend);

        // Кость стопы возвращаем как была: подошва на полу, голень остаётся наклонённой.
        if (_leftFoot != null)
            _leftFoot.SetPositionAndRotation(leftPos, leftRot);
        if (_rightFoot != null)
            _rightFoot.SetPositionAndRotation(rightPos, rightRot);
    }

    Vector3 FootAim(bool left, Vector3 groundNormal)
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, groundNormal);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            return transform.forward;

        float yaw = left ? 16f : -16f;
        return Quaternion.AngleAxis(yaw, groundNormal) * forward.normalized;
    }

    bool FootFacesBack(Transform foot)
    {
        Vector3 toes = Vector3.ProjectOnPlane(foot.up, Vector3.up);
        if (toes.sqrMagnitude < 0.01f)
            return true;

        return Vector3.Dot(toes.normalized, transform.forward) < 0.2f;
    }

    static void BendKnee(Transform hip, Transform knee, Transform foot, Vector3 target)
    {
        Vector3 hipPos = hip.position;
        Vector3 kneePos = knee.position;
        float upper = Vector3.Distance(hipPos, kneePos);
        float lower = Vector3.Distance(kneePos, foot.position);
        if (upper < 0.001f || lower < 0.001f)
            return;

        Vector3 toTarget = target - hipPos;
        float reach = toTarget.magnitude;
        float max = upper + lower - 0.001f;
        float min = Mathf.Abs(upper - lower) + 0.001f;
        reach = Mathf.Clamp(reach, min, max);
        if (toTarget.sqrMagnitude < 0.0001f)
            toTarget = Vector3.down;
        toTarget = toTarget.normalized * reach;

        Vector3 bend = Vector3.ProjectOnPlane(kneePos - hipPos, toTarget);
        if (bend.sqrMagnitude < 0.0001f)
            bend = Vector3.Cross(toTarget, hip.right);
        if (bend.sqrMagnitude < 0.0001f)
            return;

        float adjacent = (upper * upper - lower * lower + reach * reach) / (2f * reach);
        float height = Mathf.Sqrt(Mathf.Max(upper * upper - adjacent * adjacent, 0f));
        Vector3 kneeTarget = hipPos + toTarget.normalized * adjacent + bend.normalized * height;

        hip.rotation = Quaternion.FromToRotation(kneePos - hipPos, kneeTarget - hipPos) * hip.rotation;
        knee.rotation = Quaternion.FromToRotation(foot.position - knee.position, target - knee.position) * knee.rotation;
    }

    void LayFoot(Transform foot, Vector3 toes, Vector3 groundNormal, bool left)
    {
        if (toes.sqrMagnitude < 0.0001f)
            return;

        if (groundNormal.sqrMagnitude < 0.0001f)
            groundNormal = Vector3.up;

        // Наклон как в обычной стойке: пятка и носок на одной высоте.
        foot.rotation = Quaternion.LookRotation(groundNormal.normalized, toes.normalized) * SolePitch;
        Transform toe = left ? _leftToes : _rightToes;
        if (toe != null)
            toe.localRotation = ToePitch;
    }

    bool AirPose()
    {
        if (_animator == null)
            return false;

        if (_inTransition)
            return IsAir(_nextState);

        return IsAir(_currentState);
    }

    static bool IsAir(AnimatorStateInfo state)
    {
        int hash = state.shortNameHash;
        return hash == JumpHash || hash == FallState || hash == FallingIdleState || hash == LandState;
    }

    void CacheEyes()
    {
        _hasEye = false;
        if (_leftEye == null && _rightEye == null)
            return;

        Vector3 world = _leftEye != null && _rightEye != null
            ? (_leftEye.position + _rightEye.position) * 0.5f
            : (_leftEye != null ? _leftEye.position : _rightEye.position);
        _eyeLocal = transform.InverseTransformPoint(world);
        _eyeLocal.z += EyeForward;
        _hasEye = true;
    }

    void Spawn()
    {
        if (model == null)
            return;

        var instance = Instantiate(model, transform);
        instance.name = "Character";
        _character = instance.transform;
        _character.localPosition = new Vector3(0f, feetLift, 0f);
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * modelScale;

        _bodyShader ??= Shader.Find("Atmosphere/FirstPersonBody");
        var created = new List<Material>(8);
        BindHierarchy(instance.transform, created);
        _bodyMaterials = created.ToArray();

        if (_leftEye == null && _rightEye == null)
            _leftEye = _head;

        IgnoreHeadLayer();

        _animator = instance.GetComponentInChildren<Animator>();
        if (_animator == null)
            return;

        if (controller != null)
            _animator.runtimeAnimatorController = controller;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.SetFloat(RateHash, 1f);
        _leftUpper = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        _leftLower = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        _rightUpper = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        _rightLower = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        _leftToes = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
        _rightToes = _animator.GetBoneTransform(HumanBodyBones.RightToes);
        _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
        _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
        _upperChest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
        _yawParts = 1;
        if (_chest != null)
            _yawParts++;
        if (_upperChest != null)
            _yawParts++;
    }

    // Слой виден в сцене и в тенях. Игровая камера его не рисует, поэтому голова не забивает обзор.
    void IgnoreHeadLayer()
    {
        int layer = LayerMask.NameToLayer(HeadLayer);
        if (layer < 0)
            return;

        var cameras = GetComponentsInChildren<Camera>(true);
        int mask = ~(1 << layer);
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].cullingMask &= mask;
    }

    void BindHierarchy(Transform node, List<Material> created)
    {
        switch (node.name)
        {
            case "Neck1":
                _neck = node;
                break;
            case "Neck":
                _neck ??= node;
                break;
            case "Head":
                _head = node;
                break;
            case "LeftEye":
                _leftEye = node;
                break;
            case "RightEye":
                _rightEye = node;
                break;
        }

        if (node.TryGetComponent(out Renderer renderer))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (renderer is SkinnedMeshRenderer skin)
                skin.updateWhenOffscreen = true;

            if (IsHidden(node.name))
                ConcealFromPlayer(renderer);
            else if (_bodyShader != null)
                renderer.materials = HideInterior(renderer.sharedMaterials, created);
        }

        for (int i = 0; i < node.childCount; i++)
            BindHierarchy(node.GetChild(i), created);
    }

    static Material[] HideInterior(Material[] source, List<Material> created)
    {
        var next = new Material[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var material = new Material(_bodyShader);
            CopySurface(source[i], material);
            material.SetFloat(ClipDistanceId, HideNearCamera);
            created.Add(material);
            next[i] = material;
        }

        return next;
    }

    static void CopySurface(Material source, Material destination)
    {
        if (source == null)
            return;

        if (source.HasProperty(BaseMapId))
            destination.SetTexture(BaseMapId, source.GetTexture(BaseMapId));
        else if (source.HasProperty(MainTexId))
            destination.SetTexture(BaseMapId, source.GetTexture(MainTexId));

        if (source.HasProperty(BaseColorId))
            destination.SetColor(BaseColorId, source.GetColor(BaseColorId));
        else if (source.HasProperty(ColorId))
            destination.SetColor(BaseColorId, source.GetColor(ColorId));

        if (!source.HasProperty(BumpMapId))
            return;

        var bump = source.GetTexture(BumpMapId);
        if (bump == null)
            return;

        destination.SetTexture(BumpMapId, bump);
        destination.EnableKeyword("_NORMALMAP");
    }

    static void ConcealFromPlayer(Renderer renderer)
    {
        int layer = LayerMask.NameToLayer(HeadLayer);
        if (layer < 0)
        {
            renderer.enabled = false;
            return;
        }

        renderer.gameObject.layer = layer;
    }

    static bool IsHidden(string name)
    {
        return name.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("hair", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("eyeball", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("teeth", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("lash", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
