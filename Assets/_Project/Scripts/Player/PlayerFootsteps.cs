using UnityEngine;

/// <summary>
/// Звуки шагов. Самостоятельный скрипт: PlayerHeadBob не изменяется.
/// Фаза шага считается по той же формуле, что и в PlayerHeadBob
/// (скорость, походка, настройки из HeadBob.Settings), поэтому шаг
/// звучит в нижней точке покачивания камеры.
/// Если поменяете расчёт фазы в PlayerHeadBob.UpdateBob, обновите его и здесь.
/// </summary>
[DefaultExecutionOrder(301)] // после PlayerHeadBob (300)
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerHeadBob))]
[RequireComponent(typeof(AudioSource))]
public class PlayerFootsteps : MonoBehaviour
{
    [Header("Клипы")]
    [SerializeField] AudioClip[] stepClips;
    [Tooltip("Если пусто, при приземлении играют обычные шаги.")]
    [SerializeField] AudioClip[] landClips;

    [Header("Громкость")]
    [Range(0f, 1f)] [SerializeField] float walkVolume = 0.6f;
    [SerializeField] float sprintVolumeMul = 1.35f;
    [SerializeField] float crouchVolumeMul = 0.45f;
    [SerializeField] float volumeJitter = 0.15f;

    [Header("Высота тона")]
    [SerializeField] Vector2 pitchRange = new(0.92f, 1.08f);

    // Нижняя точка камеры: sin(2 * phase) = -1  =>  phase = 3π/4 + π * k
    const float StepPhase = Mathf.PI * 0.75f;

    PlayerController _player;
    PlayerHeadBob _bob;
    AudioSource _source;

    float _phase;
    float _weight;
    int _stepIndex = -1;
    int _lastStep = -1;
    int _lastLand = -1;

    bool _wasGrounded = true;
    float _lastY;
    float _fallSpeed;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _bob = GetComponent<PlayerHeadBob>();
        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f; // 2D: свои шаги слышны одинаково в обоих ушах
        _lastY = transform.position.y;
    }

    void LateUpdate()
    {
        var s = _bob.Settings;
        // Те же условия, при которых PlayerHeadBob не двигает фазу.
        if (!_bob.enabled || s == null || !s.enabled || !_player.HasCamera)
            return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        UpdateSteps(s, dt);
        UpdateLanding(s, dt);
    }

    void UpdateSteps(HeadBobSettings s, float dt)
    {
        float planarSpeed = _player.PlanarSpeed;
        bool moving = planarSpeed > s.minSpeed;
        bool canBob = moving && (_player.IsGrounded || _player.IsClimbing);

        float gaitSpeed = Mathf.Max(0.01f, _player.CurrentGaitSpeed);
        float targetWeight = canBob ? Mathf.InverseLerp(s.minSpeed, gaitSpeed, planarSpeed) : 0f;
        _weight = Mathf.MoveTowards(_weight, Mathf.Clamp01(targetWeight), s.blendSpeed * dt);

        if (_weight <= 0.01f) return;

        PlayerGait gait = _player.CurrentGait;
        HeadBobProfile profile = GetProfile(s, gait);

        float speedRatio = Mathf.Clamp(planarSpeed / gaitSpeed, 0f, 1.25f);
        _phase += profile.stepsPerSecond * Mathf.PI * speedRatio * dt;

        int index = Mathf.FloorToInt((_phase - StepPhase) / Mathf.PI);
        if (index <= _stepIndex) return;
        _stepIndex = index;

        if (gait == PlayerGait.Climb || _weight <= 0.25f) return;

        float gaitMul = gait switch
        {
            PlayerGait.Sprint => sprintVolumeMul,
            PlayerGait.Crouch => crouchVolumeMul,
            _ => 1f
        };
        // weight < 1: игрок разгоняется или идёт медленнее обычного -> тише
        Play(stepClips, ref _lastStep, walkVolume * gaitMul * Mathf.Lerp(0.6f, 1f, _weight));
    }

    void UpdateLanding(HeadBobSettings s, float dt)
    {
        bool grounded = _player.IsGrounded;
        float y = transform.position.y;

        if (!grounded)
            _fallSpeed = (y - _lastY) / dt; // последняя скорость в воздухе
        else if (!_wasGrounded && -_fallSpeed >= s.landingMinVelocity)
        {
            float impact = Mathf.InverseLerp(s.landingMinVelocity, s.landingMaxVelocity, -_fallSpeed);
            var clips = (landClips != null && landClips.Length > 0) ? landClips : stepClips;
            Play(clips, ref _lastLand, walkVolume * Mathf.Lerp(0.9f, 1.6f, impact));
        }

        _wasGrounded = grounded;
        _lastY = y;
    }

    static HeadBobProfile GetProfile(HeadBobSettings s, PlayerGait gait)
    {
        return gait switch
        {
            PlayerGait.Sprint => s.sprint,
            PlayerGait.Crouch => s.crouch,
            PlayerGait.Climb => s.climb,
            _ => s.walk
        };
    }

    void Play(AudioClip[] clips, ref int last, float volume)
    {
        if (clips == null || clips.Length == 0) return;

        int i = Random.Range(0, clips.Length);
        if (clips.Length > 1 && i == last)
            i = (i + 1 + Random.Range(0, clips.Length - 1)) % clips.Length; // не повторять клип подряд
        last = i;

        _source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        float v = Mathf.Clamp01(volume * (1f + Random.Range(-volumeJitter, volumeJitter)));
        _source.PlayOneShot(clips[i], v);
    }
}
