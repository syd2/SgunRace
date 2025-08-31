using UnityEngine;

/// Smoothly animates lateral lane offset for the TrackFollower.
/// Index: 0=left, 1=center, 2=right  →  offsets: -L, 0, +L.
[ExecuteAlways]
public class LaneMover : MonoBehaviour
{
    [Header("Lanes")]
    [Range(0,2)] public int startLaneIndex = 1;
    public float laneOffset = 1.25f;      // L
    [Tooltip("Seconds to complete a lane change.")]
    public float laneSwapTime = 0.3f;

    [Header("Input (optional)")]
    public bool enableInput = true;
    public KeyCode leftKeyA = KeyCode.A;
    public KeyCode leftKey  = KeyCode.LeftArrow;
    public KeyCode rightKeyD = KeyCode.D;
    public KeyCode rightKey  = KeyCode.RightArrow;

    // Runtime
    public int LaneIndex { get; private set; }
    public float CurrentOffset { get; private set; }  // what TrackFollower reads

    private float targetOffset;
    private float t; // 0..1 progress of current swap
    private float lastSwapDir; // -1 or +1 for easing

    void OnEnable()
    {
        SetLane(startLaneIndex, instant:true);
    }

    void Update()
    {
        if (enableInput && Application.isPlaying)
        {
            if (Input.GetKeyDown(leftKeyA) || Input.GetKeyDown(leftKey))
                SetLane(LaneIndex - 1);
            if (Input.GetKeyDown(rightKeyD) || Input.GetKeyDown(rightKey))
                SetLane(LaneIndex + 1);
        }

        // Animate toward target
        if (Mathf.Approximately(CurrentOffset, targetOffset))
            return;

        if (laneSwapTime <= 0f)
        {
            CurrentOffset = targetOffset;
            return;
        }

        // Smoothstep over laneSwapTime
        float dir = Mathf.Sign(targetOffset - CurrentOffset);
        if (dir != 0f && dir != lastSwapDir) { t = 0f; lastSwapDir = dir; }

        t += Time.deltaTime / Mathf.Max(1e-4f, laneSwapTime);
        float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        CurrentOffset = Mathf.Lerp(CurrentOffset, targetOffset, s);

        // Snap at end
        if (t >= 1f || Mathf.Abs(CurrentOffset - targetOffset) < 1e-3f)
        {
            CurrentOffset = targetOffset;
            t = 0f;
        }
    }

    public void SetLane(int newIndex, bool instant = false)
    {
        newIndex = Mathf.Clamp(newIndex, 0, 2);
        LaneIndex = newIndex;
        targetOffset = IndexToOffset(LaneIndex);

        if (instant)
        {
            CurrentOffset = targetOffset;
            t = 0f;
        }
        // else Update() will blend
    }

    private float IndexToOffset(int idx)
    {
        switch (idx)
        {
            case 0: return -laneOffset;
            case 1: return 0f;
            case 2: return +laneOffset;
            default: return 0f;
        }
    }
}
