// EdgeZone.cs
//
// ADDED: line-based rules, as a companion to GroundArea rather than a subclass.
//
// WHY NOT PolygonArea: EdgeCollider2D is an open polyline with no interior, so
//      OverlapPoint is always false on it. Contains() - which RulingAreaAt,
//      AreaAt, TileStep and the movement lock are all built on - cannot work.
//      Inheriting would have handed every one of those callers a component that
//      silently answers "no" to everything.
//
// WHAT IT ANSWERS INSTEAD: not "what is the ground at this point" but "may I
//      cross this line this frame". That is a segment-vs-segment test between the
//      player's current and desired positions, which is exactly the right shape
//      for one-way ledges and jump-only walls.
//
// THE BOOLS, re-read for a line:
//      canJumpOver     - crossing is allowed while airborne
//      canFallThrough  - crossing is allowed while FALLING specifically
//      Both off        - solid line, never crossed
//      Both on         - crossable in the air, blocked on foot
//
//      canFallThrough alone is the one-way ledge: walk into it and stop, fall
//      through it from above. Note this is a deliberately different reading from
//      GroundArea, where the same bool means "there is no floor here".
//
// NOT INCLUDED: lockMovementInside and elevation. A line has no inside to lock
//      you into and no surface to stand on, so both would be dead fields.

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(EdgeCollider2D))]
public class EdgeZone : MonoBehaviour
{
    // Mirrors GroundArea.All. Same register/unregister pattern, different list -
    // the two are never queried together, so one shared list would only force
    // every caller to type-check what it pulled out.
    public static readonly List<EdgeZone> All = new List<EdgeZone>();

    [Header("Crossing Rules")]
    [Tooltip("ON: the player can cross this line while airborne (jumping or falling).")]
    public bool canJumpOver = false;

    // ADDED: the elevation the player's OWN height must reach before canJumpOver /
    // canFallThrough are allowed to grant passage. Below this, the edge blocks
    // regardless of those bools - matches the sketch: the wall holds until the
    // green dot (the root transform) clears the block's top.
    [Tooltip("0 = no gate, existing behaviour. Above 0: canJumpOver/canFallThrough only apply once the player's elevation reaches this height.")]
    public float heightGateThreshold = 0f;

    [Tooltip("ON: the player can cross this line while falling. A one-way ledge on its own.")]
    public bool canFallThrough = false;

    [Header("Overlap")]
    // ADDED: matches GroundArea.priority in spirit - highest wins when lines cross
    // each other. Kept because two edges meeting at a corner is common and the
    // player is otherwise blocked by whichever happened to register first.
    [Tooltip("Higher wins when edges overlap at a corner. Leave 0 normally.")]
    public int priority = 0;

    [Header("Debug Visualization")]
    public bool showDebugGizmos = true;

    private EdgeCollider2D edge;

    // Same lazy pattern as PolygonArea.Area, and for the same reason: OnDrawGizmos
    // runs in edit mode where Awake never fires.
    private EdgeCollider2D Edge
    {
        get
        {
            if (edge == null) edge = GetComponent<EdgeCollider2D>();
            return edge;
        }
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }// end of function >:D

    void OnDisable()
    {
        All.Remove(this);
    }// end of function >:D

    /// <summary>May the player move from 'from' to 'to' given how they are moving?</summary>
    // The one public question. Returns the blocking edge, or null if the move is
    // clear - returning the edge rather than a bool so the caller can clamp against
    // it instead of only being told "no".
    public static EdgeZone Blocker(Vector2 from, Vector2 to, bool airborne, bool falling, float playerElevation)
    {
        EdgeZone best = null;

        for (int i = 0; i < All.Count; i++)
        {
            EdgeZone z = All[i];

            // ADDED: the gate. Below threshold, this edge ignores its own permission
            // bools entirely - it behaves as if both were false, same as a solid wall.
            bool gateOpen = playerElevation >= z.heightGateThreshold;

            if (gateOpen && falling && z.canFallThrough) continue;
            if (gateOpen && airborne && z.canJumpOver) continue;

            if (!z.CrossedBy(from, to)) continue;

            if (best == null || z.priority > best.priority) best = z;
        }

        return best;
    }// end of function >:D

    /// <summary>True if the segment from->to crosses any span of this polyline.</summary>
    // A CROSSING test, not a proximity test, for the same reason FallCoroutine uses
    // one: it cannot be tunnelled through at low frame rates or high speeds.
    public bool CrossedBy(Vector2 from, Vector2 to)
    {
        if (Edge == null) return false;

        Vector2[] points = Edge.points;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 a = transform.TransformPoint(points[i] + Edge.offset);
            Vector2 b = transform.TransformPoint(points[i + 1] + Edge.offset);

            if (SegmentsCross(from, to, a, b)) return true;
        }

        return false;
    }// end of function >:D

    /// <summary>Where along 'from->to' the crossing happens, pulled back just short of the line.</summary>
    // ADDED: so a blocked move stops AT the edge instead of being cancelled outright.
    // Cancelling makes the player stick to walls when sliding along them at an angle.
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

    // Standard orientation-based segment intersection. Kept private and static:
    // nothing outside this file needs it, and it depends on no instance state.
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

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || Edge == null) return;

        // Colour by permissiveness, same idea as GroundArea's gizmo - magenta for a
        // solid line, orange once anything can cross it.
        Gizmos.color = (canJumpOver || canFallThrough) ? new Color(1f, 0.5f, 0f) : Color.magenta;
        Gizmos.matrix = transform.localToWorldMatrix;

        Vector2[] points = Edge.points;
        for (int i = 0; i < points.Length - 1; i++)
            Gizmos.DrawLine(points[i] + Edge.offset, points[i + 1] + Edge.offset);

        Gizmos.matrix = Matrix4x4.identity;
    }// end of function >:D
}