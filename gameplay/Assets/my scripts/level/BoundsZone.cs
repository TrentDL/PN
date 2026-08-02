using System.Collections.Generic;
using UnityEngine;

// Multi-instance companion to BoundsManager. Does NOT replace it - BoundsManager
// stays the single global walkable area; these are extra zones you can place freely.
//
// Put as many of these in a scene as you like. Each needs a PolygonCollider2D.
//
// keepInside = true   -> objects must stay INSIDE this shape (a room, a ledge, a pen)
// keepInside = false  -> objects must stay OUTSIDE this shape (a rock, a table, a pit)
//
// The 'group' string decides WHO obeys this zone. A zone with group "npc" is ignored
// by anything querying group "player". Leave it "Default" if you don't need that yet.
//
// USAGE from a movement script:
//     if (!BoundsZone.IsWithinBounds(desired, "player"))
//         desired = BoundsZone.ClampToBounds(desired, "player");

[RequireComponent(typeof(PolygonCollider2D))]
public class BoundsZone : MonoBehaviour
{
    // All enabled zones. Registered on enable, removed on disable, so destroyed
    // or deactivated zones can't linger and constrain things invisibly.
    public static readonly List<BoundsZone> All = new List<BoundsZone>();

    [Header("Behaviour")]
    [Tooltip("ON  = objects must stay INSIDE this shape (walkable area).\n" +
             "OFF = objects must stay OUTSIDE this shape (obstacle).")]
    public bool keepInside = true;

    [Tooltip("Only objects querying this same group obey this zone. " +
             "e.g. 'player', 'npc', 'props'. Case-insensitive.")]
    public string group = "Default";

    [Tooltip("How far past the boundary to place a clamped object. " +
             "Too small and it can jitter on the edge; too large and it visibly pops.")]
    public float buffer = 0.02f;

    [Header("Debug Visualization")]
    public bool showDebugGizmos = true;
    [Tooltip("Used when keepInside is ON")]
    public Color insideColor = Color.green;
    [Tooltip("Used when keepInside is OFF")]
    public Color outsideColor = new Color(1f, 0.4f, 0.2f);

    private PolygonCollider2D zoneCollider;


    void Awake()
    {
        zoneCollider = GetComponent<PolygonCollider2D>();
    }


    void OnEnable()
    {
        if (zoneCollider == null) zoneCollider = GetComponent<PolygonCollider2D>();
        if (!All.Contains(this)) All.Add(this);
    }


    void OnDisable()
    {
        All.Remove(this);
    }


    private bool MatchesGroup(string queryGroup)
    {
        return string.Equals(group, queryGroup, System.StringComparison.OrdinalIgnoreCase);
    }


    private bool ContainsPoint(Vector2 worldPosition)
    {
        return zoneCollider != null && zoneCollider.OverlapPoint(worldPosition);
    }


    // ---------------------------------------------------------------------------
    // Public queries
    // ---------------------------------------------------------------------------

    /// <summary>
    /// True if the position satisfies every zone in the given group.
    /// </summary>
    /// <remarks>
    /// Combination rules, which are NOT the same for the two zone types:
    ///   keepInside  zones are OR'd  - you need to be inside AT LEAST ONE of them.
    ///                                 (Two adjoining rooms: being in either is fine.
    ///                                  AND-ing them would only allow the overlap.)
    ///   keepOutside zones are AND'd - you must be outside EVERY one of them.
    /// If a group has no keepInside zones at all, there's no containment constraint -
    /// only the obstacles apply.
    /// </remarks>
    public static bool IsWithinBounds(Vector2 worldPosition, string queryGroup = "Default")
    {
        bool hasInsideZone = false;
        bool insideSomeZone = false;

        for (int i = 0; i < All.Count; i++)
        {
            var zone = All[i];
            if (zone == null || !zone.MatchesGroup(queryGroup)) continue;

            bool contains = zone.ContainsPoint(worldPosition);

            if (zone.keepInside)
            {
                hasInsideZone = true;
                if (contains) insideSomeZone = true;
            }
            else if (contains)
            {
                return false;   // inside an obstacle - immediate fail
            }
        }

        return !hasInsideZone || insideSomeZone;
    }


