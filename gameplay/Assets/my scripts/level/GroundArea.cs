// GroundArea.cs
//
// RENAMED: BoundsManager  ->  GroundArea   (file must match the class name)
// WHY: BoundsManager was a singleton that owned ONE PolygonCollider2D, so a
//      second walkable region was impossible. This is a plain component -
//      add one to every ground object (GroundBounds 1, GroundBounds 2, ...).
//
// RENAMED MEMBERS (old -> new):
//      Instance          -> All          (a list, not a single instance)
//      boundsCollider    -> area
//      IsWithinBounds()  -> Contains()
//      ClampToBounds()   -> ClampInside()
// ADDED: elevation, AreaAt()

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PolygonCollider2D))]
public class GroundArea : MonoBehaviour
{
    // ADDED: replaces the old "Instance" singleton. Every enabled area registers
    // itself so the player can ask which ground it is standing on.
    public static readonly List<GroundArea> All = new List<GroundArea>();

    // ADDED: the "ground 2 sits higher" part. Visual only - the player's model is
    // lifted by this much while standing here. Leave 0 for the base floor.
    // Units match jumpHeight (local units of visualRoot's parent).
    [Tooltip("How high the player's model sits while standing on this ground. 0 = base floor.")]
    public float elevation = 0f;

    [Header("Debug Visualization")]
    public bool showDebugGizmos = true;
    public Color gizmoColor = Color.green;

    private PolygonCollider2D area;   // RENAMED from boundsCollider

    void Awake()
    {
        // CHANGED: no singleton guard anymore - multiple GroundAreas is the point.
        area = GetComponent<PolygonCollider2D>();
    }// end of function >:D

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }// end of function >:D

    void OnDisable()
    {
        All.Remove(this);   // ADDED: keeps the list clean on scene unload / disable
    }// end of function >:D

    /// <summary>True if the world position is inside this ground's polygon.</summary>
    // KEPT from BoundsManager.IsWithinBounds - same OverlapPoint test, now per area.
    public bool Contains(Vector2 worldPosition)
    {
        return area != null && area.OverlapPoint(worldPosition);
    }// end of function >:D

    /// <summary>Nearest valid point inside this ground. Used to stop at the edge.</summary>
    // KEPT from BoundsManager.ClampToBounds, minus the null-collider fail-safe
    // (RequireComponent now guarantees the collider exists).
    public Vector2 ClampInside(Vector2 desiredPosition)
    {
        if (Contains(desiredPosition)) return desiredPosition;

        Vector2 edge = area.ClosestPoint(desiredPosition);

        // Nudge inward so the player does not sit exactly on the line and jitter.
        return edge + (edge - desiredPosition).normalized * 0.01f;
    }// end of function >:D

    /// <summary>Which ground is under this point? Null if none (a gap).</summary>
    // ADDED: the lookup the player needs to decide where it landed.
    // Highest elevation wins when two areas overlap, so a ledge drawn on top of
    // the floor takes priority.
    public static GroundArea AreaAt(Vector2 worldPosition)
    {
        GroundArea best = null;

        for (int i = 0; i < All.Count; i++)
        {
            GroundArea a = All[i];
            if (a.Contains(worldPosition) && (best == null || a.elevation > best.elevation))
                best = a;
        }

        return best;
    }// end of function >:D

    void OnDrawGizmos()
    {
        // CHANGED: fetch the collider here instead of relying on Awake - Awake does
        // not run in edit mode, so the old version drew nothing until you hit Play.
        PolygonCollider2D poly = area != null ? area : GetComponent<PolygonCollider2D>();
        if (!showDebugGizmos || poly == null) return;

        Gizmos.color = gizmoColor;

        Vector2[] points = poly.points;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 currentPoint = transform.TransformPoint(points[i]);
            Vector2 nextPoint = transform.TransformPoint(points[(i + 1) % points.Length]);
            Gizmos.DrawLine(currentPoint, nextPoint);
        }
    }// end of function >:D
}
