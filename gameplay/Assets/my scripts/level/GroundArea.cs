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
//
// CHANGED: derives from PolygonArea.
// WHY: Contains(), the collider reference and the gizmo drawing moved to that base
//      so the shape/gizmo code is written once.
//
// ADDED: the four surface types, as two bools rather than four.
// WHY: the four cases are the four combinations of two independent questions -
//      "can I fall through it?" and "can I jump over it?" - so they are stored as
//      two bools and named by one read-only property. Four separate bools would
//      let you tick contradictory pairs.
//
//      canFallThrough  canJumpOver   Type        Behaviour
//      --------------  -----------   ---------   --------------------------------
//      false           false         Wall        blocks; jumping is ignored
//      false           true          JumpWall    blocks on foot, clears in a jump
//      true            true          Pit         fall in, or jump across
//      true            false         JumpZone    land on it, but cannot fall through
//
// ADDED (overlap pass): lockMovementInside and priority.
// WHY: the surface bools describe WHAT an area is. They cannot describe WHICH area
//      answers when several cover the same point - every overlapping area was
//      answering at once, so a jumpable hole drawn on solid floor could never win.
//      'priority' picks the one that speaks; the bools then say what it says.
//
// ADDED (confinement pass): blockWalkOff, allowWalkOffIntoGap, stopAtGroundPoint.
//
//      FallCoroutine has THREE entry points, and the flags do not all reach them:
//        1. the walk-off branch in ApplyMovement   - blockWalkOff guards it
//        2. the step-down branch in ApplyMovement  - blockWalkOff guards it
//        3. the apex of JumpCoroutine's ascent     - nothing prevents it
//
//      That is why blockWalkOff is NOT named "fallingDisabled": a jump's descent
//      begins in JumpCoroutine and that flag never sees it.
//
// REPLACED (stop pass): disableFalling -> stopAtGroundPoint.
// WHY: disableFalling skipped FallCoroutine entirely, so a missed jump snapped back
//      with no motion at all - it read as a glitch. The fall was never the problem;
//      only its OUTCOME was. stopAtGroundPoint lets the whole descent play and
//      simply refuses to let it pass below this area's height.
//
//      THE CONFINEMENT FLAGS side by side:
//        (none)              walk off = drop,  missed jump = falls to fallFloor
//        blockWalkOff        walk off = STOP,  missed jump = falls to fallFloor
//        stopAtGroundPoint   walk off = drop*, missed jump = falls, lands back here
//        lockMovementInside  walk off = stop,  jump out = NO
//
//      * with stopAtGroundPoint on, a walk-off also stops at this height - both go
//        through FallCoroutine. Pair with blockWalkOff off if that is unwanted.

using System.Collections.Generic;
using UnityEngine;

public class GroundArea : PolygonArea
{
    // ADDED: replaces the old "Instance" singleton. Every enabled area registers
    // itself so the player can ask which ground it is standing on.
    public static readonly List<GroundArea> All = new List<GroundArea>();

    // ADDED: the "ground 2 sits higher" part. Visual only - the player's model is
    // lifted by this much while standing here. Leave 0 for the base floor.
    // Units match jumpHeight (local units of visualRoot's parent).
    // NOTE: read through ElevationAt(), never directly - an ElevationRamp on this
    // object overrides it.
    [Header("Height")]
    [Tooltip("How high the player's model sits while standing on this ground. 0 = base floor. Ignored if an ElevationRamp is attached.")]
    public float elevation = 0f;

    [Header("Surface Type")]
    // ADDED: is there anything to stand on here?
    [Tooltip("OFF: solid, the player stands on it.  ON: open space, the player falls through it.")]
    public bool canFallThrough = false;

    // ADDED: does a jump clear this area? Read only while airborne, so it never
    // affects walking.
    [Tooltip("OFF: a jump does nothing here.  ON: the player can pass over this area in a jump.")]
    public bool canJumpOver = false;

