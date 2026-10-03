using UnityEngine;
using UnityEngine.Rendering;   // SortingGroup
// Pass 2 (Live2D removal): removed "using Live2D.Cubism.Rendering;"
// Why: the SDK is gone, so this line would cause a compile error.
// Pass 6 (group sort): added UnityEngine.Rendering - SortingGroup lives there. Built into Unity.

// ADDED: Cubism equivalent of DepthSort. Drives the model's SortingOrder
// from ground Y so the Live2D model sorts against sprites on the same scale.
// Pass 2 (Live2D removal): now drives a standard Unity Renderer instead of a Live2D model.
// Pass 6 (group sort): now drives a SortingGroup, so ALL sprite parts move through the
// draw order together - the same job CubismRenderController did for the whole model.
// Each part's own Order in Layer now only layers it against its siblings (like LocalSortingOrder).
[ExecuteAlways]
public class CubismDepthSort : MonoBehaviour
{
    [Tooltip("MUST match the baseline used by your sprite DepthSort")]
    public float baseline = 0f;

    [Tooltip("MUST match the precision used by your sprite DepthSort")]
    public float precision = 100f;

    [Tooltip("Fixed offset to land in the same range as static scenery")]
    public int sortOffset = 0;

    [Tooltip("Sort by this transform's Y (e.g. a feet GroundPoint). " +
             "If empty, uses this object's own transform Y.")]
    public Transform groundReference;

    // ADDED: reference to the model's render controller (write target)
    // Pass 2 (Live2D removal): type changed from CubismRenderController to Renderer.
    // Why: Renderer is the parent class of SpriteRenderer, MeshRenderer, etc.,
    // so this works with whichever built-in renderer is on the object (polymorphism).
    // Pass 6 (group sort): type changed from Renderer to SortingGroup. Name kept so the
    // rest of the script reads the same.
    private SortingGroup targetRenderer;

    void Awake()
    {
        targetRenderer = GetComponent<SortingGroup>();

        // Pass 6 (group sort): say so ONCE instead of failing silently every frame.
        // Why: the old silent "return" in LateUpdate hid a missing component completely.
        if (targetRenderer == null)
            Debug.LogWarning(name + ": CubismDepthSort needs a Sorting Group on this same object.", this);

        // Pass 6 (group sort): warn if sorting would follow the jump instead of the feet.
        if (groundReference == null)
            Debug.LogWarning(name + ": Ground Reference is empty - sorting will follow this object's Y, " +
                             "which rises during jumps. Drag in GroundPoint.", this);
    }



    // ATTENTION Artists do not touch anything below this line...
    //Why: it will break the depth sorting of the player character
    void LateUpdate()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<SortingGroup>();
            // Pass 6 (group sort): Renderer -> SortingGroup.
            if (targetRenderer == null) return;
        }

        // Same measurement convention as the sprite system:
        // use a ground reference if provided, else this transform's Y.
        float sortY = (groundReference != null)
            ? groundReference.position.y
            : transform.position.y;

        // IDENTICAL math to DepthSort so the model shares the scale.
        int order = sortOffset - (int)((sortY - baseline) * precision);

        // MODIFIED: write to Cubism's controller, not a SpriteRenderer.
        // The setter early-returns if unchanged, so this is cheap.
        // Pass 2 (Live2D removal): now writes to Renderer.sortingOrder.
        // Why the "if": Unity's setter doesn't promise the early-return Cubism's did,
        // so this check keeps the same "only write when it changed" behavior.
        // Pass 6 (group sort): same check, now on SortingGroup.sortingOrder.
        if (targetRenderer.sortingOrder != order)
        {
            targetRenderer.sortingOrder = order;
        }
    }
}