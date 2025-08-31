using UnityEngine;

/// Simple, smooth chase camera for a kinematic spline follower.
[DisallowMultipleComponent]
public class ChaseCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;                  // assign PlayerVehicle
    public Vector3 localOffset = new Vector3(0f, 4f, -8f);

    [Header("Smoothing")]
    [Tooltip("Seconds to smooth position. 0.12–0.2 feels good.")]
    public float positionSmoothTime = 0.15f;
    [Tooltip("How fast to rotate toward the look target. Bigger = snappier.")]
    public float rotationLerp = 12f;

    [Header("Look Ahead")]
    [Tooltip("Future seconds to look along target.forward (scaled by speed).")]
    public float lookAheadTime = 0.5f;
    public float minLookAhead = 2f;
    public float maxLookAhead = 25f;

    [Header("Debug")]
    public bool topDownDebug = false;
    public float topDownHeight = 20f;

    Vector3 _vel;           // SmoothDamp velocity
    TrackFollower _tf;      // to read forwardSpeed
    Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (target == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p) target = p.transform;
        }
        if (target) _tf = target.GetComponent<TrackFollower>();
    }

    void LateUpdate()
    {
        if (!target) return;

        if (topDownDebug)
        {
            // Simple orthographic top-down for debugging lanes
            if (_cam) { _cam.orthographic = true; _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, 10f, 1f - Mathf.Exp(-6f * Time.deltaTime)); }
            Vector3 desired = target.position + Vector3.up * topDownHeight;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _vel, positionSmoothTime);
            var look = Quaternion.LookRotation((target.position - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-rotationLerp * Time.deltaTime));
            return;
        }
        else if (_cam) _cam.orthographic = false;

        // Look slightly ahead based on follower speed
        float speed = (_tf != null) ? Mathf.Abs(_tf.forwardSpeed) : 10f;
        float lookAhead = Mathf.Clamp(speed * lookAheadTime, minLookAhead, maxLookAhead);

        Vector3 focus = target.position + target.forward * lookAhead;
        Vector3 desiredPos = target.TransformPoint(localOffset);

        // Smooth position
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _vel, positionSmoothTime);

        // Smooth rotation toward focus
        Vector3 toFocus = (focus - transform.position);
        if (toFocus.sqrMagnitude > 1e-6f)
        {
            Quaternion desiredRot = Quaternion.LookRotation(toFocus.normalized, Vector3.up);
            float k = 1f - Mathf.Exp(-rotationLerp * Time.deltaTime); // exp-smooth
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, k);
        }
    }

    [ContextMenu("Snap To Target")]
    public void SnapToTarget()
    {
        if (!target) return;
        transform.position = target.TransformPoint(localOffset);
        transform.rotation = Quaternion.LookRotation(target.forward, Vector3.up);
    }
}
