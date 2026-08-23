// PolygonArea.cs
//
// ADDED: shared base class for GroundArea.
// WHY: "a polygon you can test a world point against, drawn as a coloured gizmo"
//      was about to be copy-pasted into a second component, so it lives here once
//      instead (DRY). Subclasses supply only a colour and their own meaning.

using UnityEngine;

[RequireComponent(typeof(PolygonCollider2D))]
public abstract class PolygonArea : MonoBehaviour
{
    [Header("Debug Visualization")]
    public bool showDebugGizmos = true;

    // ADDED: replaces GroundArea's public "gizmoColor" field.
    // WHY: colour is now the type's identity, not a per-instance setting somebody
    //      can accidentally change in the Inspector.
    protected abstract Color GizmoColor { get; }

    private PolygonCollider2D area;

    // CHANGED: lazy property instead of assigning in Awake().
    // WHY: OnDrawGizmos runs in edit mode where Awake never fires, so the old
    //      version drew nothing until you pressed Play.
    protected PolygonCollider2D Area
    {
        get
        {
            if (area == null) area = GetComponent<PolygonCollider2D>();
            return area;
        }
    }

    /// <summary>True if the world position is inside this polygon.</summary>
    // KEPT from GroundArea.Contains - same OverlapPoint test, now shared.
    public bool Contains(Vector2 worldPosition)
    {
        return Area != null && Area.OverlapPoint(worldPosition);
    }// end of function >:D

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || Area == null) return;

        Gizmos.color = GizmoColor;

        // CHANGED: set the matrix once instead of calling TransformPoint per point.
        Gizmos.matrix = transform.localToWorldMatrix;

        // CHANGED: loop every path instead of only the "points" array.
        // WHY: "points" is just path 0. A collider with an island or a hole in it
        //      drew as half a shape before.
        for (int p = 0; p < Area.pathCount; p++)
        {
            Vector2[] path = Area.GetPath(p);

            for (int i = 0; i < path.Length; i++)
            {
                // ADDED: + Area.offset - the old gizmo ignored the collider's offset
                // field, so a nudged collider drew in the wrong place.
                Gizmos.DrawLine(path[i] + Area.offset,
                                path[(i + 1) % path.Length] + Area.offset);
            }
        }

        Gizmos.matrix = Matrix4x4.identity;   // leave the state as we found it
    }// end of function >:D
}