using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Общие ссылки на карту ввода. Каждый скрипт вызывает Ensure в Awake.
/// </summary>
static class PlayerActions
{
    static bool _ready;

    public static InputAction Move { get; private set; }
    public static InputAction Look { get; private set; }
    public static InputAction Jump { get; private set; }
    public static InputAction Sprint { get; private set; }
    public static InputAction Crouch { get; private set; }
    public static InputAction Lean { get; private set; }
    public static InputAction DrawWeapon { get; private set; }
    public static InputAction Melee { get; private set; }

    public static void Ensure()
    {
        if (_ready)
            return;

        _ready = true;
        var actions = InputSystem.actions;
        if (actions == null)
        {
            Debug.LogError("Не найден Input Action Asset.");
            return;
        }

        Move = actions.FindAction("Player/Move", false);
        Look = actions.FindAction("Player/Look", false);
        Jump = actions.FindAction("Player/Jump", false);
        Sprint = actions.FindAction("Player/Sprint", false);
        Crouch = actions.FindAction("Player/Crouch", false);
        Lean = actions.FindAction("Player/Lean", false);
        DrawWeapon = actions.FindAction("Player/DrawWeapon", false);
        Melee = actions.FindAction("Player/Melee", false);
    }
}
