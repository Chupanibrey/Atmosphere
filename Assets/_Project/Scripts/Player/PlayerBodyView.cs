using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Тело от первого лица: видны ноги и нижняя часть торса, когда игрок смотрит вниз.
/// Самостоятельный скрипт, остальные файлы не изменяются.
/// Анимации (Humanoid, In Place) берутся из полей ниже, Animator Controller не нужен.
/// Темп ног подстраивается под stepsPerSecond из PlayerHeadBob.Settings.
/// </summary>
[DefaultExecutionOrder(310)] // после PlayerController (200) и PlayerHeadBob (300)
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerHeadBob))]
public class PlayerBodyView : MonoBehaviour
{
    const int Idle = 0;
    const int Walk = 1;
    const int Run = 2;
    const int CrouchIdle = 3;
    const int CrouchWalk = 4;
    const int ClipCount = 5;

    [Header("Модель")]
    [Tooltip("Персонаж с Mixamo (FBX с Humanoid-ригом). Тащите сюда FBX или префаб.")]
    [SerializeField] GameObject bodyPrefab;
    [SerializeField] float modelScale = 1f;
    [Tooltip("Сдвиг модели назад от камеры, чтобы камера не оказывалась внутри груди.")]
    [SerializeField] float backOffset = 0.2f;
    [SerializeField] bool hideHead = true;
    [Tooltip("Прячет собственные руки модели (у вас свои руки с монтировкой).")]
    [SerializeField] bool hideArms = true;
    [Tooltip("Отключает капсулу «Body» игрока, она больше не нужна.")]
    [SerializeField] bool hidePlayerCapsule = true;
    [Tooltip("Скрывать тело, пока игрок на лестнице.")]
    [SerializeField] bool hideWhileClimbing = true;

    [Header("Анимации (Humanoid, In Place)")]
    [SerializeField] AnimationClip idle;
    [SerializeField] AnimationClip walk;
    [Tooltip("Если пусто, используется walk.")]
    [SerializeField] AnimationClip run;
    [Tooltip("Если пусто, используется idle.")]
    [SerializeField] AnimationClip crouchIdle;
    [Tooltip("Если пусто, используется walk.")]
    [SerializeField] AnimationClip crouchWalk;

    [Header("Плавность")]
    [SerializeField] float blendSpeed = 8f;

    PlayerController _player;
    PlayerHeadBob _bob;
    GameObject _instance;
    Animator _animator;
    Renderer[] _renderers;
    Transform _head;
    Transform _leftArm;
    Transform _rightArm;

