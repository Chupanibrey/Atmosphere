using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Аниация рук с монтировкой: достать, покой, случайный удар, убрать.
/// </summary>
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(PlayerController))]
public class PlayerCrowbar : MonoBehaviour
{
    static readonly int Show = Animator.StringToHash("Show");
    static readonly int Idle = Animator.StringToHash("Idle");
    static readonly int Hide = Animator.StringToHash("Hide");
    static readonly int[] Attacks =
    {
        Animator.StringToHash("Attack 1"),
        Animator.StringToHash("Attack 2"),
        Animator.StringToHash("Attack 3"),
        Animator.StringToHash("Attack 4")
    };

    [SerializeField] GameObject viewModel;
    [SerializeField] RuntimeAnimatorController controller;
    [SerializeField] Material armsMaterial;
    [SerializeField] Material crowbarMaterial;
    [SerializeField] Vector3 localPosition = Vector3.zero;
    [SerializeField] Vector3 localEuler = Vector3.zero;
    [SerializeField] float modelScale = 1f;

    PlayerController _player;
    InputAction _drawAction;
    InputAction _meleeAction;
    GameObject _instance;
    Animator _animator;
    int _queuedState;
    int _lastAttack = -1;
    bool _holstering;
    readonly ViewModelObstruction _walls = new ViewModelObstruction();

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        var actions = InputSystem.actions;
        _drawAction = actions != null ? actions.FindAction("Player/DrawWeapon", false) : null;
        _meleeAction = actions != null ? actions.FindAction("Player/Melee", false) : null;
    }

    void OnEnable()
    {
        _drawAction?.Enable();
        _meleeAction?.Enable();
    }

    void OnDisable()
    {
        _drawAction?.Disable();
        _meleeAction?.Disable();
    }

    void Update()
    {
        bool drawn = _instance != null && _instance.activeSelf;

        if (!drawn)
        {
            if (WasPressed(_drawAction))
                Draw();
            return;
        }

        if (_holstering && Finished(Hide))
        {
            _instance.SetActive(false);
            _holstering = false;
            return;
        }

        if (WasPressed(_drawAction) && !_holstering && (IsCurrent(Idle) || IsCurrent(Show) || IsAttack()))
            Play(Hide);

        if (MeleeHeld() && IsCurrent(Idle))
            Play(NextAttack());
    }

    void LateUpdate()
    {
        if (_queuedState != 0 && _animator != null && _animator.isInitialized)
        {
            int state = _queuedState;
            _queuedState = 0;
            _animator.Play(state, 0, 0f);
            _animator.Update(0f);
        }

        TuckArmsFromWalls();
    }

    void TuckArmsFromWalls()
    {
        if (_instance == null || !_instance.activeSelf)
        {
            _walls.ResetMotion();
            return;
        }

        Transform cam = _instance.transform.parent;
        if (cam == null)
            return;

        _walls.Apply(_instance.transform, cam, transform, localPosition, localEuler, Time.deltaTime);
    }

    void Draw()
    {
        EnsureInstance();
        if (_instance == null)
            return;

        _holstering = false;
        _instance.SetActive(true);
        Play(Show);
    }

    void Play(int state)
    {
        if (_animator == null)
            return;

        _holstering = state == Hide;
        _queuedState = state;
    }

    int NextAttack()
    {
        int index = Random.Range(0, Attacks.Length);
        if (Attacks.Length > 1 && index == _lastAttack)
            index = (index + 1) % Attacks.Length;

        _lastAttack = index;
        return Attacks[index];
    }

    bool IsCurrent(int state)
    {
        if (_animator == null || !_animator.isInitialized || _animator.IsInTransition(0))
            return false;

        return _animator.GetCurrentAnimatorStateInfo(0).shortNameHash == state;
    }

    bool IsAttack()
    {
        if (_animator == null || !_animator.isInitialized || _animator.IsInTransition(0))
            return false;

        int hash = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        for (int i = 0; i < Attacks.Length; i++)
        {
            if (hash == Attacks[i])
                return true;
        }

        return false;
    }

    bool Finished(int state)
    {
        if (!IsCurrent(state))
            return false;

        return _animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f;
    }

    bool MeleeHeld()
    {
        if (_meleeAction != null)
            return _meleeAction.IsPressed();

        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    static bool WasPressed(InputAction action)
    {
        return action != null && action.WasPressedThisFrame();
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
            _animator.keepAnimatorStateOnDisable = false;
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
}
