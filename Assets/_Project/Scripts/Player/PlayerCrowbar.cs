using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Руки с монтировкой на камере. H достаёт и убирает, атака проигрывает удар.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerCrowbar : MonoBehaviour
{
    const string SwingState = "Attack";

    [SerializeField] GameObject viewModel;
    [SerializeField] RuntimeAnimatorController controller;
    [SerializeField] Material armsMaterial;
    [SerializeField] Material crowbarMaterial;
    [SerializeField] Vector3 localPosition = Vector3.zero;
    [SerializeField] Vector3 localEuler = Vector3.zero;
    [SerializeField] float modelScale = 1f;

    PlayerController _player;
    InputAction _drawAction;
    InputAction _attackAction;
    GameObject _instance;
    Animator _animator;
    bool _drawn;
    bool _swingQueued;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        var actions = InputSystem.actions;
        _drawAction = actions != null ? actions.FindAction("Player/DrawWeapon", false) : null;
        _attackAction = actions != null ? actions.FindAction("Player/Attack", false) : null;
    }

    void OnEnable()
    {
        _drawAction?.Enable();
        _attackAction?.Enable();
    }

    void OnDisable()
    {
        _drawAction?.Disable();
        _attackAction?.Disable();
    }

    void Update()
    {
        if (WasDrawPressed())
            SetDrawn(!_drawn);

        if (_drawn && _attackAction != null && _attackAction.WasPressedThisFrame())
            PlaySwing();
    }

    void LateUpdate()
    {
        // Play в тот же кадр, когда объект только включился, Animator ещё не инициализирован.
        if (!_swingQueued || _animator == null || !_animator.isInitialized)
            return;

        _swingQueued = false;
        _animator.Play(SwingState, 0, 0f);
        _animator.Update(0f);
    }

    bool WasDrawPressed()
    {
        if (_drawAction != null)
            return _drawAction.WasPressedThisFrame();

        return Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame;
    }

    void SetDrawn(bool drawn)
    {
        _drawn = drawn;

        if (!drawn)
        {
            if (_instance != null)
                _instance.SetActive(false);
            return;
        }

        EnsureInstance();
        if (_instance == null)
            return;

        _instance.SetActive(true);
        PlaySwing();
    }

    void EnsureInstance()
    {
        if (_instance != null || viewModel == null)
        {
            if (viewModel == null)
                Debug.LogError("PlayerCrowbar: не назначена модель рук.");
            return;
        }

        Transform pivot = _player.CameraPivot != null ? _player.CameraPivot : transform;
        _instance = Instantiate(viewModel, pivot);
        _instance.name = "Crowbar Arms";
        _instance.transform.localPosition = localPosition;
        _instance.transform.localRotation = Quaternion.Euler(localEuler);
        _instance.transform.localScale = Vector3.one * modelScale;

        foreach (var camera in _instance.GetComponentsInChildren<Camera>(true))
            camera.enabled = false;
        foreach (var light in _instance.GetComponentsInChildren<Light>(true))
            light.enabled = false;

        ApplyMaterials(_instance);

        _animator = _instance.GetComponentInChildren<Animator>(true);
        if (_animator != null)
        {
            if (controller != null)
                _animator.runtimeAnimatorController = controller;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.applyRootMotion = false;
        }

        foreach (var skin in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void ApplyMaterials(GameObject root)
    {
        if (armsMaterial == null && crowbarMaterial == null)
            return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string name = mats[i] != null ? mats[i].name : renderer.name;
                bool bar = name.IndexOf("crowbar", System.StringComparison.OrdinalIgnoreCase) >= 0;
                Material next = bar ? crowbarMaterial : armsMaterial;
                if (next != null)
                    mats[i] = next;
            }

            renderer.sharedMaterials = mats;
        }
    }

    void PlaySwing()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null)
            return;

        _swingQueued = true;
    }
}
