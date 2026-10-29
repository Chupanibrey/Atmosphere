using UnityEngine;

/// <summary>
/// Явный набор шагов на полу или объекте. Имеет приоритет над материалом и тегом.
/// </summary>
public class FootstepSurface : MonoBehaviour
{
    [SerializeField] FootstepClips clips;

    public FootstepClips Clips => clips;
    public bool HasClips => clips != null && clips.Steps.Length > 0;
}
