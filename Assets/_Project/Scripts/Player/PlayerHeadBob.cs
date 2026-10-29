using System;
using UnityEngine;


[Serializable]
public class HeadBobSettings
{
    public bool enabled = true;

    [Range(0f, 2f)]
    [Tooltip("Общая сила покачивания.")]
    public float amount = 1f;

    [Tooltip("Скорость, ниже которой шаг считается стоянием.")]
    public float minSpeed = 0.15f;

    [Tooltip("Как быстро покачивание включается и выключается.")]
    public float blendSpeed = 2.8f;

    [Tooltip("Как быстро камера догоняет целевое смещение.")]
    public float returnSpeed = 7f;

    [Range(0f, 1f)]
    [Tooltip("Сила дыхания на месте.")]
    public float idleAmount = 0.28f;

    [Tooltip("Частота дыхания на месте, циклов в секунду.")]
    public float idleFrequency = 0.22f;

    [Tooltip("Провал камеры при приземлении, в метрах.")]
    public float landingDip = 0.04f;

    [Tooltip("Минимальная скорость падения для провала камеры.")]
    public float landingMinVelocity = 2.5f;

    [Tooltip("Скорость падения, при которой провал максимальный.")]
    public float landingMaxVelocity = 7f;

    [Tooltip("Скорость возврата камеры после приземления.")]
    public float landingRecover = 1.35f;

    public HeadBobProfile walk = new()
    {
        stepsPerSecond = 1.8f,
        vertical = 0.035f,
        horizontal = 0.02f,
        forward = 0.008f,
        pitch = 0.45f,
        yaw = 0.15f,
        roll = 0.85f
    };

    public HeadBobProfile sprint = new()
    {
        stepsPerSecond = 3f,
        vertical = 0.05f,
        horizontal = 0.028f,
        forward = 0.014f,
        pitch = 0.65f,
        yaw = 0.2f,
        roll = 1.15f
    };

    public HeadBobProfile crouch = new()
    {
        stepsPerSecond = 1.15f,
        vertical = 0.018f,
        horizontal = 0.014f,
        forward = 0.006f,
        pitch = 0.25f,
        yaw = 0.12f,
        roll = 0.4f
    };

    public HeadBobProfile climb = new()
    {
        stepsPerSecond = 0.8f,
        vertical = 0.022f,
        horizontal = 0.006f,
        forward = 0.004f,
        pitch = 0.2f,
        yaw = 0.06f,
        roll = 0.2f
    };

    public HeadBobProfile idle = new()
    {
        stepsPerSecond = 1.8f,
        vertical = 0.005f,
        horizontal = 0.0015f,
        forward = 0.0005f,
        pitch = 0.06f,
        yaw = 0.03f,
        roll = 0.04f
    };
}

[Serializable]
public class HeadBobProfile
{
    [Tooltip("Каденс: шагов в секунду на полной скорости.")]
    public float stepsPerSecond = 1.8f;

    [Tooltip("Смещение вверх-вниз, м/с²..")]
    public float vertical = 0.03f;

    [Tooltip("Смещение влево-вправо, м/с²..")]
    public float horizontal = 0.02f;

    [Tooltip("Смещение вперёд-назад, м/с²..")]
    public float forward = 0.008f;

    [Tooltip("Наклон вверх-вниз, градусы.")]
    public float pitch = 0.4f;

    [Tooltip("Поворот влево-вправо, градусы.")]
    public float yaw = 0.15f;

    [Tooltip("Крен (наклон головы к плечу), градусы.")]
    public float roll = 0.5f;
}

/// <summary>
/// Покачивание камеры.
/// </summary>
[DefaultExecutionOrder(300)] // после PlayerController
[RequireComponent(typeof(PlayerController))]
public class PlayerHeadBob : MonoBehaviour
{
    [SerializeField] HeadBobSettings settings = new();

    PlayerController _player;
    float _bobPhase;
    float _idlePhase;
    float _bobWeight;
    Vector3 _bobOffset;
    Vector3 _bobEuler;
    float _landingDip;

    /// <summary>Нижняя точка камеры: один вызов на шаг, без второго расчёта фазы.</summary>
    public event Action Stepped;

