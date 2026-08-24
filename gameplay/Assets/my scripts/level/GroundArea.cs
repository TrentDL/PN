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
//      "Ground" (the ordinary floor) is a Wall whose interior you stand on - solid
//      and unjumpable is exactly right for a plain floor.
//
// ADDED (overlap pass): lockMovementInside and priority.
// WHY: the two bools describe WHAT an area is. They cannot describe WHICH area
//      answers when several cover the same point - every overlapping area was
//      answering at once, so a jumpable hole drawn on solid floor could never win
//      the argument. 'priority' picks the one that speaks; the bools then say what
//      it says. 'lockMovementInside' is a separate axis again: it constrains X/Y,
//      the other two constrain height.
//
// ADDED (confinement pass): blockWalkOff.
// WHY: v7 replaced playercontrol1's hard boundary with the Battletoads drop, which
//      is right for ledges and wrong for a boxed-in room. This restores the old
//      behaviour per-area instead of globally.
//
//      THE THREE CONFINEMENT FLAGS, which are easy to confuse:
//        (none)              walk off = drop,  jump out = yes, fall off = yes
//        blockWalkOff        walk off = STOP,  jump out = yes
//        lockMovementInside  walk off = stop,  jump out = NO,  fall off = no
//
//      blockWalkOff is a fence you can hop. lockMovementInside is a sealed box.

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
    [Header("Height")]
    [Tooltip("How high the player's model sits while standing on this ground. 0 = base floor.")]
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
    // NOT the same as lockMovementInside: that one holds you in while AIRBORNE too.
    // This one only refuses the walk-off, so a jump still carries you clear.
    [Tooltip("ON: walking off this area's edge stops you instead of dropping you. Jumping still leaves.")]
    public bool blockWalkOff = false;

    // ADDED: jumping allowed, walking is not. Solves the "jump in place inside a
    // pen" case - the player can leave the ground but cannot leave the polygon.
    // Independent of the other two: it constrains X/Y, they constrain height.
    [Tooltip("ON: the player is clamped inside this area even while airborne. Jump straight up, but not out.")]
    public bool lockMovementInside = false;

    // ADDED: what a locked area does at its boundary.
    // 0 = clamp - the player stops exactly at the edge (the original behaviour).
    // Above 0 = push - the player is moved back inward at this speed, in world units
    // per second. Reads better than a hard stop for force fields, and avoids the
    // edge-jitter a clamp can produce when input keeps pressing into the boundary.
    [Tooltip("0 = hard clamp at the edge. Above 0 = push the player back inward at this speed.")]
    public float pushBackSpeed = 0f;

    [Header("Overlap")]
    // ADDED: who wins when areas overlap. The HIGHEST priority area covering the
    // point decides canFallThrough and canJumpOver outright, and every lower area
    // under it is silent. Equal priorities fall back to highest elevation.
    // NOTE: this governs SURFACE rules only. The confinement flags above are read
    // by containment, not by ruling - see the loop in PlayerControl3.ApplyMovement.
    [Tooltip("Higher wins when areas overlap. Leave 0 for ordinary ground.")]
    public int priority = 0;

    // ADDED: one word for the pair, for gizmo colour and for debugging.
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
    // NOTE: colour reads the two surface bools only, so a priority-1 area or a
    //       locked pen looks the same as ordinary ground of that type. Tinting by
    //       priority is the cheap fix if layered levels make that confusing.
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
    // overlapping areas disagreed. One scan, one winner, both questions asked of
    // it (DRY). Note it does NOT skip fall-through areas - deciding that is the
    // caller's job, and skipping here would let a hole be outvoted by the floor
    // underneath it.
    public static GroundArea RulingAreaAt(Vector2 worldPosition)
    {
        GroundArea best = null;

        for (int i = 0; i < All.Count; i++)
        {
            GroundArea a = All[i];
            if (!a.Contains(worldPosition)) continue;

            if (best == null
                || a.priority > best.priority
                || (a.priority == best.priority && a.elevation > best.elevation))
                best = a;
        }

        return best;
    }// end of function >:D

    /// <summary>Which ground is under this point? Null if none, or if the ruling area is a hole.</summary>
    // CHANGED: was its own scan over All. It asks the ruling area first now - if
    // that area is fall-through, the point is a hole regardless of what solid
    // ground sits beneath it. That is what makes a jumpable hole drawn on top of
    // the floor behave as a hole.
    public static GroundArea AreaAt(Vector2 worldPosition)
    {
        GroundArea ruler = RulingAreaAt(worldPosition);
        if (ruler == null || ruler.canFallThrough) return null;

        return ruler;
    }// end of function >:D

    /// <summary>True if a jump can pass over this point. False = jumping is ignored here.</summary>
    // CHANGED: was "any non-jumpable area covering the point vetoes it", which meant
    // a plain floor underneath always won the argument against a jumpable area drawn
    // on top. Only the ruling area is asked now - use priority to pick it.
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