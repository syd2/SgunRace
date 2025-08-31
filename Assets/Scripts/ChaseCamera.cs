using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ChaseCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Local Offset (relative to target)")]
    public Vector3 localOffset = new Vector3(0f, 4f, -8f); // x=right, y=up, z=back

    [Header("Smoothing")]
    public float positionLerp = 10f; // higher = snappier
    public float rotationLerp = 12f;

    void OnEnable()
    {
        if (!target) return;
        // Place safely on the very first frame
        transform.position = target.TransformPoint(localOffset);
        Vector3 view = target.position - transform.position;
        if (view.sqrMagnitude < 1e-6f) view = Vector3.forward; // absolute fallback
        transform.rotation = Quaternion.LookRotation(view, Vector3.up);
    }

    void LateUpdate()
    {
        if (!target) return;

        // Desired camera position from constant offset
        Vector3 desiredPos = target.TransformPoint(localOffset);

        // Smooth position (exponential lerp)
        float dt = Mathf.Clamp(Time.deltaTime, 1f/240f, 1f/30f);
        float posT = 1f - Mathf.Exp(-positionLerp * dt);
        transform.position = Vector3.Lerp(transform.position, desiredPos, posT);

        // Always look at the target (guaranteed non-zero because offset != 0)
        Vector3 toTarget = target.position - transform.position;
        // if something odd happens, nudge it
        if (toTarget.sqrMagnitude < 1e-6f) toTarget = new Vector3(0f, 0f, 0.001f);
        Quaternion lookRot = Quaternion.LookRotation(toTarget, Vector3.up);

        float rotT = 1f - Mathf.Exp(-rotationLerp * dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rotT);
    }
}
