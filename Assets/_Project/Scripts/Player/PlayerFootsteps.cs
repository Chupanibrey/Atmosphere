using UnityEngine;

/// <summary>
/// Шаги и приземление. Такт берётся у камеры, удар о землю — у контроллера.
/// Клип выбирается по полу под ногами, чтобы следующее покрытие не требовало нового скрипта.
/// </summary>
[DefaultExecutionOrder(290)]
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerHeadBob))]
public class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] FootstepClips defaultClips;
    [SerializeField] FootstepClips[] surfaces;
    [Range(0f, 1f)] [SerializeField] float walkVolume = 0.6f;
    [SerializeField] float sprintVolumeMul = 1.35f;
    [SerializeField] float crouchVolumeMul = 0.45f;
    [SerializeField] float climbVolumeMul = 0.7f;
    [SerializeField] float volumeJitter = 0.12f;
    [SerializeField] Vector2 pitchRange = new(0.94f, 1.06f);
    [SerializeField] LayerMask groundMask = ~0;

    const int Voices = 4;

    PlayerController _player;
    PlayerHeadBob _bob;
    AudioSource[] _voices;
    int _voice;
    int _lastStep = -1;
    int _lastLand = -1;
    float _lastY;
    float _airStartY;
    bool _wasGrounded = true;
    float _blockBobStep;
    readonly RaycastHit[] _hits = new RaycastHit[8];

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _bob = GetComponent<PlayerHeadBob>();
        var root = new GameObject("Footstep Voices");
        root.transform.SetParent(transform, false);
        root.hideFlags = HideFlags.HideInHierarchy;

        _voices = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++)
        {
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 128;
            _voices[i] = source;
        }

        _lastY = transform.position.y;
        _airStartY = _lastY;
    }

    void LateUpdate()
    {
        float y = transform.position.y;
        float rise = y - _lastY;
        bool grounded = _player.IsGrounded && !_player.IsClimbing;

        if (_wasGrounded && !grounded)
            _airStartY = _lastY;

        // Вверх ступень забирается за один кадр. Вниз капсула срывается и касается пола уже в OnLanded.
        if (grounded && rise >= 0.1f && rise <= 0.45f)
            PlayStair();

        _wasGrounded = grounded;
        _lastY = y;
    }

    void OnEnable()
    {
        if (_bob != null)
            _bob.Stepped += OnStepped;
        if (_player != null)
            _player.Landed += OnLanded;
    }

    void OnDisable()
    {
        if (_bob != null)
            _bob.Stepped -= OnStepped;
        if (_player != null)
            _player.Landed -= OnLanded;
    }

    void OnStepped()
    {
        if (Time.time < _blockBobStep)
            return;

        if (_player.IsClimbing)
        {
            Play(Resolve().Steps, ref _lastStep, walkVolume * climbVolumeMul);
            return;
        }

        if (!_player.IsGrounded)
            return;

        PlayStep();
    }

    void PlayStep()
    {
        float gaitMul = _player.CurrentGait switch
        {
            PlayerGait.Sprint => sprintVolumeMul,
            PlayerGait.Crouch => crouchVolumeMul,
            PlayerGait.Climb => climbVolumeMul,
            _ => 1f
        };

        Play(Resolve().Steps, ref _lastStep, walkVolume * gaitMul);
    }

    void PlayStair()
    {
        _blockBobStep = Time.time + 0.22f;
        PlayStep();
    }

    void OnLanded(float fallSpeed)
    {
        if (_player.IsClimbing)
            return;

        float drop = _airStartY - transform.position.y;
        if (drop >= 0.1f && drop <= 0.5f)
        {
            PlayStair();
            return;
        }

        var settings = _bob != null ? _bob.Settings : null;
        float min = settings != null ? settings.landingMinVelocity : 2.5f;
        float max = settings != null ? settings.landingMaxVelocity : 7f;
        if (-fallSpeed < min)
            return;

        float impact = Mathf.InverseLerp(min, max, -fallSpeed);
        var clips = Resolve();
        AudioClip[] land = clips.Lands.Length > 0 ? clips.Lands : clips.Steps;
        Play(land, ref _lastLand, Mathf.Lerp(walkVolume, 1f, impact));
    }

    FootstepClips Resolve()
    {
        if (!TryHitGround(out RaycastHit hit))
            return defaultClips;

        var overrideSurface = hit.collider.GetComponentInParent<FootstepSurface>();
        if (overrideSurface != null && overrideSurface.HasClips)
            return overrideSurface.Clips;

        string material = hit.collider.sharedMaterial != null ? hit.collider.sharedMaterial.name : string.Empty;
        string tag = hit.collider.tag;
        string name = hit.collider.name;

        if (surfaces != null)
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i].Matches(material, tag, name))
                    return surfaces[i];
            }
        }

        return defaultClips;
    }

    bool TryHitGround(out RaycastHit hit)
    {
        hit = default;
        float radius = 0.12f;
        Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);
        int count = Physics.SphereCastNonAlloc(
            origin,
            radius,
            Vector3.down,
            _hits,
            0.4f,
            groundMask,
            QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Transform other = _hits[i].transform;
            if (other == null || other == transform || other.IsChildOf(transform))
                continue;

            if (_hits[i].distance >= nearest)
                continue;

            nearest = _hits[i].distance;
            hit = _hits[i];
            found = true;
        }

        return found;
    }

    void Play(AudioClip[] clips, ref int last, float volume)
    {
        if (clips == null || clips.Length == 0)
            return;

        int index = Random.Range(0, clips.Length);
        if (clips.Length > 1 && index == last)
            index = (index + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
        last = index;

        AudioSource source = _voices[_voice];
        _voice = (_voice + 1) % _voices.Length;
        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.clip = clips[index];
        source.volume = Mathf.Clamp01(volume * (1f + Random.Range(-volumeJitter, volumeJitter)));
        source.Play();
    }
}

[System.Serializable]
public class FootstepClips
{
    [Tooltip("Имена Physic Material, тега или объекта. Пусто — не участвует в подборе.")]
    public string[] names;
    public AudioClip[] steps;
    public AudioClip[] lands;

    public AudioClip[] Steps => steps ?? System.Array.Empty<AudioClip>();
    public AudioClip[] Lands => lands ?? System.Array.Empty<AudioClip>();

    public bool Matches(string material, string tag, string objectName)
    {
        if (names == null)
            return false;

        for (int i = 0; i < names.Length; i++)
        {
            string key = names[i];
            if (string.IsNullOrEmpty(key))
                continue;

            if (Contains(material, key) || Contains(tag, key) || Contains(objectName, key))
                return true;
        }

        return false;
    }

    static bool Contains(string value, string key)
    {
        return !string.IsNullOrEmpty(value)
            && value.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
