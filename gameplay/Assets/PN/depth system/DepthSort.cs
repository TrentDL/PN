using UnityEngine;
using UnityEngine.Rendering;   // SortingGroup
// Pass 7 (merge): added UnityEngine.Rendering - SortingGroup lives there. Built into Unity, no package.

// Pass 7 (merge): DepthSort now also does CubismDepthSort's job, so the project needs ONE
// depth-sorting script instead of two copies of the same math (DRY).
//   * Object has a Sorting Group  -> the whole group is sorted (multi-part player).
//   * Otherwise, a Sprite Renderer -> that one sprite is sorted (boxes, props).
// FILE NAME: must be DepthSort.cs to match the class name, or Unity cannot attach it.
[ExecuteAlways]
public class DepthSort : MonoBehaviour
{
    [Tooltip("Shared across sorted objects - keep consistent")]
    public float baseline = 0f;

    [Tooltip("How sensitive sorting is to Y changes")]
    public float precision = 100f;

    [Tooltip("Fixed value added to the computed order. " +
             "Tune this to land in the same range as your static scenery.")]
    public int sortOffset = 0;                  // ADDED: threshold/range knob

    [Tooltip("Optional: sort by this transform's Y instead of sprite bounds")]
    public Transform groundReference;

    private SpriteRenderer sr;
    private SortingGroup group;                 // Pass 7 (merge): the multi-part write target

    void Awake()
    {
        Cache();   // Pass 7 (merge): lookups moved into Cache() so Awake and LateUpdate share them (DRY)

        // Pass 7 (merge): a grouped object is a character whose art LIFTS during jumps, so
        // sorting by its own Y would push it behind things mid-jump. Warn once, not every frame.
        // Props (no group) are fine without one - they fall back to their sprite's bottom edge.
        if (group != null && groundReference == null)
            Debug.LogWarning(name + ": DepthSort on a Sorting Group has no Ground Reference - " +
                             "drag in GroundPoint or it will sort by its jump height.", this);
    }

    void LateUpdate()
    {
        // Pass 7 (merge): only retry the lookups when BOTH are missing. The old line retried
        // GetComponent every frame whenever sr was null; with two targets, a prop that simply
        // has no Sorting Group would have paid for a lookup every frame forever.
        if (sr == null && group == null)
        {
            Cache();
            if (sr == null && group == null) return;
        }

        // MODIFIED: add a tunable offset so dynamic orders land near static ones
        int order = sortOffset - (int)((SortY() - baseline) * precision);
        // Pass 7 (merge): the measure and the write moved into SortY() / WriteOrder(),
        // so each piece has one job. The math itself is unchanged.
        WriteOrder(order);
    }

    // Pass 7 (merge): finds the write targets. One place, called from Awake and LateUpdate.
    private void Cache()
    {
        sr = GetComponent<SpriteRenderer>();
        group = GetComponent<SortingGroup>();
    }

    // Pass 7 (merge): WHERE to measure from. Same order of preference as before:
    // ground reference first, then the sprite's bottom edge. The last fallback (own Y)
    // is new - it covers a Sorting Group object that has no Sprite Renderer of its own.
    private float SortY()
    {
        if (groundReference != null) return groundReference.position.y;
        if (sr != null) return sr.bounds.min.y;
        return transform.position.y;
    }

    // Pass 7 (merge): WHERE to write. A Sorting Group wins when present - Unity sorts a grouped
    // object by the group's order, and any Sprite Renderer inside it only layers against its siblings.
    // The "!=" checks write only when the value changed (carried over from CubismDepthSort).
    private void WriteOrder(int order)
    {
        if (group != null)
        {
            if (group.sortingOrder != order) group.sortingOrder = order;
        }
        else if (sr.sortingOrder != order)
        {
            sr.sortingOrder = order;
        }
    }
}