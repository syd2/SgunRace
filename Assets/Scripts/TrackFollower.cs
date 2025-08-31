using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// Kinematic follower that moves along a SplineContainer at uniform m/s,
/// with optional lateral lane offset supplied by LaneMover.
[DefaultExecutionOrder(10)]
public class TrackFollower : MonoBehaviour
{
    [Header("Track")]
    public SplineContainer splineContainer;
    [Tooltip("If true, wraps when reaching the end.")]
    public bool loop = false;
    [Tooltip("If true (and loop=false), stop at the end instead of clamping & jittering.")]
    public bool stopAtEnd = true;
    [Tooltip("Meters per second along the centerline.")]
    public float forwardSpeed = 12f;
    [Tooltip("Lift above ground to avoid z-fighting.")]
    public float heightOffset = 0.20f;

    [Header("Sampling")]
    [Tooltip("Higher = smoother speed. 256–512 recommended on real tracks.")]
    public int samples = 256;

    [Header("Lifecycle")]
    [Tooltip("Reset distanceAlongTrack when entering Play mode.")]
    public bool resetOnPlay = true;
    [Tooltip("Starting distance (meters from start) when Play begins.")]
    public float startDistance = 0f;
    [Tooltip("If true, drives the object in Edit Mode for preview. Otherwise only while playing.")]
    public bool previewInEditMode = false;

    [Header("Runtime (read-only)")]
    public float distanceAlongTrack = 0f;   // meters from start
    public float trackLength = 0f;          // meters

    // Optional: lane offset provider on the same GameObject
    private LaneMover laneMover;

    // arc-length LUT
    private float[] cumLen;   // cumulative length at each sample
    private float[] ts;       // t parameter [0..1] at each sample

    void OnEnable()
    {
        laneMover = GetComponent<LaneMover>();
        BuildLUT();

        if (Application.isPlaying && resetOnPlay)
            distanceAlongTrack = Mathf.Clamp(startDistance, 0f, Mathf.Max(0.0001f, trackLength));

        // initial orientation
        if (splineContainer) SnapRotationToTangent(0f);
    }

    void OnValidate()
    {
        samples = Mathf.Max(16, samples);
        BuildLUT();
        // In edit mode, keep pose in sync when preview is enabled
        if (!Application.isPlaying && previewInEditMode)
            Drive(0f); // 0 dt -> just place according to current distance
    }

    void Update()
    {
        if (!Application.isPlaying && !previewInEditMode) return;
        if (splineContainer == null || trackLength <= 0f) return;

        float dt = Mathf.Max(0f, Time.deltaTime);
        Drive(dt);
    }

    // --- Core driving ---
    void Drive(float dt)
    {
        // advance
        if (dt > 0f)
            distanceAlongTrack += forwardSpeed * dt;

        if (loop)
        {
            if (trackLength > 0f)
            {
                distanceAlongTrack %= trackLength;
                if (distanceAlongTrack < 0f) distanceAlongTrack += trackLength;
            }
        }
        else
        {
            if (stopAtEnd && distanceAlongTrack >= trackLength)
            {
                distanceAlongTrack = trackLength;
                // don't keep adding; effectively stopped
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
        Vector3 worldTan = splineContainer.transform.TransformDirection(((Vector3)tanL)).normalized;

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

    // --- Helpers ---
    private Spline GetSpline()
    {
        // Supports Splines 2.x and 1.x
        return (splineContainer.Splines != null && splineContainer.Splines.Count > 0)
            ? splineContainer.Splines[0]
            : splineContainer.Spline;
    }

    private void BuildLUT()
    {
        if (splineContainer == null) return;
        var spline = GetSpline();
        if (spline == null) return;

        // Compute total length (meters)
        trackLength = SplineUtility.CalculateLength(spline, splineContainer.transform.localToWorldMatrix);

        // Allocate arrays
        int n = Mathf.Max(2, samples);
        cumLen = new float[n];
        ts     = new float[n];

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
        trackLength = cumLen[n - 1]; // measured length

        // keep distance valid
        distanceAlongTrack = Mathf.Clamp(distanceAlongTrack, 0f, trackLength);
    }

    private float DistanceToT(float distance)
    {
        if (cumLen == null || cumLen.Length < 2) return 0f;

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
        SplineUtility.Evaluate(spline, t, out float3 _pL, out float3 tanL, out float3 _upL);
        Vector3 worldTan = splineContainer.transform.TransformDirection(((Vector3)tanL)).normalized;
        transform.rotation = Quaternion.LookRotation(worldTan, Vector3.up);
    }

    // Handy inspector buttons
    [ContextMenu("Snap To Start")]
    void SnapToStart()
    {
        distanceAlongTrack = 0f;
        if (!Application.isPlaying) Drive(0f);
    }

    [ContextMenu("Snap To End")]
    void SnapToEnd()
    {
        distanceAlongTrack = trackLength;
        if (!Application.isPlaying) Drive(0f);
    }
}
