// InterceptZone.cs
//
// ADDED: sibling of AnimationZone. Same polygon-entry logic, but drives
// CubismRenderingInterceptController.SortingOrder instead of a Cubism art mesh's
// LocalSortingOrder.
//
// WHY A SEPARATE SCRIPT rather than one widened array: the two numbers live on
// different scales. LocalSortingOrder is small and model-local; the interceptor's
// SortingOrder is compared against the model's global draw-pass order (authored at
// 1509 in this project). One shared sortOrderInside cannot be correct for both, so
// a single component would need two target fields and a type test in every loop.
// Two components, one number each, no branching - each script has one reason to change.

using UnityEngine;
using Live2D.Cubism.Rendering.URP.RenderingInterceptor;   // CubismRenderingInterceptController

[RequireComponent(typeof(PolygonCollider2D))]
public class InterceptZone : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Whose position is tested. Usually the player root.")]
    public Transform watched;

    [Tooltip("Objects with a Cubism Rendering Intercept Controller. Dropping in the GameObject is fine - the component is found on it or its children.")]
    public Component[] interceptors;

    [Tooltip("SortingOrder to apply while inside. Same scale as the value authored on the interceptor, NOT the small numbers used by art meshes.")]
    public int sortOrderInside = 1509;

    [Header("Behaviour")]
    [Tooltip("ON: re-applies the order every frame while inside, so nothing else can steal it back.")]
    public bool holdWhileInside = true;

    [Tooltip("Seconds to hold the sorting order after the player exits. Cover the full fall duration.")]
    public float restoreDelay = 1f;

    [Header("Debug Visualization")]
    public bool showDebugGizmos = true;

    private PolygonCollider2D area;
    private bool wasInside = false;               // edge detection - act on ENTRY, not every frame
    private float exitTime = -1f;                 // when they left; negative means inside

    // ADDED: resolved once at startup so the per-frame loops never do a lookup or null check.
    private CubismRenderingInterceptController[] resolved;
    private int[] originalOrders;                 // whatever was authored, captured not hardcoded

    void Awake()
    {
        area = GetComponent<PolygonCollider2D>();

        // ADDED: two-pass fill into plain arrays. Bad entries are dropped here rather than
        // null-checked every frame in SetOrder. Arrays only - no extra namespace needed.
        CubismRenderingInterceptController[] temp =
            new CubismRenderingInterceptController[interceptors.Length];
        int count = 0;

        for (int i = 0; i < interceptors.Length; i++)
        {
            CubismRenderingInterceptController c = Resolve(interceptors[i]);
            if (c == null)
            {
                Debug.LogWarning(name + ": interceptors[" + i + "] has no CubismRenderingInterceptController - skipped.", this);
                continue;
            }
            temp[count] = c;
            count++;
        }

        // ADDED: only copy when something was skipped - normally temp is already the right size.
        if (count == temp.Length)
        {
            resolved = temp;
        }
        else
        {
            resolved = new CubismRenderingInterceptController[count];
            for (int i = 0; i < count; i++)
                resolved[i] = temp[i];
        }

        originalOrders = new int[resolved.Length];
        for (int i = 0; i < resolved.Length; i++)
            originalOrders[i] = resolved[i].SortingOrder;
    }// end of function >:D

    // ADDED: accepts the component itself, or finds it on that object or a child. Your
    // Inspector showed a Transform in the slot - dragging a GameObject assigns its Transform,
    // so without this the list would silently resolve to nothing.
    private static CubismRenderingInterceptController Resolve(Component c)
    {
        if (c == null) return null;

        CubismRenderingInterceptController direct = c as CubismRenderingInterceptController;
        if (direct != null) return direct;

        return c.GetComponentInChildren<CubismRenderingInterceptController>();
    }// end of function >:D

    void Update()
    {
        bool inside = area.OverlapPoint(watched.position);

        if (inside && !wasInside)
        {
            SetOrder(sortOrderInside);
            exitTime = -1f;                       // cancel any pending restore
        }
        // ADDED: re-assert every frame while inside. Entry-only assignment is a one-shot write -
        // anything that changes SortingOrder afterwards wins permanently.
        else if (inside && holdWhileInside)
        {
            SetOrder(sortOrderInside);
        }
        else if (!inside && wasInside)
        {
            exitTime = Time.time;                 // start the clock instead of restoring now
        }

        // ADDED: the delayed restore. A timer rather than a coroutine so a re-entry cancels
        // it by simply resetting exitTime.
        if (exitTime >= 0f && Time.time - exitTime >= restoreDelay)
        {
            Restore();
            exitTime = -1f;
        }

        wasInside = inside;
    }// end of function >:D

    private void SetOrder(int order)
    {
        for (int i = 0; i < resolved.Length; i++)
            resolved[i].SortingOrder = order;
    }// end of function >:D

    private void Restore()
    {
        for (int i = 0; i < resolved.Length; i++)
            resolved[i].SortingOrder = originalOrders[i];
    }// end of function >:D

    void OnDrawGizmos()
    {
        PolygonCollider2D poly = area != null ? area : GetComponent<PolygonCollider2D>();
        if (!showDebugGizmos || poly == null) return;

        // ADDED: cyan, so these read as distinct from AnimationZone's magenta at a glance.
        Gizmos.color = Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;

        for (int p = 0; p < poly.pathCount; p++)
        {
            Vector2[] path = poly.GetPath(p);
            for (int i = 0; i < path.Length; i++)
                Gizmos.DrawLine(path[i] + poly.offset, path[(i + 1) % path.Length] + poly.offset);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }// end of function >:D
}