    [Header("Confinement")]
    // ADDED: restores playercontrol1's behaviour for this one area. That version had
    // no fall at all - IsWithinBounds/ClampToBounds simply stopped you at the edge.
    // Covers the two WALK-OFF paths only. A jump's descent is not a walk-off.
    [Tooltip("ON: walking off this area's edge stops you instead of dropping you. Jumping still leaves, and a missed jump still falls.")]
    public bool blockWalkOff = false;

    // ADDED: the opposite of blockWalkOff. Normally, stepping off an edge with no tile
    // below clamps you - that is the world's edge and stopping is correct. ON: treat
    // it as an open drop instead, so you fall and the pit return catches you.
    [Tooltip("ON: walking off this area with nothing below drops you instead of stopping. Pairs with fallFloor.")]
    public bool allowWalkOffIntoGap = false;

    // ADDED (stop pass): a fall that STARTS here always ends here. The descent still
    // plays in full - the arc, the shadow shrink, the acceleration - it just cannot
    // pass below this area's own height.
    // REPLACES disableFalling, which skipped the coroutine entirely and snapped the
    // player back with no motion. Keeping the animation and changing only the outcome
    // is what makes a missed jump read as a stumble rather than a glitch.
    [Tooltip("ON: a fall from this area stops at this area's height instead of continuing past it. The fall still animates.")]
    public bool stopAtGroundPoint = false;

    // ADDED: jumping allowed, walking is not. The player can leave the ground but not
    // the polygon. Constrains X/Y; the surface bools constrain height.
    [Tooltip("ON: the player is clamped inside this area even while airborne. Jump straight up, but not out.")]
    public bool lockMovementInside = false;

    // ADDED: what a locked area does at its boundary.
    // 0 = clamp, the player stops exactly at the edge (original behaviour).
    // Above 0 = push back inward at this speed, in world units per second. Reads
    // better for a force field, and avoids the edge-jitter a clamp can produce.
    [Tooltip("0 = hard clamp at the edge. Above 0 = push the player back inward at this speed.")]
    public float pushBackSpeed = 0f;

    [Header("Overlap")]
    // ADDED: who wins when areas overlap. The HIGHEST priority area covering the
    // point decides the surface rules outright; equal priorities fall back to
    // highest elevation.
    // NOTE: governs SURFACE rules only. The confinement flags are read by
    // containment, not by ruling - see the loop in PlayerControl3.ApplyMovement.
    [Tooltip("Higher wins when areas overlap. Leave 0 for ordinary ground.")]
    public int priority = 0;

    // ADDED (ramp pass): optional per-position elevation. Null on a flat area, which
    // is the common case - hence the cache rather than a GetComponent per query.
    private ElevationRamp ramp;
    private bool rampChecked = false;

    private ElevationRamp Ramp
    {
        get
        {
            // Lazy for the same reason PolygonArea.Area is: OnDrawGizmos runs in edit
            // mode where Awake never fires.
            if (!rampChecked)
            {
                ramp = GetComponent<ElevationRamp>();
                rampChecked = true;
            }
            return ramp;
        }
    }

    // ADDED: one word for the surface pair, for gizmo colour and for debugging.
    // A property rather than a stored field so the two can never disagree (DRY).
    public SurfaceType Type
    {
        get
        {
            if (canFallThrough) return canJumpOver ? SurfaceType.Pit : SurfaceType.JumpZone;
            return canJumpOver ? SurfaceType.JumpWall : SurfaceType.Wall;
        }
    }

    // CHANGED: was a public "gizmoColor" field, now derived from the type.
    // WHY: colour is now information - you can read a level's rules at a glance
    //      instead of trusting whoever set the swatch.
    // NOTE: colour reads the two surface bools only, so a priority-1 area, a locked
    //       pen and a ramp all look like ordinary ground of their type.
    protected override Color GizmoColor
    {
        get
        {
            switch (Type)
            {
                case SurfaceType.Pit:      return Color.red;      // fall in, jump across
                case SurfaceType.JumpZone: return Color.cyan;     // land on, never fall through
                case SurfaceType.JumpWall: return Color.yellow;   // clears only in a jump
                default:                   return Color.green;    // Wall / ordinary floor
            }
        }
    }

