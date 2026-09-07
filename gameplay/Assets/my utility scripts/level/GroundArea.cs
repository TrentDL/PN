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
// ADDED (Drop Target toggle): dropTarget.
// WHY: the fall-fix (rejecting a tile above the falling player) is correct by
//      default but level-specific, so it can be switched off per-area if it ever
//      conflicts with a level built before it existed.
//
// ADDED (tuning pass): fallFloorOverride, maxStepUpOverride.
// WHY: fallFloor and maxStepUp used to be single global values on PlayerControl3,
//      so every pit had the same bottom and every ledge had the same reach. These
//      are OPTIONAL per-area overrides, not a move - the player's fields remain the
//      default for any area that leaves these untouched. maxStepUpOverride is read
//      from the area the player is JUMPING FROM, since the destination area is not
//      known until the jump lands; reachability is a property of where you left.
//
// CHANGED (ownership pass): fallFloorOverride -> fallFloor, maxStepUpOverride ->
//      maxStepUp, and ResolveFallFloor/ResolveMaxStepUp -> the static FallFloorFor/
//      MaxStepUpFor. The values now live HERE outright rather than deferring to
//      PlayerControl3, so both sentinels (-9999f, -1f) are gone.
// WHY: a sentinel is only readable next to the field it defers to - "-9999" means
//      nothing on its own. A plain value with a sensible initializer reads by
//      itself, and the "no current area" fallback that PlayerControl3 wrote out as
//      a ternary at each call site is folded into the static helpers (DRY).
// NOTE THE TRADE: the single global tuning knob is gone with it. Retuning every
//      area now means touching every area. The tuning-pass design above was the
//      better fit while most areas were unconfigured; this one is better once the
//      ground itself is the thing you tune.

