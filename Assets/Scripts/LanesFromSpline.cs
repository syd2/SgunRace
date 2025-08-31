using UnityEngine;
using UnityEngine.Splines;

[ExecuteAlways]
public class LanesFromSpline : MonoBehaviour
{
    [Header("Inputs")]
    public SplineContainer center;      // assign your Track's SplineContainer
    public LineRenderer left;
    public LineRenderer right;

    [Header("Settings")]
    public float laneOffset = 1.25f;    // L (half lane spacing)
    public int samples = 100;           // smoothness of the line
    public float yOffset = 0.01f;       // lift above plane to avoid z-fight

    [ContextMenu("Regenerate Lanes")]
    public void Regenerate()
    {
        if (center == null || left == null || right == null) return;

        // Get first spline (works for Splines 2.x and 1.x)
        var spline = center.Splines.Count > 0 ? center.Splines[0] : center.Spline;
        if (spline == null) return;

        // Prepare arrays
        if (samples < 2) samples = 2;
        var leftPos  = new Vector3[samples];
        var rightPos = new Vector3[samples];

        Vector3 lastRight = Vector3.right; // fallback if tangent is degenerate

        for (int i = 0; i < samples; i++)
        {
            float t = (samples == 1) ? 0f : i / (float)(samples - 1);

            // Evaluate in spline (local) space
            Unity.Mathematics.float3 posL, tanL, upL;
            SplineUtility.Evaluate(spline, t, out posL, out tanL, out upL);

            // Convert to world space
            Vector3 worldPos = center.transform.TransformPoint((Vector3)posL);
            Vector3 worldTan = center.transform.TransformDirection(((Vector3)tanL)).normalized;

            // Compute right vector from tangent
            Vector3 rightVec = Vector3.Cross(Vector3.up, worldTan);
            if (rightVec.sqrMagnitude < 1e-6f) rightVec = lastRight; // fallback
            else rightVec.Normalize();
            lastRight = rightVec;

            // Offset left/right and lift a hair above the plane
            Vector3 lift = Vector3.up * yOffset;
            leftPos[i]  = worldPos - rightVec * laneOffset + lift;
            rightPos[i] = worldPos + rightVec * laneOffset + lift;
        }

        // Assign to renderers
        left.positionCount = samples;
        right.positionCount = samples;
        left.useWorldSpace = true;
        right.useWorldSpace = true;
        left.SetPositions(leftPos);
        right.SetPositions(rightPos);
    }

    void OnValidate()
    {
        // Auto-regenerate in editor when values change
        if (center && left && right) Regenerate();
    }
}
