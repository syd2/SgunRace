using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Splines;
using Unity.Mathematics;

/// Kinematic follower that moves along a SplineContainer at uniform m/s,
/// with optional lateral lane offset supplied by LaneMover.
[ExecuteAlways]
public class TrackFollower : MonoBehaviour
{
    [Header("Track")]
    public SplineContainer splineContainer;
    [Tooltip("If true, wraps when reaching the end of the spline.")]
    public bool loop = false;
    [Tooltip("Meters per second along the centerline.")]
    public float forwardSpeed = 1.2f;
    [Tooltip("Lift above ground to avoid z-fighting.")]
    public float heightOffset = 0.20f;

    [Header("Finish Behaviour (point-to-point)")]
    [Tooltip("If true and loop is false, zero speed when finish is reached.")]
    public bool stopAtEnd = true;
    public UnityEvent onFinished;

    [Header("Sampling")]
    [Tooltip("Higher = smoother speed. 128 is fine for short blockouts.")]
    public int samples = 128;

    [Header("Runtime (read-only)")]
    public float distanceAlongTrack = 0f;   // meters from start
    public float trackLength = 0f;          // meters

    // Optional: lane offset provider on the same GameObject
    private LaneMover laneMover;

    // arc-length LUT
    private float[] cumLen;   // cumulative length at each sample
    private float[] ts;       // t parameter [0..1] at each sample

    // internal finish flag
    private bool _finished;

    void OnEnable()
    {
        laneMover = GetComponent<LaneMover>();
        BuildLUT();
        distanceAlongTrack = 0f;
        _finished = false;
        SnapRotationToTangent(0f);
    }

    void OnValidate()
    {
        if (samples < 16) samples = 16;
        // Rebuild when values change in editor
        BuildLUT();
    }

    void Update()
    {
        if (splineContainer == null || trackLength <= 0f) return;

        // Advance distance uniformly in meters (unless we finished and must stop)
        if (!(_finished && stopAtEnd))
            distanceAlongTrack += forwardSpeed * Time.deltaTime;

        if (loop)
        {
            distanceAlongTrack %= trackLength;
            if (distanceAlongTrack < 0f) distanceAlongTrack += trackLength;
        }
        else
        {
            // Detect finish once
            if (!_finished && distanceAlongTrack >= trackLength)
            {
                distanceAlongTrack = trackLength;   // clamp to end
                _finished = true;
                if (stopAtEnd) forwardSpeed = 0f;  // halt our "engine"
                onFinished?.Invoke();              // notify listeners (timer/UI/etc.)
            }
            else
            {
                distanceAlongTrack = Mathf.Clamp(distanceAlongTrack, 0f, trackLength);
            }
        }

        // Convert distance -> t using LUT
        float t = DistanceToT(distanceAlongTrack);

        // Evaluate center position & tangent in WORLD space
        var spline = GetSpline();
        SplineUtility.Evaluate(spline, t, out float3 pL, out float3 tanL, out float3 upL);

        Vector3 worldPos = splineContainer.transform.TransformPoint((Vector3)pL);
        Vector3 worldTan = splineContainer.transform.TransformDirection((Vector3)tanL).normalized;

        // Compute right vector in world space
        Vector3 right = Vector3.Cross(Vector3.up, worldTan).normalized;
        if (right.sqrMagnitude < 1e-6f) right = Vector3.right; // fallback

        // Lateral offset from LaneMover (or 0 if none)
        float lateral = (laneMover != null) ? laneMover.CurrentOffset : 0f;

        // Final position & rotation
        Vector3 finalPos = worldPos + right * lateral + Vector3.up * heightOffset;
        transform.position = finalPos;
        transform.rotation = Quaternion.LookRotation(worldTan, Vector3.up);
    }

    // --- Public helpers -------------------------------------------------------

    /// Rebuild the length lookup table (call if you edit the spline at runtime).
    public void RebuildLUT() => BuildLUT();

    /// Reset the follower to the start of the spline.
    public void ResetToStart()
    {
        _finished = false;
        distanceAlongTrack = 0f;
        SnapRotationToTangent(0f);
    }

    /// Teleport along the track to a fraction (0..1) of its length.
    public void TeleportToFraction(float f01)
    {
        f01 = Mathf.Clamp01(f01);
        distanceAlongTrack = trackLength * f01;
        _finished = false;
        SnapRotationToTangent(DistanceToT(distanceAlongTrack));
    }

    // --- Internals ------------------------------------------------------------

    private Spline GetSpline()
    {
        // Supports Splines 2.x and 1.x
        return splineContainer.Splines.Count > 0 ? splineContainer.Splines[0] : splineContainer.Spline;
    }

    private void BuildLUT()
    {
        if (splineContainer == null) return;

        var spline = GetSpline();
        if (spline == null) return;

        // Allocate arrays
        int n = Mathf.Max(2, samples);
        cumLen = new float[n];
        ts     = new float[n];

        // Sample positions in world space and accumulate length
        Vector3 prev = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            float t = (n == 1) ? 0f : i / (float)(n - 1);
            ts[i] = t;

            SplineUtility.Evaluate(spline, t, out float3 pL, out float3 _tan, out float3 _up);
            Vector3 wp = splineContainer.transform.TransformPoint((Vector3)pL);

            if (i == 0) cumLen[i] = 0f;
            else        cumLen[i] = cumLen[i - 1] + Vector3.Distance(prev, wp);

            prev = wp;
        }

        // Guard: if spline changed drastically, clamp distance
        distanceAlongTrack = Mathf.Clamp(distanceAlongTrack, 0f, cumLen[n - 1]);
        trackLength = cumLen[n - 1]; // measured length (matches our LUT)
    }

    private float DistanceToT(float distance)
    {
        if (cumLen == null || cumLen.Length < 2) return 0f;

        // Binary search for segment
        int lo = 0, hi = cumLen.Length - 1;
        distance = Mathf.Clamp(distance, 0f, cumLen[hi]);

        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (cumLen[mid] < distance) lo = mid + 1;
            else hi = mid;
        }

        int i1 = Mathf.Clamp(lo, 1, cumLen.Length - 1);
        int i0 = i1 - 1;

        float d0 = cumLen[i0];
        float d1 = cumLen[i1];
        float t0 = ts[i0];
        float t1 = ts[i1];

        float seg = Mathf.Max(1e-6f, d1 - d0);
        float u = (distance - d0) / seg;

        return Mathf.Lerp(t0, t1, u);
    }

    private void SnapRotationToTangent(float t)
    {
        var spline = GetSpline();
        if (spline == null) return;
        SplineUtility.Evaluate(spline, t, out float3 pL, out float3 tanL, out float3 _upL);
        Vector3 worldTan = splineContainer.transform.TransformDirection((Vector3)tanL).normalized;
        transform.rotation = Quaternion.LookRotation(worldTan, Vector3.up);
    }
}
