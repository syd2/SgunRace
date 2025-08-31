using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class SplineFromWaypoints : MonoBehaviour
{
    [Header("Inputs")]
    public Transform waypointsParent;     // RoadSpace/CenterMarkers
    public SplineContainer outputSpline;  // Track (SplineContainer)
    public Transform startWaypoint;       // Start/finish marker
    public bool closed = true;

    [Header("Ordering guardrails")]
    [Tooltip("Max allowed link length (m). Set ~2–3x your typical spacing.")]
    public float maxLinkDistance = 35f;
    [Range(0f,1f)] public float directionBias = 0.7f; // prefer forward motion
    [Tooltip("2-opt passes to uncross edges (0 disables).")]
    [Range(0,6)] public int twoOptPasses = 2;

    [Header("Smoothing")]
    [Range(0f, 1f)] public float handleTightness = 0.5f; // lower = rounder

    [Header("Projection (optional)")]
    public LayerMask roadMask = ~0;
    public float projectRayHeight = 50f;
    public float projectYOffset = 0.00f;

    [ContextMenu("Build Spline From Waypoints")]
    public void Build()
    {
        if (!waypointsParent || !outputSpline) return;

        // collect points
        var pts = new List<Transform>();
        for (int i = 0; i < waypointsParent.childCount; i++)
            pts.Add(waypointsParent.GetChild(i));
        if (pts.Count < 2) { Debug.LogWarning("Need at least 2 waypoints."); return; }

        // order: guarded nearest chain
        var ordered = GuardedNearestChain(pts, out bool hadLongEdges);

        // optional 2-opt to uncross
        if (closed && twoOptPasses > 0)
            TwoOptRefine(ordered, twoOptPasses);

        // warn if any egregious jump remains
        if (HasLongEdge(ordered, maxLinkDistance * 1.01f))
            Debug.LogWarning("[SplineFromWaypoints] Long edge remains. Add a marker in that area or raise maxLinkDistance slightly.");

        // write spline
        var s = outputSpline.Splines.Count > 0 ? outputSpline.Splines[0] : outputSpline.Spline;
        s.Clear();

        foreach (var t in ordered)
        {
            Vector3 local = outputSpline.transform.InverseTransformPoint(t.position);
            var knot = new BezierKnot((float3)local);
            s.Add(knot, TangentMode.Mirrored);
        }
        s.Closed = closed;

        // mirrored handles
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

        MarkSplineDirty();
        Debug.Log($"[SplineFromWaypoints] Built spline with {n} knots.");
    }

    // --- Ordering helpers ---

    List<Transform> GuardedNearestChain(List<Transform> pts, out bool hadLongEdge)
    {
        hadLongEdge = false;
        var remaining = new HashSet<Transform>(pts);

        Transform start = startWaypoint && remaining.Contains(startWaypoint)
            ? startWaypoint
            : pts.OrderBy(t => t.position.z).First(); // fallback

        var ordered = new List<Transform>(pts.Count);
        Transform cur = start;
        remaining.Remove(cur);
        ordered.Add(cur);

        Vector3 fwd = Vector3.forward;
        if (remaining.Count > 0)
            fwd = (Nearest(cur, remaining).position - cur.position).normalized;

        while (remaining.Count > 0)
        {
            Transform next = null;
            float bestScore = float.NegativeInfinity;

            foreach (var c in remaining)
            {
                Vector3 d = c.position - cur.position;
                float dist = d.magnitude;
                if (dist > maxLinkDistance) continue;           // hard guard

                float dirScore = Vector3.Dot(fwd, d.normalized); // prefer forward
                float nearScore = 1f - Mathf.Clamp01(dist / maxLinkDistance);
                float score = Mathf.Lerp(nearScore, dirScore, directionBias);
                if (score > bestScore) { bestScore = score; next = c; }
            }

            if (next == null)
            {
                // nothing valid; fall back to nearest (and mark that we broke the guard)
                next = Nearest(cur, remaining);
                hadLongEdge = true;
            }

            ordered.Add(next);
            remaining.Remove(next);
            fwd = (next.position - cur.position).normalized;
            cur = next;
        }

        return ordered;
    }

    void TwoOptRefine(List<Transform> order, int passes)
    {
        if (order.Count < 4) return;
        for (int pass = 0; pass < passes; pass++)
        {
            bool improved = false;
            int n = order.Count;
            for (int i = 0; i < n - 1; i++)
            {
                int i2 = (i + 1) % n;
                for (int j = i + 2; j < n - (closed ? 0 : 1); j++)
                {
                    int j2 = (j + 1) % n;
                    float dOld = Dist(order[i], order[i2]) + Dist(order[j], order[j2]);
                    float dNew = Dist(order[i], order[j])  + Dist(order[i2], order[j2]);
                    if (dNew + 0.001f < dOld) // swap!
                    {
                        Reverse(order, i2, j);
                        improved = true;
                    }
                }
            }
            if (!improved) break;
        }
    }

    bool HasLongEdge(List<Transform> order, float limit)
    {
        int n = order.Count;
        for (int i = 0; i < n; i++)
        {
            float d = Vector3.Distance(order[i].position, order[(i+1)%n].position);
            if (d > limit) return true;
        }
        return false;
    }

    Transform Nearest(Transform a, IEnumerable<Transform> set)
    {
        Transform best = null; float bestD = float.PositiveInfinity;
        foreach (var t in set) { float d = (t.position - a.position).sqrMagnitude; if (d < bestD) { bestD = d; best = t; } }
        return best;
    }

    float Dist(Transform a, Transform b) => Vector3.Distance(a.position, b.position);

    void Reverse(List<Transform> list, int i, int j)
    {
        while (i < j) { (list[i], list[j]) = (list[j], list[i]); i++; j--; }
    }

    [ContextMenu("Project Knots To Road")]
    public void ProjectKnots()
    {
        if (!outputSpline) return;
        var s = outputSpline.Splines.Count > 0 ? outputSpline.Splines[0] : outputSpline.Spline;
        if (s == null) return;

        for (int i = 0; i < s.Count; i++)
        {
            var k = s[i];
            Vector3 wp = outputSpline.transform.TransformPoint((Vector3)k.Position);
            Vector3 start = wp + Vector3.up * projectRayHeight;

            if (Physics.Raycast(start, Vector3.down, out var hit, projectRayHeight * 2f, roadMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 newWp = hit.point + Vector3.up * projectYOffset;
                k.Position = (float3)outputSpline.transform.InverseTransformPoint(newWp);
                s.SetKnot(i, k);
            }
        }
        MarkSplineDirty();
        Debug.Log("[SplineFromWaypoints] Projected knots to road.");
    }

    void MarkSplineDirty()
    {
    #if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(outputSpline);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(outputSpline.gameObject.scene);
    #endif
    }
}
