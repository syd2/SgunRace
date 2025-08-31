using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class SplineFromWaypointsStrict : MonoBehaviour
{
    [Header("Inputs")]
    public Transform waypointsParent;          // RoadSpace/CenterMarkers
    public SplineContainer outputSpline;       // Track (SplineContainer)
    public bool closed = true;                 // loop lap or not

    [Header("Safety")]
    [Tooltip("Hard limit per segment; prevents cross-map jumps.")]
    public float maxSegmentLength = 40f;       // meters
    public bool abortOnLongEdge = true;

    [Header("Smoothing")]
    [Range(0f,1f)] public float handleTightness = 0.5f; // lower = rounder

    [Header("Project to road (optional)")]
    public bool projectToRoad = true;
    public LayerMask roadMask = ~0;            // set to your Road layer if you use one
    public float projectRayHeight = 50f;
    public float projectYOffset = 0f;

    [ContextMenu("Build (Strict Order)")]
    public void BuildStrict()
    {
        if (!waypointsParent || !outputSpline) return;

        // 1) collect children IN HIERARCHY ORDER
        var pts = new List<Transform>();
        for (int i = 0; i < waypointsParent.childCount; i++)
        {
            var t = waypointsParent.GetChild(i);
            if (t && t.gameObject.activeInHierarchy) pts.Add(t);
        }
        if (pts.Count < 2) { Debug.LogWarning("[StrictSpline] Need at least 2 waypoints."); return; }

        // 2) safety: check segment lengths (including last->first if closed)
        int limit = closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < limit; i++)
        {
            Vector3 a = pts[i].position;
            Vector3 b = pts[(i + 1) % pts.Count].position;
            float d = Vector3.Distance(a, b);
            if (d > maxSegmentLength)
            {
                Debug.LogWarning($"[StrictSpline] Segment {i}->{(i + 1) % pts.Count} is {d:F1} m > max {maxSegmentLength}. " +
                                 $"Move/add a waypoint between them (or raise maxSegmentLength).");
                if (abortOnLongEdge) return;
            }
        }

        // 3) write knots at EXACT waypoint positions
        var s = outputSpline.Splines.Count > 0 ? outputSpline.Splines[0] : outputSpline.Spline;
        s.Clear();
        foreach (var t in pts)
        {
            var local = outputSpline.transform.InverseTransformPoint(t.position);
            var knot = new BezierKnot((float3)local);
            s.Add(knot, TangentMode.Mirrored);
        }
        s.Closed = closed;

        // 4) mirror handles for smoothness (knots stay put)
        int n = s.Count;
        for (int i = 0; i < n; i++)
        {
            if (!closed && (i == 0 || i == n - 1)) continue;

            int iPrev = (i - 1 + n) % n;
            int iNext = (i + 1) % n;

            Vector3 p  = outputSpline.transform.TransformPoint((Vector3)s[i].Position);
            Vector3 p0 = outputSpline.transform.TransformPoint((Vector3)s[iPrev].Position);
            Vector3 p1 = outputSpline.transform.TransformPoint((Vector3)s[iNext].Position);

            Vector3 dir = (p1 - p0).normalized;
            float distPrev = Vector3.Distance(p, p0);
            float distNext = Vector3.Distance(p, p1);
            float len = Mathf.Min(distPrev, distNext) * 0.5f * Mathf.Lerp(0.25f, 0.75f, 1f - handleTightness);

            Vector3 inH  = p - dir * len;
            Vector3 outH = p + dir * len;

            var knot = s[i];
            knot.TangentIn  = (float3)outputSpline.transform.InverseTransformPoint(inH)  - knot.Position;
            knot.TangentOut = (float3)outputSpline.transform.InverseTransformPoint(outH) - knot.Position;
            s.SetKnot(i, knot);
        }

        if (projectToRoad) ProjectKnotsToRoad();

        MarkDirty();
        Debug.Log($"[StrictSpline] Built {(closed ? "closed" : "open")} spline with {n} knots (strict order).");
    }

    [ContextMenu("Project Knots To Road")]
    public void ProjectKnotsToRoad()
    {
        if (!outputSpline) return;
        var s = outputSpline.Splines.Count > 0 ? outputSpline.Splines[0] : outputSpline.Spline;
        if (s == null) return;

        for (int i = 0; i < s.Count; i++)
        {
            var k = s[i];
            Vector3 wp = outputSpline.transform.TransformPoint((Vector3)k.Position);
            Vector3 start = wp + Vector3.up * projectRayHeight;

            if (Physics.Raycast(start, Vector3.down, out var hit, projectRayHeight * 2f, roadMask,
                                QueryTriggerInteraction.Ignore))
            {
                Vector3 newWp = hit.point + Vector3.up * projectYOffset;
                k.Position = (float3)outputSpline.transform.InverseTransformPoint(newWp);
                s.SetKnot(i, k);
            }
        }
        MarkDirty();
        Debug.Log("[StrictSpline] Projected knots to road.");
    }

    void MarkDirty()
    {
    #if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(outputSpline);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(outputSpline.gameObject.scene);
    #endif
    }

    // Optional: draw long edges in red in the Scene view for easy fix
    void OnDrawGizmos()
    {
        if (!waypointsParent) return;
        Gizmos.color = Color.red;
        for (int i = 0; i < waypointsParent.childCount - (closed ? 0 : 1); i++)
        {
            var a = waypointsParent.GetChild(i);
            var b = waypointsParent.GetChild((i + 1) % waypointsParent.childCount);
            if (!a || !b) continue;
            float d = Vector3.Distance(a.position, b.position);
            if (d > maxSegmentLength)
                Gizmos.DrawLine(a.position, b.position);
        }
    }
}