// ADDED (wall pass): blockEntry, heightGateThreshold.
// WHY: the goal was a block that is SOLID on foot and passable over the top - the
//      polygon equivalent of an EdgeZone with a height gate.
//
// REJECTED ON THE WAY THERE - a crossing gate (crossingGateThreshold +
//      CrossingBlocker, built on a segment-vs-boundary test ported into
//      PolygonArea). It could not work, and the reason is worth keeping:
//      CrossedBy only fires on the FRAME THE BOUNDARY IS TOUCHED. It has no opinion
//      about a player already standing inside, so walking around within the polygon
//      was never blocked. A LINE has no interior, which is why the same test is
//      correct on EdgeZone and wrong here.
//
// WHAT WORKS INSTEAD: Contains(), the test this class already had. Entry is
//      "outside last frame, inside this frame", which is a pair of Contains calls
//      and needs no new geometry at all. See the confinement loop in
//      PlayerControl3.ApplyMovement.
//
// NOTE: blockEntry is the INVERSE of lockMovementInside, not a variant of it. That
//      flag keeps a player who is INSIDE from leaving; this keeps a player who is
//      OUTSIDE from entering. The two are mutually exclusive per frame - the loop
//      guards on Contains(current) - so they cannot share one bool.

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

    // ADDED: perspective scale is driven by world Y (depth), but an elevated platform
    // visually lifts the player without moving them in depth - so the scale can look
    // wrong while standing on one. This is a stopgap: it freezes scale at whatever it
    // was on entry, it does not compute a "correct" one.
    [Tooltip("ON: perspective scale stops updating while standing on this area. Temporary fix for depth perception on elevated platforms.")]
    public bool freezeScaleWhileOn = false;

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
    [Tooltip("ON: a fall from this area stops at this area's height instead of continuing past it. The fall still animates.")]
    public bool stopAtGroundPoint = false;

    // ADDED: jumping allowed, walking is not. The player can leave the ground but not
    // the polygon. Constrains X/Y; the surface bools constrain height.
    [Tooltip("ON: the player is clamped inside this area even while airborne. Jump straight up, but not out.")]
    public bool lockMovementInside = false;

    // ADDED (wall pass): the inverse of lockMovementInside above - that one keeps you
    // IN, this one keeps you OUT. Turns the polygon into a solid obstacle on foot.
    // Pair it with heightGateThreshold to make it passable over the top; leave that
    // at 0 and this is an unconditional wall.
    // NOTE: this is about the FOOTPRINT, not the surface. canFallThrough/canJumpOver
    // still describe what the ground here IS - blockEntry describes whether you may
    // walk onto it at all.
    [Tooltip("ON: the player cannot WALK into this area. Set Height Gate Threshold to let them jump over it.")]
    public bool blockEntry = false;

    // ADDED (wall pass): ported from EdgeZone.heightGateThreshold, and gating the
    // same kind of thing - a permission that only opens once the player is high
    // enough. Here it guards blockEntry above.
    //
    // The height compared against is the player's TOTAL height, ground elevation
    // plus the jump arc. PlayerControl3 supplies that sum; see the note at the
    // confinement loop for why the player has to compute it.
    //
    // 0 = no gate, so blockEntry alone is a wall of infinite height. Set this to the
    // block's visual height to get "solid on foot, clears in a jump".
    [Tooltip("0 = the wall is absolute. Above 0: the player may enter once their total height (ground + jump arc) reaches this.")]
    public float heightGateThreshold = 0f;

    // ADDED: what a locked area does at its boundary.
    // 0 = clamp, the player stops exactly at the edge (original behaviour).
    // Above 0 = push back inward at this speed, in world units per second. Reads
    // better for a force field, and avoids the edge-jitter a clamp can produce.
    [Tooltip("0 = hard clamp at the edge. Above 0 = push the player back inward at this speed.")]
    public float pushBackSpeed = 0f;

    [Header("Fall Tuning")]
    // ADDED: per-area override of PlayerControl3.fallFloor. Negative sentinel means
    // "use the player's global value" - most areas never touch this, so a level does
    // not need to configure it everywhere just to give one pit a shallower bottom.
    //
    // CHANGED (ownership pass): RENAMED fallFloorOverride -> fallFloor. It is no
    // longer an override of anything - PlayerControl3 has no fallFloor to override.
    // The -9999f sentinel is replaced by the DefaultFallFloor initializer, so an
    // unconfigured area reads as the same number it used to borrow.
    [Tooltip("Elevation at which a fall starting on this area gives up and returns the player to safe ground.")]
    public float fallFloor = DefaultFallFloor;

    // ADDED: per-area override of PlayerControl3.maxStepUp. Read from the area the
    // player is JUMPING FROM - the destination area is not known until the jump
    // lands, so reachability is decided by where the player left, not where they
    // are headed.
    //
    // CHANGED (ownership pass): RENAMED maxStepUpOverride -> maxStepUp, same reason.
    // The "read from the area you are JUMPING FROM" rule above is unchanged - it is
    // still the caller in PlayerControl3 that decides which area is asked.
    [Tooltip("Reach of a single jump's climb, for jumps starting on this area, in elevation units.")]
    public float maxStepUp = DefaultMaxStepUp;

    // ADDED (ownership pass): the values used when the player is inside NO area -
    // startup before the first SetArea, and any fall that begins off-area. const
    // rather than a serialized field because there is no object to hang it on in
    // that case; one constant each, so the two call sites in PlayerControl3 cannot
    // drift apart (DRY).
    // Microsoft C# - const: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/const
    public const float DefaultFallFloor = -5f;
    public const float DefaultMaxStepUp = 1.5f;

    /// <summary>fallFloor to use for a fall starting on this area. Null-tolerant - a null area gets the default.</summary>
    // CHANGED (ownership pass): was the instance method ResolveFallFloor(float).
    // WHY static: the null-area case was a ternary written out at the call site in
    // PlayerControl3, and an instance method cannot answer for a null instance. The
    // area is the argument now, so the player asks one question and never repeats
    // the fallback. WHY no parameter: there is no global default left to pass in.
    // Microsoft C# - static members: https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-classes-and-static-class-members
    public static float FallFloorFor(GroundArea area)
    {
        return (area != null) ? area.fallFloor : DefaultFallFloor;
    }// end of function >:D

    /// <summary>maxStepUp to use for a jump starting on this area. Null-tolerant - a null area gets the default.</summary>
    // CHANGED (ownership pass): was the instance method ResolveMaxStepUp(float).
    public static float MaxStepUpFor(GroundArea area)
    {
        return (area != null) ? area.maxStepUp : DefaultMaxStepUp;
    }// end of function >:D

    [Header("Overlap")]
    // ADDED: who wins when areas overlap. The HIGHEST priority area covering the
    // point decides the surface rules outright; equal priorities fall back to
    // highest elevation.
    // NOTE: governs SURFACE rules only. The confinement flags are read by
    // containment, not by ruling - see the loop in PlayerControl3.ApplyMovement.
    [Tooltip("Higher wins when areas overlap. Leave 0 for ordinary ground.")]
    public int priority = 0;

    [Header("Fall Landing")]
    // ADDED (Drop Target toggle): whether the "tile above the falling player" fix
    // applies to falls that start on this area. Default true because the fix
    // corrects a real bug (a raised platform could hide the floor beneath it); the
    // flag exists as an escape hatch, not as something normally turned off.
    [Tooltip("ON (default): a fall rejects tiles above the player and drops to whatever is genuinely below. OFF: restores the old JumpTarget-only landing, which can hide a floor under a raised platform.")]
    public bool dropTarget = true;

    [Header("Debug Visualization")]
    // ADDED (walkable preview): draws this area's polygon lifted to its own
    // elevation, so the Scene view shows where the player will actually STAND
    // rather than only where the footprint sits on the floor.
    //
    // WHY THIS IS NEEDED: a GroundArea polygon is a FOOTPRINT - see TileStep's
    // DEPTH note. 'elevation' lifts the visual only, so the collider gizmo stays
    // planted at the base while the player is drawn 'elevation' units higher.
    // The two never coincide, which is the visual disconnect.
    [Tooltip("Draw a second outline at this area's elevation - the surface the player will walk on.")]
    public bool showWalkablePreview = true;

        // ADDED (walkable preview): world units per elevation unit. This is the player's
    // parent scale - PlayerControl3 applies elevation to visualRoot.localPosition,
    // which the parent transform then scales. There is no way to read it from here
    // (the player is not this area's business), so it is authored.
    //
    // CAVEAT: AdjustPlayerScale changes that scale with depth, so this is only exact
    // at one depth. If the preview drifts as you move the player in Y, that is the
    // same coupling described below - not a bug in the gizmo.
    [Tooltip("World units per elevation unit. Match the parentScaleY your player logs.")]
    public float elevationToWorld = 5.349f;

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


    void OnDrawGizmos()
    {
        if (!showWalkablePreview || Area == null || elevation == 0f) return;

        // The player's height is applied in LOCAL units of visualRoot's parent and
        // then scaled by that parent, so one elevation unit is worth
        // 'elevationToWorld' world units. Set this to the parentScaleY your
        // LogVisualHeight prints (5.349 in the current scene).
        float lift = elevation * elevationToWorld;

        Gizmos.color = new Color(1f, 0.3f, 0.4f, 0.9f);   // the pink layer in the mockup

        for (int p = 0; p < Area.pathCount; p++)
        {
            Vector2[] path = Area.GetPath(p);

            for (int i = 0; i < path.Length; i++)
            {
                Vector3 a = transform.TransformPoint(path[i] + Area.offset);
                Vector3 b = transform.TransformPoint(path[(i + 1) % path.Length] + Area.offset);

                a.y += lift;
                b.y += lift;

                Gizmos.DrawLine(a, b);

                // A vertical tick from footprint to surface - shows the lift itself,
                // which is what you are tuning.
                Gizmos.DrawLine(new Vector3(a.x, a.y - lift, a.z), a);
            }
        }
    }// end of function >:D


    // ADDED (walkable preview): read the factor from the live player instead of
    // authoring it. WHY: AdjustPlayerScale changes the player's parent scale with
    // depth, so a hardcoded elevationToWorld is only right at one depth and the
    // preview would lie everywhere else. Reading it live means the outline tracks
    // whatever the player's factor currently is.
    //
    // NOTE this is an EDITOR convenience only - it deliberately reaches for the
    // player, which no runtime GroundArea code should ever do.
    private float ElevationToWorld()
    {
        PlayerControl3 player = Object.FindFirstObjectByType<PlayerControl3>();
        if (player == null || player.visualRoot == null || player.visualRoot.parent == null)
            return 1f;

        return player.visualRoot.parent.lossyScale.y;
    }// end of function >:D


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