    // REMOVED: Awake() and the private "area" field - the base class owns the
    // collider reference now.

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }// end of function >:D

    void OnDisable()
    {
        All.Remove(this);   // ADDED: keeps the list clean on scene unload / disable
    }// end of function >:D

    /// <summary>This area's height at a world position. Flat areas ignore the argument.</summary>
    // ADDED (ramp pass): the ONE place elevation is resolved. Callers must use this
    // rather than the field, or a ramped area silently behaves as flat.
    public float ElevationAt(Vector2 worldPosition)
    {
        return (Ramp != null) ? Ramp.ElevationAt(worldPosition) : elevation;
    }// end of function >:D

    /// <summary>Nearest valid point inside this ground. Used to stop at the edge.</summary>
    // KEPT from BoundsManager.ClampToBounds, minus the null-collider fail-safe
    // (RequireComponent on the base now guarantees the collider exists).
    public Vector2 ClampInside(Vector2 desiredPosition)
    {
        if (Contains(desiredPosition)) return desiredPosition;

        Vector2 edge = Area.ClosestPoint(desiredPosition);

        // Nudge inward so the player does not sit exactly on the line and jitter.
        return edge + (edge - desiredPosition).normalized * 0.01f;
    }// end of function >:D

    /// <summary>The area whose rules apply at this point. Highest priority, then highest elevation.</summary>
    // ADDED: the single overlap resolver. AreaAt and CanJumpOverPoint were each
    // running their own scan with their own tie-break, which is exactly why
    // overlapping areas disagreed. One scan, one winner, both questions asked of it.
    // Note it does NOT skip fall-through areas - deciding that is the caller's job,
    // and skipping here would let a hole be outvoted by the floor underneath it.
    public static GroundArea RulingAreaAt(Vector2 worldPosition)
    {
        GroundArea best = null;

        for (int i = 0; i < All.Count; i++)
        {
            GroundArea a = All[i];
            if (!a.Contains(worldPosition)) continue;

            // CHANGED (ramp pass): ElevationAt, not the field - a ramp's height at
            // this point is what should break the tie.
            if (best == null
                || a.priority > best.priority
                || (a.priority == best.priority
                    && a.ElevationAt(worldPosition) > best.ElevationAt(worldPosition)))
                best = a;
        }

        return best;
    }// end of function >:D

    /// <summary>Which ground is under this point? Null if none, or if the ruling area is a hole.</summary>
    // CHANGED: was its own scan over All. It asks the ruling area first now - if that
    // area is fall-through, the point is a hole regardless of what solid ground sits
    // beneath it. That is what makes a jumpable hole drawn on the floor behave as one.
    public static GroundArea AreaAt(Vector2 worldPosition)
    {
        GroundArea ruler = RulingAreaAt(worldPosition);
        if (ruler == null || ruler.canFallThrough) return null;

        return ruler;
    }// end of function >:D

    /// <summary>True if a jump can pass over this point. False = jumping is ignored here.</summary>
    // CHANGED: was "any non-jumpable area covering the point vetoes it", which meant a
    // plain floor underneath always won against a jumpable area drawn on top. Only the
    // ruling area is asked now - use priority to pick it.
    public static bool CanJumpOverPoint(Vector2 worldPosition)
    {
        GroundArea ruler = RulingAreaAt(worldPosition);
        return ruler == null || ruler.canJumpOver;
    }// end of function >:D
}

// ADDED: names for the four combinations. Lives in this file rather than its own
// because it has no meaning apart from GroundArea and is four lines long.
public enum SurfaceType
{
    Wall,       // solid, jumping ignored         - green
    JumpWall,   // solid on foot, clears in air   - yellow
    Pit,        // fall in, or jump across        - red
    JumpZone    // land on it, never fall through - cyan
}