using UnityEngine;

/// <summary>
/// Маркер лестницы. Вешаем на объект с триггер-коллайдером.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Ladder : MonoBehaviour
{
    [Tooltip("Локальное направление вверх по лестнице.")]
    [SerializeField] Vector3 localUp = Vector3.up;

    [Tooltip("Локальное направление «лицом к лестнице» (обычно -Z или +Z).")]
    [SerializeField] Vector3 localFace = Vector3.forward;

    public Vector3 ClimbUp => transform.TransformDirection(localUp).normalized;
    public Vector3 AttachDirection => transform.TransformDirection(localFace).normalized;
    // Вбок по лестнице: перпендикуляр к «вверх» и «лицом к ней».
    public Vector3 ClimbRight => Vector3.Cross(ClimbUp, AttachDirection).normalized;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + ClimbUp * 1.5f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + AttachDirection);
    }
}
