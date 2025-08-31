using UnityEngine;

public class AlignTrackToRoad : MonoBehaviour
{
    public Transform track;        // your SplineContainer object
    public Renderer roadRenderer;  // assign MeshRenderer from Plane.005 (the road)
    public float yLift = 0.20f;    // hover above road a bit

    [ContextMenu("Align Track To Road Bounds")]
    public void AlignNow()
    {
        if (!track || !roadRenderer) return;
        var c = roadRenderer.bounds.center;         // world center of the road mesh
        track.position = new Vector3(c.x, c.y + yLift, c.z);
        track.rotation = roadRenderer.transform.rotation;
        track.localScale = Vector3.one;
    }
}
