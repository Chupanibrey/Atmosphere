using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Выглядывание из-за угла: Q влево, E вправо.
/// </summary>
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(PlayerController))]
public class PlayerLean : MonoBehaviour
{
    const float AxisDeadzone = 0.01f;
    const int FitIterations = 8;

    [SerializeField] PlayerLeanSettings settings = new();

    PlayerController _player;
    Camera _camera;
    InputAction _leanAction;
    bool _ownsAction;
    Vector3 _offset;
    Vector3 _euler;
    readonly RaycastHit[] _hits = new RaycastHit[16];
    readonly Vector3[] _nearCorners = new Vector3[4];

    public PlayerLeanSettings Settings => settings;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        CacheAction();
        TryCacheCamera();
    }

    void OnEnable()
    {
        _leanAction?.Enable();
    }

    void OnDisable()
    {
        _leanAction?.Disable();
        ClearLean();
    }

    void OnDestroy()
    {
        if (_ownsAction)
            _leanAction?.Dispose();
    }

    void LateUpdate()
    {
        if (_player == null || !_player.HasCamera)
            return;

        TryCacheCamera();

        if (settings == null || !settings.enabled)
        {
            ClearLean();
            return;
        }

        UpdateLean(Time.deltaTime);
    }

    void UpdateLean(float dt)
    {
        float axis = ReadAxis();
        Vector3 targetOffset = Vector3.zero;
        Vector3 targetEuler = Vector3.zero;

        if (Mathf.Abs(axis) >= AxisDeadzone)
        {
            float distance = FitDistance(axis);
            targetOffset = new Vector3(Mathf.Sign(axis) * distance, settings.height * Mathf.Abs(axis), 0f);
            targetEuler = new Vector3(0f, 0f, -axis * settings.roll);
        }

        float blend = 1f - Mathf.Exp(-settings.lerpSpeed * dt);
        _offset = Vector3.Lerp(_offset, targetOffset, blend);
        _euler = Vector3.Lerp(_euler, targetEuler, blend);
        _player.SetCameraLean(_offset, _euler);
    }

    float ReadAxis()
    {
        if (_player.IsClimbing || _leanAction == null)
            return 0f;

        return _leanAction.ReadValue<float>();
    }

    float FitDistance(float axis)
    {
        float desired = settings.distance * Mathf.Abs(axis);
        float sign = Mathf.Sign(axis);

        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            if (IsClear(sign * desired, axis))
                return desired;

            // Максимальный lean, при котором near clip не заходит в стену.
            float lo = 0f;
            float hi = desired;
            for (int i = 0; i < FitIterations; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (IsClear(sign * mid, axis))
                    lo = mid;
                else
                    hi = mid;
            }

            return lo;
        }
        finally
        {
            Physics.queriesHitBackfaces = prevBackfaces;
        }
    }

    bool IsClear(float signedDistance, float axis)
    {
        Vector3 safe = CameraWorldPoint(0f, 0f);
        Vector3 cameraPos = CameraWorldPoint(signedDistance, settings.height * Mathf.Abs(axis));
        Quaternion rotation = Quaternion.Euler(_player.Pitch, _player.transform.eulerAngles.y, -axis * settings.roll);
        FillNearCorners(cameraPos, rotation);

        if (SegmentBlocked(safe, cameraPos))
            return false;

        for (int i = 0; i < 4; i++)
        {
            if (SegmentBlocked(safe, _nearCorners[i]))
                return false;
        }

        return true;
    }

    Vector3 CameraWorldPoint(float localX, float extraY)
    {
        Vector3 local = _player.CameraRestLocalPos;
        local.y = _player.CameraHeightY + extraY;
        local.x += localX;
        return _player.transform.TransformPoint(local);
    }

    void FillNearCorners(Vector3 cameraPos, Quaternion rotation)
    {
        float near = _camera != null ? _camera.nearClipPlane : 0.3f;
        float fov = _camera != null ? _camera.fieldOfView : 60f;
        float aspect = _camera != null ? _camera.aspect : 16f / 9f;
        float halfH = near * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * aspect;
        float pad = settings.wallPadding;

        _nearCorners[0] = cameraPos + rotation * new Vector3(-halfW - pad, -halfH - pad, near + pad);
        _nearCorners[1] = cameraPos + rotation * new Vector3(halfW + pad, -halfH - pad, near + pad);
        _nearCorners[2] = cameraPos + rotation * new Vector3(-halfW - pad, halfH + pad, near + pad);
        _nearCorners[3] = cameraPos + rotation * new Vector3(halfW + pad, halfH + pad, near + pad);
    }

    bool SegmentBlocked(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.001f)
            return false;

        int count = Physics.RaycastNonAlloc(
            from,
            delta / dist,
            _hits,
            dist,
            settings.obstructionMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Transform hit = _hits[i].transform;
            if (hit == _player.transform || hit.IsChildOf(_player.transform))
                continue;

            return true;
        }

        return false;
    }

    void ClearLean()
    {
        _offset = Vector3.zero;
        _euler = Vector3.zero;
        _player?.SetCameraLean(Vector3.zero, Vector3.zero);
    }

    void TryCacheCamera()
    {
        if (_camera != null || _player.CameraPivot == null)
            return;

        _camera = _player.CameraPivot.GetComponent<Camera>();
    }

    void CacheAction()
    {
        var actions = InputSystem.actions;
        _leanAction = actions != null ? actions.FindAction("Player/Lean", throwIfNotFound: false) : null;
        if (_leanAction != null)
            return;

        _leanAction = new InputAction("Lean", InputActionType.Value);
        _leanAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/q")
            .With("Positive", "<Keyboard>/e");
        _ownsAction = true;
    }
}

[Serializable]
public class PlayerLeanSettings
{
    public bool enabled = true;

    [Tooltip("Насколько камера уходит в сторону, метры.")]
    public float distance = 0.45f;

    [Tooltip("Небольшое опускание головы при наклоне.")]
    public float height = -0.04f;

    [Tooltip("Крен камеры, градусы.")]
    public float roll = 8f;

    [Tooltip("Как быстро выходим и возвращаемся.")]
    public float lerpSpeed = 10f;

    [Tooltip("Запас вокруг near clip, метры. Если снова видно внутренности стены — увеличьте.")]
    public float wallPadding = 0.08f;

    [Tooltip("Слои стен. Для Prototype Map оставьте Everything.")]
    public LayerMask obstructionMask = ~0;
}
