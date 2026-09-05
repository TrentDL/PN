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

        /// <summary>True if the segment from->to crosses this polygon's boundary.</summary>
    // ADDED (polygon crossing pass): the polygon counterpart to EdgeZone.CrossedBy.
    // WHY HERE and not in GroundArea: this is pure polygon geometry - the same
    // "what shape am I" knowledge that already owns Contains() and the gizmo. A
    // subclass supplies meaning, not maths (DRY, same reasoning as the class note).
    //
    // A CROSSING test, not a proximity test, for the same reason EdgeZone uses one:
    // it cannot be tunnelled through at low frame rates or high speeds.
    //
    // NOTE: this walks every PATH, matching OnDrawGizmos below rather than the
    // "points" array - a collider with an island or a hole has a boundary on each,
    // and all of them are crossable.
    public bool CrossedBy(Vector2 from, Vector2 to)
    {
        if (Area == null) return false;

        for (int p = 0; p < Area.pathCount; p++)
        {
            Vector2[] path = Area.GetPath(p);

            // CHANGED from EdgeZone.CrossedBy: the modulo wrap. An EdgeCollider2D is
            // an OPEN polyline and stops at Length - 1; a polygon path is CLOSED, so
            // the last point connects back to the first and that closing segment is
            // as crossable as any other.
            for (int i = 0; i < path.Length; i++)
            {
                Vector2 a = transform.TransformPoint(path[i] + Area.offset);
                Vector2 b = transform.TransformPoint(path[(i + 1) % path.Length] + Area.offset);

                if (SegmentsCross(from, to, a, b)) return true;
            }
        }

        return false;
    }// end of function >:D

    /// <summary>Where along 'from->to' the crossing happens, pulled back just short of the boundary.</summary>
    // ADDED (polygon crossing pass): mirrors EdgeZone.StopShortOf, and for the same
    // reason - cancelling a blocked move outright makes the player stick to walls
    // when sliding along them at an angle.
    public Vector2 StopShortOf(Vector2 from, Vector2 to)
    {
        Vector2 direction = to - from;
        float distance = direction.magnitude;

        if (distance < 0.0001f) return from;

        // Walk back from the destination until the move no longer crosses. Cheap
        // because the step count is fixed and the test is trivial arithmetic.
        for (int step = 1; step <= 8; step++)
        {
            Vector2 candidate = Vector2.Lerp(to, from, step / 8f);
            if (!CrossedBy(from, candidate)) return candidate;
        }

        return from;   // nowhere along the move was clear - stay put
    }// end of function >:D

    // Standard orientation-based segment intersection.
    //
    // DUPLICATED from EdgeZone deliberately, following the precedent in TileStep's
    // header: a shared helper would mean a third type existing only to hold four
    // lines of arithmetic, and both copies are self-contained and stable. If a THIRD
    // caller ever appears, that judgement should be revisited - see the note in the
    // reply that produced this pass.
    private static bool SegmentsCross(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d1 = Cross(p3, p4, p1);
        float d2 = Cross(p3, p4, p2);
        float d3 = Cross(p1, p2, p3);
        float d4 = Cross(p1, p2, p4);

        return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
    }// end of function >:D

    private static float Cross(Vector2 a, Vector2 b, Vector2 p)
    {
        return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    }// end of function >:D


}