    PlayableGraph _graph;
    AnimationMixerPlayable _mixer;
    readonly AnimationClip[] _clips = new AnimationClip[ClipCount];
    readonly AnimationClipPlayable[] _playables = new AnimationClipPlayable[ClipCount];
    readonly float[] _weights = new float[ClipCount];
    readonly float[] _times = new float[ClipCount];
    readonly float[] _target = new float[ClipCount];
    bool _ready;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _bob = GetComponent<PlayerHeadBob>();
    }

    void Start()
    {
        if (bodyPrefab == null)
        {
            Debug.LogError("PlayerBodyView: не назначена модель тела.");
            return;
        }

        if (idle == null || walk == null)
        {
            Debug.LogError("PlayerBodyView: нужны минимум клипы idle и walk.");
            return;
        }

        BuildInstance();
        BuildGraph();

        if (hidePlayerCapsule)
        {
            Transform capsule = transform.Find("Body");
            if (capsule != null && capsule.TryGetComponent(out Renderer capsuleRenderer))
                capsuleRenderer.enabled = false;
        }
    }

    void OnDestroy()
    {
        if (_graph.IsValid())
            _graph.Destroy();
    }

    void BuildInstance()
    {
        _instance = Instantiate(bodyPrefab, transform);
        _instance.name = "Body View";
        _instance.transform.localPosition = new Vector3(0f, 0f, -backOffset);
        _instance.transform.localRotation = Quaternion.identity;
        _instance.transform.localScale = Vector3.one * modelScale;

        // Коллайдеры модели мешали бы CharacterController.
        foreach (var col in _instance.GetComponentsInChildren<Collider>(true))
            Destroy(col);
        foreach (var cam in _instance.GetComponentsInChildren<Camera>(true))
            cam.enabled = false;
        foreach (var lightSource in _instance.GetComponentsInChildren<Light>(true))
            lightSource.enabled = false;

        _renderers = _instance.GetComponentsInChildren<Renderer>(true);
        foreach (var skin in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skin.updateWhenOffscreen = true;

        _animator = _instance.GetComponentInChildren<Animator>(true);
        if (_animator == null)
        {
            Debug.LogError("PlayerBodyView: на модели нет Animator. Проверьте, что в импорте стоит Rig -> Humanoid.");
            return;
        }

        _animator.runtimeAnimatorController = null;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (_animator.avatar == null || !_animator.avatar.isHuman)
        {
            Debug.LogWarning("PlayerBodyView: у модели нет Humanoid-аватара, голова и руки не будут скрыты.");
            return;
        }

        _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        _leftArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _rightArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
    }

    void BuildGraph()
    {
        if (_animator == null)
            return;

        _clips[Idle] = idle;
        _clips[Walk] = walk;
        _clips[Run] = run != null ? run : walk;
        _clips[CrouchIdle] = crouchIdle != null ? crouchIdle : idle;
        _clips[CrouchWalk] = crouchWalk != null ? crouchWalk : walk;

        _graph = PlayableGraph.Create("PlayerBodyView");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        var output = AnimationPlayableOutput.Create(_graph, "Body", _animator);
        _mixer = AnimationMixerPlayable.Create(_graph, ClipCount);
        output.SetSourcePlayable(_mixer);

        for (int i = 0; i < ClipCount; i++)
        {
            var clipPlayable = AnimationClipPlayable.Create(_graph, _clips[i]);
            clipPlayable.SetSpeed(0.0); // время задаём вручную, чтобы привязать к скорости шага
            _graph.Connect(clipPlayable, 0, _mixer, i);
            _playables[i] = clipPlayable;
        }

        _weights[Idle] = 1f;
        _mixer.SetInputWeight(Idle, 1f);
        _graph.Play();
        _ready = true;
    }

    void Update()
    {
        if (!_ready)
            return;

        float dt = Time.deltaTime;

        bool visible = !(hideWhileClimbing && _player.IsClimbing);
        SetVisible(visible);
        if (!visible)
            return;

        HeadBobSettings s = _bob.Settings;
        float planar = _player.PlanarSpeed;
        float gaitSpeed = Mathf.Max(0.01f, _player.CurrentGaitSpeed);
        bool moving = planar > (s != null ? s.minSpeed : 0.15f);
        bool crouching = _player.IsCrouching;
        bool sprinting = _player.CurrentGait == PlayerGait.Sprint;

        int idleIndex = crouching ? CrouchIdle : Idle;
        int moveIndex = crouching ? CrouchWalk : (sprinting ? Run : Walk);

        // Целевые веса: стоим или идём, в нужной позе.
        System.Array.Clear(_target, 0, ClipCount);
        float[] target = _target;
        target[idleIndex] = moving ? 0f : 1f;
        target[moveIndex] = moving ? 1f : 0f;

        float sum = 0f;
        for (int i = 0; i < ClipCount; i++)
        {
            _weights[i] = Mathf.MoveTowards(_weights[i], target[i], blendSpeed * dt);
            sum += _weights[i];
        }

        if (sum < 0.0001f)
        {
            _weights[idleIndex] = 1f;
            sum = 1f;
        }

        float speedRatio = Mathf.Clamp(planar / gaitSpeed, 0f, 1.25f);
        for (int i = 0; i < ClipCount; i++)
        {
            float speed = 1f;
            float cadence = CadenceFor(i, s);
            if (cadence > 0f)
            {
                // В цикле ходьбы два шага: за секунду нужно stepsPerSecond / 2 циклов.
                speed = cadence * speedRatio * _clips[i].length * 0.5f;
            }

            if (_weights[i] > 0.001f)
                _times[i] += dt * speed;

            float length = Mathf.Max(0.01f, _clips[i].length);
            _times[i] = Mathf.Repeat(_times[i], length);
            _playables[i].SetTime(_times[i]);
            _mixer.SetInputWeight(i, _weights[i] / sum);
        }
    }

    void LateUpdate()
    {
        if (!_ready)
            return;

        // Скрываем голову и руки модели: схлопываем кости (после анимации).
        if (hideHead && _head != null)
            _head.localScale = Vector3.zero;
        if (hideArms)
        {
            if (_leftArm != null) _leftArm.localScale = Vector3.zero;
            if (_rightArm != null) _rightArm.localScale = Vector3.zero;
        }
    }

    static float CadenceFor(int index, HeadBobSettings s)
    {
        if (s == null)
            return 0f;

        switch (index)
        {
            case Walk: return s.walk.stepsPerSecond;
            case Run: return s.sprint.stepsPerSecond;
            case CrouchWalk: return s.crouch.stepsPerSecond;
            default: return 0f;
        }
    }

    void SetVisible(bool visible)
    {
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].enabled = visible;
        }
    }
}
