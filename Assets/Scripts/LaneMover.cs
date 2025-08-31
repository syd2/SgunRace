using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class LaneMover : MonoBehaviour
{
    // ----- Lane layout -----
    [Header("Lane Layout")]
    [Tooltip("Number of lanes across the road (odd recommended so there is a center lane).")]
    public int laneCount = 3;
    [Tooltip("Distance in meters between lane centers.")]
    public float laneWidth = 1.8f;

    // ----- Swap behaviour -----
    [Header("Swap")]
    [Tooltip("Seconds to tween between lanes.")]
    public float laneSwapTime = 0.18f;
    [Tooltip("Allow one extra queued step while swapping (e.g., double-tap).")]
    public bool bufferNext = true;

    // ----- Input -----
    [Header("Input (uses your existing 'move' action)")]
    [Tooltip("Reference to the Vector2 'move' action (WASD/arrows). X drives left/right swaps.")]
    public InputActionReference move;  // Vector2

    [Header("Input Hysteresis")]
    [Tooltip("Press threshold on |X| to register a left/right press.")]
    [Range(0.05f, 0.95f)] public float pressThreshold = 0.5f;
    [Tooltip("Release threshold to re-arm after the stick/key returns toward center.")]
    [Range(0.0f, 0.95f)] public float releaseThreshold = 0.25f;

    // ----- Exposed to TrackFollower -----
    public float CurrentOffset { get; private set; } = 0f;
    public int LaneIndex => _laneIndex;

    // runtime
    int _laneIndex;
    int _targetIndex;
    bool _isSwapping;
    int _bufferedDelta;   // -1/0/+1

    // latches to generate one event per press
    bool _leftLatched;
    bool _rightLatched;

    void OnEnable()
    {
        _laneIndex = _targetIndex = CenterIndex();
        UpdateOffsetImmediate();

        if (move && move.action != null && !move.action.enabled)
            move.action.Enable();
    }

    void OnDisable()
    {
        if (move && move.action != null && move.action.enabled)
            move.action.Disable();
    }

    void Update()
    {
        // Read X from your move (Vector2)
        if (move == null || move.action == null) return;

        Vector2 v = move.action.ReadValue<Vector2>();
        float x = v.x;

        // Latch logic (one swap per press)
        // Go Right
        if (x > pressThreshold && !_rightLatched)
        {
            _rightLatched = true;
            _leftLatched  = false;
            HandleInput(+1);
        }
        else if (x < releaseThreshold) // allow re-arming when it returns near center
        {
            _rightLatched = false;
        }

        // Go Left
        if (x < -pressThreshold && !_leftLatched)
        {
            _leftLatched = true;
            _rightLatched = false;
            HandleInput(-1);
        }
        else if (x > -releaseThreshold)
        {
            _leftLatched = false;
        }
    }

    void HandleInput(int delta)
    {
        int min = 0;
        int max = Mathf.Max(0, laneCount - 1);

        if (!_isSwapping)
        {
            _bufferedDelta = 0;
            _targetIndex = Mathf.Clamp(_laneIndex + delta, min, max);
            if (_targetIndex != _laneIndex)
                StartCoroutine(SwapRoutine(_laneIndex, _targetIndex));
        }
        else if (bufferNext)
        {
            // record one extra step to apply after current swap
            _bufferedDelta = Mathf.Clamp(_bufferedDelta + delta, -1, +1);
        }
    }

    IEnumerator SwapRoutine(int from, int to)
    {
        _isSwapping = true;

        float t = 0f;
        float fromOff = LaneCenter(from);
        float toOff   = LaneCenter(to);

        while (t < laneSwapTime)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / laneSwapTime);
            // smoothstep ease
            u = u * u * (3f - 2f * u);
            CurrentOffset = Mathf.Lerp(fromOff, toOff, u);
            yield return null;
        }

        _laneIndex = to;
        CurrentOffset = LaneCenter(_laneIndex);
        _isSwapping = false;

        // apply one buffered step if any
        if (_bufferedDelta != 0)
        {
            int min = 0;
            int max = Mathf.Max(0, laneCount - 1);
            int nextTarget = Mathf.Clamp(_laneIndex + _bufferedDelta, min, max);
            _bufferedDelta = 0;
            if (nextTarget != _laneIndex)
                StartCoroutine(SwapRoutine(_laneIndex, nextTarget));
        }
    }

    int CenterIndex() => (laneCount - 1) / 2;

    float LaneCenter(int idx) => (idx - CenterIndex()) * laneWidth;

    void UpdateOffsetImmediate() => CurrentOffset = LaneCenter(_laneIndex);
}