    /// <summary>
    /// Nearest valid position for the given group. Returns the input unchanged if already valid.
    /// </summary>
    public static Vector2 ClampToBounds(Vector2 desiredPosition, string queryGroup = "Default")
    {
        var result = desiredPosition;

        // Pushing out of an obstacle can land you outside a containment zone, and
        // vice versa. A few passes settles the common cases; the cap stops a
        // pathological layout (obstacle straddling a narrow corridor) from hanging.
        for (int pass = 0; pass < 4; pass++)
        {
            if (IsWithinBounds(result, queryGroup)) return result;

            // 1. Eject from any obstacle first.
            bool ejected = false;
            for (int i = 0; i < All.Count; i++)
            {
                var zone = All[i];
                if (zone == null || zone.keepInside || !zone.MatchesGroup(queryGroup)) continue;
                if (!zone.ContainsPoint(result)) continue;

                result = zone.PushOut(result);
                ejected = true;
            }
            if (ejected) continue;

            // 2. Pull back into the nearest containment zone.
            float bestDistance = float.MaxValue;
            Vector2 bestPoint = result;
            bool found = false;

            for (int i = 0; i < All.Count; i++)
            {
                var zone = All[i];
                if (zone == null || !zone.keepInside || !zone.MatchesGroup(queryGroup)) continue;

                Vector2 candidate = zone.PullIn(result);
                float distance = (candidate - result).sqrMagnitude;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPoint = candidate;
                    found = true;
                }
            }

            if (!found) return result;
            result = bestPoint;
        }

        return result;
    }


    // ---------------------------------------------------------------------------
    // Edge math
    // ---------------------------------------------------------------------------

    // Point is OUTSIDE, bring it in. Collider2D.ClosestPoint is reliable here.
    private Vector2 PullIn(Vector2 outsidePosition)
    {
        if (zoneCollider == null) return outsidePosition;

        Vector2 edge = zoneCollider.ClosestPoint(outsidePosition);
        Vector2 inward = (edge - outsidePosition).normalized;
        return edge + inward * buffer;
    }


    // Point is INSIDE, push it out.
    //
    // IMPORTANT: this can't use Collider2D.ClosestPoint. That method returns the
    // input position unchanged when the point is already inside the collider, so
    // using it here would silently do nothing and the object would stay stuck in
    // the obstacle forever. We walk the polygon edges by hand instead.
    private Vector2 PushOut(Vector2 insidePosition)
    {
        if (zoneCollider == null) return insidePosition;

        Vector2 nearest = insidePosition;
        float bestDistance = float.MaxValue;
        bool found = false;

        for (int path = 0; path < zoneCollider.pathCount; path++)
        {
            Vector2[] points = zoneCollider.GetPath(path);

            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = LocalToWorld(points[i]);
                Vector2 b = LocalToWorld(points[(i + 1) % points.Length]);

                Vector2 candidate = ClosestPointOnSegment(a, b, insidePosition);
                float distance = (candidate - insidePosition).sqrMagnitude;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = candidate;
                    found = true;
                }
            }
        }

        if (!found) return insidePosition;

        Vector2 outward = (nearest - insidePosition).normalized;

        // Degenerate case: the point sits exactly on the edge, so there's no
        // direction to push. Nudge along the collider's own up axis instead.
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = transform.up;
        }

        return nearest + outward * buffer;
    }


    // PolygonCollider2D points are local AND shifted by the collider's offset.
    // Forgetting the offset makes gizmos and edge math drift from the real shape.
    private Vector2 LocalToWorld(Vector2 localPoint)
    {
        return transform.TransformPoint(localPoint + zoneCollider.offset);
    }


    private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;

        if (lengthSquared < 0.000001f) return a;   // degenerate segment

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
        return a + ab * t;
    }


    // ---------------------------------------------------------------------------
    // Gizmos
    // ---------------------------------------------------------------------------

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        if (zoneCollider == null) zoneCollider = GetComponent<PolygonCollider2D>();
        if (zoneCollider == null) return;

        Gizmos.color = keepInside ? insideColor : outsideColor;

        // Loop over pathCount, not just .points - a PolygonCollider2D can hold
        // several paths (a shape with holes, or a multi-part region), and reading
        // only .points draws just the first one.
        for (int path = 0; path < zoneCollider.pathCount; path++)
        {
            Vector2[] points = zoneCollider.GetPath(path);

            for (int i = 0; i < points.Length; i++)
            {
                Vector2 current = LocalToWorld(points[i]);
                Vector2 next = LocalToWorld(points[(i + 1) % points.Length]);
                Gizmos.DrawLine(current, next);
            }
        }
    }
}
