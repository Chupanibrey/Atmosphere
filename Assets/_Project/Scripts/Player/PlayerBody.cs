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
    static readonly int RateHash = Animator.StringToHash("Rate");
    static readonly int JumpHash = Animator.StringToHash("Jump");
    static readonly int FallHash = Animator.StringToHash("Fall");
    static readonly int GroundedHash = Animator.StringToHash("Grounded");
    static readonly int FallingIdleState = Animator.StringToHash("Falling Idle");
    static readonly int LocomotionState = Animator.StringToHash("Locomotion");
    static readonly int CrouchState = Animator.StringToHash("Crouch");
    static readonly int LandState = Animator.StringToHash("Land");
    static readonly int FallState = Animator.StringToHash("Fall");
    static readonly int AirRateHash = Animator.StringToHash("AirRate");
    const float Contact = 74f / 113f;
    const float RiseRate = (50f / 60f) / 0.4515f;
    const float DropRate = (24f / 60f) / 0.4515f;
    const float FallDelay = 0.18f;
    const float FallDropSpeed = 3f;
    const float LongDropSpeed = 5.5f;
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int ClipDistanceId = Shader.PropertyToID("_ClipDistance");

    const float HideNearCamera = 0.12f;
    const float EyeForward = 0.12f;

    [SerializeField] GameObject model;
    [SerializeField] RuntimeAnimatorController controller;
    [SerializeField] float modelScale = 0.887f;
    [SerializeField] float feetLift = 0f;

    PlayerController _player;
    Animator _animator;
    Transform _character;
    Transform[] _feet;
    float _idleFootY;
    bool _idleFootReady;
    bool _landHeld;
    Transform _neck;
    Transform _head;
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

    void Update()
    {
        if (_animator == null)
            return;

        bool climbing = _player.IsClimbing;
        bool crouching = _player.IsCrouching && !climbing;
        float speed = climbing ? 0f : _player.PlanarSpeed;

        _animator.SetFloat(SpeedHash, crouching ? 0f : speed, 0.12f, Time.deltaTime);
        _animator.SetBool(CrouchHash, crouching);
        _animator.SetFloat(RateHash, Stride(speed, crouching));
        UpdateAir(climbing);
    }

    void OnJumped()
    {
        if (_animator == null)
            return;

        _airGraph = true;
        _landHeld = false;
        _airTime = 0f;
        _animator.SetBool(GroundedHash, false);
        _animator.SetFloat(AirRateHash, RiseRate);
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

        _animator.SetBool(GroundedHash, grounded);
        _animator.SetBool(FallHash, falling);

        if (climbing && _airGraph)
        {
            _airGraph = false;
            _animator.ResetTrigger(JumpHash);
            _animator.SetFloat(AirRateHash, 1f);
            _animator.CrossFadeInFixedTime(LocomotionState, 0.15f);
            return;
        }

        if (_animator.IsInTransition(0))
            return;

        var state = _animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != JumpHash)
            return;

        float norm = state.normalizedTime;
        float vy = _player.Velocity.y;

        if (grounded && norm >= 0.98f)
        {
            _airGraph = false;
            _animator.SetFloat(AirRateHash, 1f);
            int next = _animator.GetBool(CrouchHash) ? CrouchState : LocomotionState;
            _animator.CrossFadeInFixedTime(next, 0.16f);
            return;
        }

        // Выше обычного прыжка не доигрываем приземление в воздухе.
        if (!grounded && vy < -LongDropSpeed && norm >= Contact - 0.02f)
        {
            _animator.SetFloat(AirRateHash, 1f);
            _animator.CrossFadeInFixedTime(FallingIdleState, 0.12f);
            return;
        }

        if (grounded && norm < Contact && !_landHeld)
        {
            _landHeld = true;
            _animator.Play(JumpHash, 0, Contact);
            _animator.SetFloat(AirRateHash, 1f);
            return;
        }

        float rate = 1f;
        if (!grounded && vy >= -0.1f)
            rate = RiseRate;
        else if (!grounded)
            rate = DropRate;

        _animator.SetFloat(AirRateHash, rate);
    }

    void LateUpdate()
    {
        PlantSoles();
        CacheEyes();

        float pitch = Mathf.Clamp(_player.Pitch, -75f, 65f);
        Vector3 right = transform.right;
        if (_neck != null)
            _neck.rotation = Quaternion.AngleAxis(pitch * 0.25f, right) * _neck.rotation;
        if (_head != null)
            _head.rotation = Quaternion.AngleAxis(pitch * 0.45f, right) * _head.rotation;
    }

    void PlantSoles()
    {
        if (_character == null || _feet == null)
            return;

        if (!AirPose())
        {
            var rest = _character.localPosition;
            if (rest.y != feetLift)
            {
                rest.y = feetLift;
                _character.localPosition = rest;
            }

            float planted = LowestFoot();
            if (planted < float.MaxValue)
            {
                _idleFootY = planted - transform.position.y;
                _idleFootReady = true;
            }

            return;
        }

        if (!_idleFootReady)
            return;

        float foot = LowestFoot();
        if (foot == float.MaxValue)
            return;

        // Стопа прыжка держится на той же высоте, что и в стойке: без подброса и без входа в пол.
        float gap = foot - (transform.position.y + _idleFootY);
        if (Mathf.Abs(gap) < 0.001f)
            return;

        var pos = _character.localPosition;
        pos.y -= gap;
        _character.localPosition = pos;
    }

    float LowestFoot()
    {
        float lowest = float.MaxValue;
        for (int i = 0; i < _feet.Length; i++)
        {
            if (_feet[i] == null)
                continue;
            if (_feet[i].position.y < lowest)
                lowest = _feet[i].position.y;
        }

        return lowest;
    }

    static Transform[] FindFeet(Transform root)
    {
        var found = new List<Transform>(4);
        CollectFeet(root, found);
        return found.ToArray();
    }

    static void CollectFeet(Transform node, List<Transform> found)
    {
        if (node.name == "LeftFoot" || node.name == "RightFoot" || node.name == "LeftToeBase" || node.name == "RightToeBase")
            found.Add(node);

        for (int i = 0; i < node.childCount; i++)
            CollectFeet(node.GetChild(i), found);
    }

    bool AirPose()
    {
        if (_animator == null)
            return false;

        if (_animator.IsInTransition(0))
            return IsAir(_animator.GetNextAnimatorStateInfo(0));

        return IsAir(_animator.GetCurrentAnimatorStateInfo(0));
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
        _feet = FindFeet(instance.transform);

        if (_leftEye == null && _rightEye == null)
            _leftEye = _head;

        _animator = instance.GetComponentInChildren<Animator>();
        if (_animator == null)
            return;

        if (controller != null)
            _animator.runtimeAnimatorController = controller;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.SetFloat(RateHash, 1f);
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
                renderer.enabled = false;
            else if (_bodyShader != null)
                renderer.materials = HideInterior(renderer.sharedMaterials, created);
        }

        for (int i = 0; i < node.childCount; i++)
            BindHierarchy(node.GetChild(i), created);
    }

    static float Stride(float speed, bool crouching)
    {
        if (!crouching)
            return 1f;

        if (speed < 0.08f)
            return 0f;

        return Mathf.Clamp(speed / 0.75f, 0.8f, 1.35f);
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

    static bool IsHidden(string name)
    {
        return name.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("hair", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("eyeball", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("teeth", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("lash", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