    public HeadBobSettings Settings => settings;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
    }

    void OnDisable()
    {
        _player?.SetCameraBob(Vector3.zero, Vector3.zero);
    }

    void LateUpdate()
    {
        if (_player == null || !_player.HasCamera)
            return;

        float dt = Time.deltaTime;
        if (settings == null || !settings.enabled)
        {
            _bobOffset = Vector3.zero;
            _bobEuler = Vector3.zero;
            _player.SetCameraBob(Vector3.zero, Vector3.zero);
            return;
        }

        UpdateBob(dt);
        _player.SetCameraBob(_bobOffset, _bobEuler);
    }

    void UpdateBob(float dt)
    {
        // На вертикальной лестнице горизонтальной скорости нет — берём полный вектор.
        float speed = _player.IsClimbing ? _player.Velocity.magnitude : _player.PlanarSpeed;
        bool moving = speed > settings.minSpeed;
        bool canBob = moving && (_player.IsGrounded || _player.IsClimbing);

        float gaitSpeed = Mathf.Max(0.01f, _player.CurrentGaitSpeed);
        float targetWeight = canBob ? Mathf.InverseLerp(settings.minSpeed, gaitSpeed, speed) : 0f;
        _bobWeight = Mathf.MoveTowards(_bobWeight, Mathf.Clamp01(targetWeight), settings.blendSpeed * dt);

        HeadBobProfile profile = GetProfile(_player.CurrentGait);
        HeadBobProfile idle = settings.idle;

        float previousPhase = _bobPhase;
        if (_bobWeight > 0.01f)
        {
            // шаги/с, а не м/с
            float speedRatio = Mathf.Clamp(speed / gaitSpeed, 0f, 1.25f);
            _bobPhase += profile.stepsPerSecond * Mathf.PI * speedRatio * dt;
            NotifyStep(previousPhase, _bobPhase);
        }

        _idlePhase += dt * Mathf.PI * 2f * Mathf.Max(0.05f, settings.idleFrequency);

        float impactSpeed = _player.ConsumeLandingImpact();
        if (impactSpeed < 0f)
        {
            float impact = Mathf.InverseLerp(settings.landingMinVelocity, settings.landingMaxVelocity, -impactSpeed);
            _landingDip = Mathf.Max(_landingDip, settings.landingDip * impact);
        }

        _landingDip = Mathf.MoveTowards(_landingDip, 0f, settings.landingRecover * dt);

        Vector3 walkOffset = EvaluateOffset(profile, _bobPhase) * _bobWeight;
        // На ходу дыхание гасим, на месте — наоборот.
        Vector3 idleOffset = EvaluateOffset(idle, _idlePhase) * ((1f - _bobWeight) * settings.idleAmount);
        Vector3 targetOffset = (walkOffset + idleOffset) * settings.amount;
        targetOffset.y -= _landingDip;

        Vector3 walkEuler = EvaluateEuler(profile, _bobPhase) * _bobWeight;
        Vector3 idleEuler = EvaluateEuler(idle, _idlePhase) * ((1f - _bobWeight) * settings.idleAmount);
        Vector3 targetEuler = (walkEuler + idleEuler) * settings.amount;

        float recover = 1f - Mathf.Exp(-settings.returnSpeed * dt); // тот же smoothing, что у приседа
        _bobOffset = Vector3.Lerp(_bobOffset, targetOffset, recover);
        _bobEuler = Vector3.Lerp(_bobEuler, targetEuler, recover);
    }

    void NotifyStep(float previousPhase, float phase)
    {
        // sin(2φ) = -1 при φ = 3π/4 + πk. Пока вес маленький, это ещё не шаг.
        if (_bobWeight <= 0.25f || Stepped == null)
            return;

        const float trough = Mathf.PI * 0.75f;
        int before = Mathf.FloorToInt((previousPhase - trough) / Mathf.PI);
        int after = Mathf.FloorToInt((phase - trough) / Mathf.PI);
        if (after > before)
            Stepped.Invoke();
    }

    HeadBobProfile GetProfile(PlayerGait gait)
    {
        return gait switch
        {
            PlayerGait.Sprint => settings.sprint,
            PlayerGait.Crouch => settings.crouch,
            PlayerGait.Climb => settings.climb,
            _ => settings.walk
        };
    }

    static Vector3 EvaluateOffset(HeadBobProfile profile, float phase)
    {
        float vertical = Mathf.Sin(phase * 2f) * profile.vertical; // два шага на цикл
        float horizontal = Mathf.Sin(phase) * profile.horizontal;
        float forward = -Mathf.Abs(Mathf.Sin(phase)) * profile.forward;
        return new Vector3(horizontal, vertical, forward);
    }

    static Vector3 EvaluateEuler(HeadBobProfile profile, float phase)
    {
        float pitch = -Mathf.Abs(Mathf.Sin(phase)) * profile.pitch;
        float yaw = Mathf.Sin(phase) * profile.yaw;
        float roll = -Mathf.Sin(phase) * profile.roll;
        return new Vector3(pitch, yaw, roll);
    }
}