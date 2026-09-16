using UnityEngine;
using Live2D.Cubism.Rendering;

// ADDED: Cubism equivalent of DepthSort. Drives the model's SortingOrder
// from ground Y so the Live2D model sorts against sprites on the same scale.
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
    private CubismRenderController renderController;

    void Awake()
    {
        renderController = GetComponent<CubismRenderController>();
    }



    // ATTENTION Artists do not touch anything below this line...
    //Why: it will break the depth sorting of the player character
    void LateUpdate()
    {
        if (renderController == null)
        {
            renderController = GetComponent<CubismRenderController>();
            if (renderController == null) return;
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
        renderController.SortingOrder = order;
    }
}
