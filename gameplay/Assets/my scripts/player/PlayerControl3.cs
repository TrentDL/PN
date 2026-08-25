// PlayerControl3.cs
//
// RENAMED: PlayerControlShadowed -> PlayerControl3  (file must match the class)
//
// CHANGED (surface types):
//   * ADDED: a jump veto at the top of JumpCoroutine - Walls and JumpZones swallow
//     the jump. Tested ONCE at takeoff: a jump you were allowed to start is a jump
//     you finish.
//   * CHANGED: the walk-off branch in ApplyMovement falls into a pit instead of
//     clamping at its lip.
//   * CHANGED: the fallFloor branch in FallCoroutine returns the player to the last
//     solid ground instead of SetArea(null), which was an unrecoverable state.
//
// CHANGED (overlap pass):
//   * ADDED: the confinement loop at the end of ApplyMovement. It sits OUTSIDE the
//     !isJumping && !isFalling guard on purpose - the point of the lock is that it
//     survives the jump.
//   * CHANGED: that loop iterates GroundArea.All rather than asking RulingAreaAt.
//     WHY: ruling picks ONE area for surface rules, so a pen sharing space with
//     ordinary floor lost the tie and the clamp silently never ran. Containment is
//     not exclusive - every locked area you are inside gets to act.
//
// CHANGED (stop pass):
//   * REMOVED: the disableFalling early-out at JumpCoroutine's apex. It skipped the
//     descent entirely, so a missed jump snapped back with no motion - it read as a
//     glitch rather than a stumble. The apex hands off unconditionally again.
//   * ADDED: the stopAtGroundPoint floor inside FallCoroutine. Same fall, same arc,
//     same shadow shrink - it just cannot pass below the height it started from.
//     Changing the OUTCOME rather than skipping the animation is the whole point.
//
// CHANGED (ramp pass):
//   * CHANGED: every read of area.elevation is now area.ElevationAt(position).
//   * ADDED: the per-frame resample at the end of ApplyMovement. WHY: SetArea only
//     fires when the AREA changes, so a ramp sampled on entry stayed frozen while
//     you walked across it. Walking within one area changes position, not area.
//
// EARLIER CHANGES - all of it about the shadow:
//   * ADDED: shadowStartLocalPos - the shadow's authored local position, so the
//     elevation offset is added to it instead of overwriting it.
//   * CHANGED: the shadow is lifted by `elevation`, so it rides up onto a ledge.
//   * ADDED: the shadow is deactivated over a gap. shadowHideDelay keeps it on
//     across narrow gaps so it does not flicker.
//   * The shadow is NOT lifted by jumpYOffset - it stays planted while the model
//     arcs above it. (To glue it to the feet, see the note in ApplyHeights.)
//   * RENAMED: ApplyVisualHeight() -> ApplyHeights(). It owns the shadow's position
//     and scale too, so there is one place the height values reach the visuals.

using System.Collections;
using UnityEngine;


public class PlayerControl3 : MonoBehaviour
{
    static float moveSpeed = 5f, moveAccuracy = 0.15f;

    #region movement fields/properties

    [Header("References")]
    [Tooltip("Live2D model root. Gets lifted during jumps and by ground elevation.")]
    public Transform visualRoot;

    [Header("Facing")]
    [Tooltip("Animator holding the flip state.")]
    public Animator animator;
    [Tooltip("Name of the state playing the flip clip.")]
    public string flipStateName = "flip";
    [Tooltip("Frame of the flip clip that shows the left-facing pose.")]
    public int leftFrame = 0;
    [Tooltip("Frame of the flip clip that shows the right-facing pose.")]
    public int rightFrame = 13;
    [Tooltip("Total length of the flip clip in frames. Used to convert a frame to normalized time.")]
    public int clipLengthFrames = 13;
    [Tooltip("Seconds for a full turn from one side to the other.")]
    public float flipDuration = 0.15f;

    private int flipStateHash;               // cached - Play(hash) avoids a string lookup per seek
    private float flipT = 0f;                // where the turn actually is:  0 = left, 1 = right
    private float flipTarget = 0f;           // where the input wants it to be

    private Rigidbody2D rb;              // MAY BE NULL - always guard
    private Vector2 moveInput;
    private Vector2 moveVelocity;

    public float PlayerScale;
    public float PlayerRatio;

    [Header("Movement")]
    public float moveXspeed = 5f;
    public float moveYspeed = 2.5f;

    [Tooltip("Scale movement speed with the perspective scale so motion looks consistent at any depth.")]
    public bool scaleSpeedWithSize = true;
    [Tooltip("Scale value at which speeds are exactly moveXspeed/moveYspeed. Set to your character's scale at the 'reference' depth.")]
    public float referenceScale = 1f;

    [Header("Sprint Settings")]
    public string sprintKeyLeft = "a";
    public string sprintKeyRight = "d";
    public float sprintMultiplier = 2f;
    public float doubleTapWindow = 0.2f;

    private float lastTapTimeLeft = -1f;
    private float lastTapTimeRight = -1f;
    private bool isSprintingLeft = false;
    private bool isSprintingRight = false;

    [Header("Jump Settings")]
    [Tooltip("Maximum height the visual rises, in local units of visualRoot's parent. " +
             "MUST exceed your tallest tile's elevation or the model never crests it.")]
    public float jumpHeight = 2f;
    // CHANGED (v13): this is the RISE time only. The descent is FallCoroutine's, and
    // its length comes from the distance it actually has to cover.
    [Tooltip("How long the ASCENT takes. The fall is separate and self-timing.")]
    public float jumpDuration = 0.5f;

    // ADDED (variable jump): the tap version. A jump released early stops rising here
    // instead of at jumpHeight, so a tap hops and a hold clears a platform.
    [Tooltip("Height a tapped jump reaches. Must be below jumpHeight or holding does nothing.")]
    public float tapJumpHeight = 0.8f;

    // ADDED: how long the key must be held to keep rising past tapJumpHeight. Below
    // this, the release is treated as a tap even if the arc has barely started.
    [Tooltip("Seconds the jump key must be held before the full-height jump commits.")]
    public float tapWindow = 0.12f;
    
    [Tooltip("Must be a CHILD of this object so it follows the player horizontally.")]
    public GameObject playerShadow;

    // CHANGED (v7): was CurrentHeight, used to test the jump arc against a surface.
    // Tile stepping decides by crossing test instead, so the arc's height is cosmetic
    // and nothing reads it. This is the reach limit.
    [Tooltip("How many elevation units a single jump can climb. A tile higher than " +
             "this above you cannot be reached, however you time the jump.")]
    public float maxStepUp = 1.5f;

    // CHANGED (v13): was fallSpeed, a flat rate. An acceleration reads as gravity and
    // makes a long drop visibly faster than a short one without any extra tuning.
    [Tooltip("Elevation units per second squared. Higher = heavier.")]
    public float fallAcceleration = 20f;

    // ADDED (v13): how far below zero the player can fall before the fall gives up.
    // Set just below your LOWEST tile. A large negative value means a missed jump
    // drops a long way before recovering, which reads as falling through the floor.
    [Tooltip("Elevation at which a fall ends with nothing underneath. Set just below your lowest tile.")]
    public float fallFloor = -5f;

    // ADDED: how long the player must be over open space before the shadow turns off.
    // Crossing a hairline seam between two grounds never reaches this, so no flicker.
    [Tooltip("Seconds over open space before the shadow is deactivated. 0 = hide the instant there is no ground below.")]
    public float shadowHideDelay = 0.08f;

    private float gapTime = 0f;   // ADDED: how long we have had nothing beneath us

    // ADDED (v13 fix): the tile the shadow is drawn on. Null over a gap. Needed
    // because `elevation` is the PLAYER's height, and during a fall those differ -
    // that difference is exactly what the shadow's scale should show.
    private GroundArea shadowGround;

    // ADDED: test toggle for jump momentum. ON = speed carries through the jump when
    // you let go of the keys. OFF = releasing stops the player dead in the air.
    [Tooltip("Keep moving at takeoff speed if the movement keys are released mid-jump. Off = hard stop in the air.")]
    public bool JumpCarryOver = true;

    // ADDED: the velocity the jump started with, refreshed every grounded frame.
    private Vector2 jumpMomentum;

    private float jumpYOffset = 0f;

    private bool isJumping = false;

    // ADDED (v8): true while a descent is playing. Separate from isJumping because
    // the two can never overlap and each guards different things.
    private bool isFalling = false;

    private Vector3 shadowStartScale;
    private Vector3 shadowStartLocalPos;   // ADDED: authored shadow position, elevation is added to it
    private Vector3 visualStartLocalPos;

    private float currentScale = 1f;

    [Header("Boundary Settings")]
    public bool enableBoundaryChecking = true;

    // The ground the player is currently standing on. Movement is clamped to this
    // one area, which is what makes the player stop at its edge.
    private GroundArea currentArea;

    // Visual lift from the current area's height, kept separate from jumpYOffset so
    // the jump arc can be added on top without either overwriting the other.
    private float elevation = 0f;

    // ADDED (surface types): where to put the player back after falling out of a pit.
    // Written on every area change, so it is the point you ENTERED the current ground
    // at - deliberately not the lip you fell from, which would drop you back in.
    private Vector2 lastSafePosition;
    private GroundArea lastSafeArea;

    #endregion


    void Start()
    {
        rb = GetComponent<Rigidbody2D>();   // fine if null

        if (visualRoot == null)
        {
            Debug.LogError("PlayerControl3: visualRoot is not assigned. " +
                           "Drag the Live2D model child onto it - jumping does nothing without it.", this);
        }
        else
        {
            visualStartLocalPos = visualRoot.localPosition;
            visualRoot.localRotation = Quaternion.identity;   // undo any flip left over from the old rotation method
        }

        if (animator == null)
        {
            Debug.LogError("PlayerControl3: animator is not assigned. Facing will not change.", this);
        }
        else
        {
            flipStateHash = Animator.StringToHash(flipStateName);
            animator.speed = 0f;   // the clip never plays itself - this script owns its time
            ApplyFacing();         // land on the left pose before the first input
        }

        // CHANGED: cache position as well as scale - ApplyHeights now moves the shadow.
        if (playerShadow != null)
        {
            shadowStartScale = playerShadow.transform.localScale;
            shadowStartLocalPos = playerShadow.transform.localPosition;
        }

        // Find the area the player was placed on in the editor and adopt its height.
        currentArea = GroundArea.AreaAt(transform.position);
        if (enableBoundaryChecking && currentArea == null)
        {
            Debug.LogWarning("PlayerControl3: player did not start inside any GroundArea. " +
                             "Movement will be unbounded until it lands on one.", this);
        }
        SetArea(currentArea);   // MOVED below the shadow cache - it writes to the shadow now
    }//end of function >:D


    void Update()
    {
        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        Vector2 dir = moveInput.normalized;
        moveVelocity = new Vector2(dir.x * moveXspeed, dir.y * moveYspeed);

        DetectDoubleTap();
        ApplySprintSpeed();
        UpdateFacing();
        AdjustPlayerScale();   // must run before ApplyScaleToSpeed
        ApplyScaleToSpeed();
        JumpPhysics();         // ADDED: last, so it sees the finished moveVelocity

        // CHANGED (v8): added !isFalling. Remove that clause if you WANT a mid-air
        // recovery jump while dropping off a ledge.
        if (Input.GetKeyDown(KeyCode.Space) && !isJumping && !isFalling)
        {
            StartCoroutine(JumpCoroutine());
        }

        // No Rigidbody2D? Then movement happens here instead of FixedUpdate.
        if (rb == null) ApplyMovement(Time.deltaTime);
    }


    void FixedUpdate()
    {
        if (rb != null) ApplyMovement(Time.fixedDeltaTime);
    }

    //reference scale 
    private void ApplyScaleToSpeed()
    {
        if (!scaleSpeedWithSize || referenceScale <= 0f) return;
        moveVelocity *= currentScale / referenceScale;
    } //end of function >:d


    // ADDED: owns the jump momentum rule and nothing else.
    // Must run AFTER ApplySprintSpeed and ApplyScaleToSpeed so the stored velocity is
    // the final one, sprint and perspective scale included.
    // CHANGED (v13 fix): the test was "if (!isJumping)". Since the jump was split,
    // isJumping goes false at the apex and isFalling takes over - so from the apex
    // down this thought the player was grounded, overwrote jumpMomentum with the
    // released (zero) velocity, and the carry-over died on the descent.
    private void JumpPhysics()
    {
        bool airborne = isJumping || isFalling;

        if (!airborne)
        {
            jumpMomentum = moveVelocity;
            return;
        }

        if (!JumpCarryOver) return;   // toggle off - falls through to the hard stop

        if (moveInput == Vector2.zero) moveVelocity = jumpMomentum;
    }


    // Single movement path, used by either Update or FixedUpdate.
    private void ApplyMovement(float deltaTime)
    {
        Vector2 current = (rb != null) ? rb.position : (Vector2)transform.position;
        Vector2 desired = current + moveVelocity * deltaTime;

        // CHANGED (v7): was clamped to currentArea, which stopped the player dead at a
        // ledge edge. Battletoads drops you instead, so we only clamp when there is
        // nothing below to fall onto.
        // CHANGED (v8): added !isFalling. Without it the drop check re-fires every
        // frame of the fall and restarts the coroutine, freezing the player mid-air.
        if (enableBoundaryChecking && !isJumping && !isFalling)
        {
            GroundArea ahead = TileStep.JumpTarget(desired, elevation, 0f);      
            if (ahead == null)
            {
                // ADDED (blockWalkOff): asked first, because it overrules both outcomes
                // below - no drop, no pit, just stop. Reads currentArea rather than the
                // destination: the rule belongs to the ground you are standing on, and
                // the destination has no area by definition here.
                if (currentArea != null && currentArea.blockWalkOff)
                {
                    desired = currentArea.ClampInside(desired);
                }
                else
                {
                    // Stepped off the tile. Anything below catches us; otherwise this is
                    // a real gap and we stop at the edge.
                    GroundArea drop = TileStep.DropTarget(desired, elevation);

                    // CHANGED (surface types): was "if (drop != null)", which clamped at
                    // the lip of a Pit with nothing under it. TileStep skips fall-through
                    // surfaces now, so a Pit reads as empty space - and empty space INSIDE
                    // the current area is a hole, while empty space outside it is the
                    // world's edge. That distinction keeps the outer boundary clamping.
                    bool insidePit = currentArea != null && currentArea.Contains(desired);

                    // ADDED (allowWalkOffIntoGap): the third way to leave. Without it,
                    // "no tile below" always clamped, so an open ledge was impossible to
                    // author - you needed a pit polygon under every drop.
                    bool openLedge = currentArea != null && currentArea.allowWalkOffIntoGap;

                    Debug.Log($"NULL AHEAD drop={drop?.name} insidePit={insidePit} openLedge={openLedge}");
                    if (drop != null || insidePit || openLedge) StartCoroutine(FallCoroutine(elevation));
                    else if (currentArea != null) desired = currentArea.ClampInside(desired);
                }
            }
            else if (ahead != currentArea)
            {
                // CHANGED (v10): was an unconditional SetArea, which snapped the player
                // down whenever JumpTarget returned a LOWER tile. JumpTarget accepts
                // anything at or below your height, so stepping off a ledge onto the
                // overlapping floor took this branch instead of the fall branch.
                // CHANGED (ramp pass): sampled at the destination, so walking up a slope
                // reads as a step-up rather than a drop.
                float aheadHeight = ahead.ElevationAt(desired);

                Debug.Log($"STEP ahead={ahead.name}@{aheadHeight} current={currentArea?.name}@{elevation}");

                // CHANGED (blockWalkOff): a lower tile ahead is still a walk-off, so the
                // same rule applies here. Without this, an area with blockWalkOff on
                // would hold at its outer edge but still drop you onto anything
                // overlapping it.
                if (aheadHeight < elevation && currentArea != null && currentArea.blockWalkOff)
                    desired = currentArea.ClampInside(desired);
                else if (aheadHeight < elevation)
                    StartCoroutine(FallCoroutine(elevation));   // it is a drop
                else
                    SetArea(ahead);                             // same height - snapping is correct
            }

            // Extra zones - props, obstacles. Delete these two lines if your
            // project has no BoundsZone script.
            if (!BoundsZone.IsWithinBounds(desired, "player"))
                desired = BoundsZone.ClampToBounds(desired, "player");
        }

        // ADDED: edge crossing. Sits outside the boundary block for the same reason the
        // movement lock does - an edge has to hold while airborne, or canJumpOver has
        // nothing to grant an exception to.
        // DELETE these two lines if your project has no EdgeZone script.
        EdgeZone blocker = EdgeZone.Blocker(current, desired, isJumping || isFalling, isFalling);
        if (blocker != null) desired = blocker.StopShortOf(current, desired);

        // ADDED (overlap pass): the movement lock. Deliberately OUTSIDE the block above,
        // so it applies while airborne too - that is the whole feature.
        // CHANGED: was RulingAreaAt(current), which asked the overlap resolver. That was
        // wrong in kind, not just in tuning - ruling picks ONE area for surface rules, so
        // a pen sharing space with ordinary floor lost the tie and the clamp never ran.
        // Containment is not exclusive: every locked area you are inside gets to act.
        // Tests 'current', not 'desired': the question is "am I in a locked pen right
        // now". Testing the destination would let a fast frame carry you out of one.
        for (int i = 0; i < GroundArea.All.Count; i++)
        {
            GroundArea a = GroundArea.All[i];
            if (!a.lockMovementInside || !a.Contains(current)) continue;

            if (a.pushBackSpeed <= 0f)
            {
                desired = a.ClampInside(desired);   // original clamp
                continue;
            }

            // Only push once the move would actually leave. Pushing while safely inside
            // would fight normal movement everywhere in the pen, not just at its wall.
            if (a.Contains(desired)) continue;

            // Direction comes from ClampInside, not from the area's centre - a
            // centre-based push sends the player across the middle of an L-shaped or
            // crescent pen instead of away from the wall they hit (DRY: one definition
            // of "inward", and it already handles concave shapes).
            Vector2 inward = (a.ClampInside(desired) - desired).normalized;
            desired = current + inward * a.pushBackSpeed * deltaTime;
        }

        // ADDED (ramp pass): resample the standing height every frame.
        // WHY: SetArea only fires when the AREA changes, so a ramp sampled on entry
        // stayed frozen while you walked across it - the elevation never moved. The
        // area is unchanged here; only the position within it is.
        // Grounded only: a fall owns `elevation` while it runs.
        if (!isJumping && !isFalling && currentArea != null)
        {
            elevation = currentArea.ElevationAt(desired);
            ApplyHeights();
        }

        if (rb != null)
            rb.MovePosition(desired);
        else
            transform.position = new Vector3(desired.x, desired.y, transform.position.z);
    }


    // RENAMED from ApplyVisualHeight - it drives the shadow as well now.
    // The one and only place jumpYOffset / elevation reach the visuals (DRY).
    // Model  = jump arc + ground height.
    // Shadow = ground height only, so it stays on the surface while the model arcs
    //          above it. To glue the shadow to the feet instead, add jumpYOffset to
    //          the shadow line below.
    private void ApplyHeights()
    {
        if (visualRoot != null)
        {
            Vector3 lp = visualStartLocalPos;
            lp.y = visualStartLocalPos.y + jumpYOffset + elevation;
            visualRoot.localPosition = lp;
        }

        if (playerShadow == null) return;

        // CHANGED (v13 fix): the shadow sits on the ground BENEATH the player, not at
        // the player's own elevation. During the ascent those are the same thing, but
        // the fall lowers `elevation` toward the tile it is heading for, which used to
        // drag the shadow down through the air with the model.
        // CHANGED (ramp pass): ElevationAt so the shadow follows a slope too.
        float groundHeight = (shadowGround != null)
            ? shadowGround.ElevationAt(transform.position)
            : elevation;

        Vector3 sp = shadowStartLocalPos;
        sp.y = shadowStartLocalPos.y + groundHeight;
        playerShadow.transform.localPosition = sp;

        // CHANGED (v13 fix): was jumpYOffset alone, which JumpCoroutine zeroes at the
        // apex - so the scale snapped back to 1x and sat there for the whole descent.
        // The gap between the feet and the ground below is the same number during the
        // rise and the fall, so one expression covers both halves.
        float heightAboveGround = jumpYOffset + (elevation - groundHeight);
        float shadowScale = (jumpHeight > 0f) ? 1f - (heightAboveGround / jumpHeight) * 0.1f : 1f;
        playerShadow.transform.localScale = shadowStartScale * shadowScale;
    }


    // ADDED: on/off switch for the shadow object. Kept separate from ApplyHeights
    // because that one runs every airborne frame and only deals with transforms -
    // this one changes the object's active state, which is not free.
    // The activeSelf check means SetActive only fires on an actual change.
    private void SetShadowVisible(bool visible)
    {
        if (playerShadow == null || playerShadow.activeSelf == visible) return;
        playerShadow.SetActive(visible);
    }


    // One place that adopts an area and its height (DRY - used by Start, by landing,
    // by the stop-at-ground-point return and by the pit return).
    private void SetArea(GroundArea area)
    {
        currentArea = area;

        // CHANGED (v7): was GroundHeightMap.HeightAt. Tiles are discrete heights, so
        // the area's own elevation is the whole answer - no texture to sample.
        // CHANGED (ramp pass): sampled at the player's position so a ramp adopts the
        // right height for where they actually landed.
        elevation = (area != null) ? area.ElevationAt(transform.position) : 0f;

        // ADDED (surface types): remember where solid ground was, for the pit return.
        if (area != null)
        {
            lastSafeArea = area;
            lastSafePosition = transform.position;
        }

        ApplyHeights();   // CHANGED: was ApplyVisualHeight

        // ADDED: standing on ground always means a visible shadow, so landing (and the
        // pit return) restores it here.
        gapTime = 0f;
        shadowGround = area;   // ADDED (v13 fix): landed - the shadow's ground is this tile
        SetShadowVisible(true);
    }


    #region Old facing implementations (kept for reference)
    // 1. ROTATION FLIP - rotated visualRoot 180 on Y. Required
    //    CubismRenderController -> Sorting -> Mode = BackToFrontOrder.
    // 2. ANIMATOR STATE TRANSITION - a bool plus a transition between two idle
    //    states. The cross-fade blended unrelated parameters too.
    // 3. TWO CUBISM OPACITY PARAMETERS - one per side, held 0.1 apart. Any
    //    interpolation between them hid both sides at once.
    // 4. ART MESH SETACTIVE - toggled the drawable GameObjects. Fought the
    //    Animator, which owns those same part opacities via the clips.
    // 5. BLEND TREE - an "angle" float picking left_idle / right_idle.
    // 6. SINGLE CUBISM PARAMETER - wrote Left_right_Opacity to -10 / +10 directly.
    // 7. FRAME SNAP - same clip as now, but Play() jumped straight to frame 0 or
    //    13 with no in-between. Replaced to get an actual turn animation.
    #endregion


    private void UpdateFacing()
    {
        if (moveInput.x != 0)                       // no horizontal input - keep last target
            flipTarget = moveInput.x > 0 ? 1f : 0f;

        if (flipT == flipTarget) return;            // turn finished - stop seeking

        flipT = (flipDuration > 0f)
            ? Mathf.MoveTowards(flipT, flipTarget, Time.deltaTime / flipDuration)
            : flipTarget;                           // guard: zero duration means snap

        ApplyFacing();
    }


    // Seeks to the frame flipT lands on. Single place the clip is seeked (DRY).
    private void ApplyFacing()
    {
        if (animator == null || clipLengthFrames <= 0) return;

        float frame = Mathf.Lerp(leftFrame, rightFrame, flipT);
        animator.Play(flipStateHash, 0, frame / clipLengthFrames);

        // Evaluate immediately. Without this the seek is not sampled until the
        // Animator's own update, leaving the turn one frame behind.
        animator.Update(0f);
    }


    private void DetectDoubleTap()
    {
        if (Input.GetKeyDown(sprintKeyLeft))
        {
            if (Time.time - lastTapTimeLeft < doubleTapWindow) isSprintingLeft = true;
            lastTapTimeLeft = Time.time;
        }

        if (Input.GetKeyDown(sprintKeyRight))
        {
            if (Time.time - lastTapTimeRight < doubleTapWindow) isSprintingRight = true;
            lastTapTimeRight = Time.time;
        }

        if (Input.GetKeyUp(sprintKeyLeft)) isSprintingLeft = false;
        if (Input.GetKeyUp(sprintKeyRight)) isSprintingRight = false;
    }


    private void ApplySprintSpeed()
    {
        if ((isSprintingLeft && moveInput.x < 0) || (isSprintingRight && moveInput.x > 0))
        {
            moveVelocity *= sprintMultiplier;
        }
    }

    #region Jump acsent
    // Visual only. The root keeps moving normally underneath, so you can steer mid-air.
    // CHANGED (v13): this is the ASCENT ONLY. The descent belongs to FallCoroutine, so
    // there is exactly ONE piece of code that brings the player down - jump, walk-off
    // and pit all share it (DRY). jumpDuration is the rise time alone, and no longer
    // has to be tuned against jumpHeight, because the fall's length comes from the
    // distance it actually has to cover.
    private IEnumerator JumpCoroutine()
    {
        if (visualRoot == null) yield break;

        // ADDED (surface types): Walls and JumpZones swallow the jump. Tested once, at
        // takeoff, rather than every frame - a jump you were allowed to start is a jump
        // you finish, which keeps the arc from stalling in mid-air.
        // CHANGED (overlap pass): CanJumpOverPoint asks only the RULING area now, so a
        // jumpable area drawn on top of solid floor wins if its priority is higher.
        if (!GroundArea.CanJumpOverPoint(transform.position)) yield break;

        isJumping = true;
        float startElevation = elevation;
        float elapsedTime = 0f;

        // ADDED (variable jump): the perspective multiplier, computed ONCE.
        // WHY: the previous version scaled jumpYOffset but compared it against an
        // unscaled height, so the release test fired on frame one at any scale above 1.
        float scale = scaleSpeedWithSize ? currentScale / referenceScale : 1f;

        // ADDED (variable jump): the floor a tap is guaranteed to reach, in the same
        // scaled units as jumpYOffset so the two are comparable.
        float minRise = tapJumpHeight * scale;

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / jumpDuration;

            // CHANGED (variable jump): amplitude is jumpHeight again, not targetHeight.
            // Lowering the amplitude mid-arc made jumpYOffset drop discontinuously - the
            // model popped down instead of easing out. One curve, exited early instead.
            jumpYOffset = Mathf.Sin(t * Mathf.PI * 0.5f) * jumpHeight * scale;

            GroundArea under = TileStep.JumpTarget(transform.position, startElevation, maxStepUp);
            shadowGround = under;

            gapTime = (under == null) ? gapTime + Time.deltaTime : 0f;
            SetShadowVisible(under != null || gapTime < shadowHideDelay);

            ApplyHeights();

            // ADDED (variable jump): released and past the minimum - stop rising and let
            // the descent take over from here. Placed AFTER ApplyHeights so the frame we
            // leave on is drawn; the old break skipped it. minRise replaces tapWindow,
            // which is no longer needed - a height floor guards a too-short hop better
            // than a time floor, because it does not depend on frame rate.
            if (!Input.GetKey(KeyCode.Space) && jumpYOffset >= minRise) break;

            yield return null;
        }

        // ADDED (v13): apex reached. Fold the arc into elevation so the handoff happens
        // at the exact height the feet are already at - no pop - then let the fall take
        // over. REMOVED with it: the landing block, the crossing test and the
        // DropTarget/takeoffArea fallbacks, which all live in FallCoroutine now.
        // CHANGED (stop pass): the disableFalling early-out that used to sit here is
        // gone. Skipping the descent removed the motion entirely; stopAtGroundPoint
        // inside FallCoroutine changes where it ENDS instead, which is the part that
        // was actually wrong.
        elevation = startElevation + jumpYOffset;
        jumpYOffset = 0f;
        isJumping = false;

        // ADDED (variable jump): the ascent's speed at the moment it ended, as a
        // NEGATIVE value because FallCoroutine subtracts speed from elevation - still
        // rising is negative downward speed. Derivative of the Sin curve, so it is
        // exact rather than a tuned guess.
        // Named exitT, not t: the loop above already uses t for its own progress and
        // C# will not let an enclosing scope reuse the name.
        float exitT = Mathf.Clamp01(elapsedTime / jumpDuration);
        float riseRate = Mathf.Cos(exitT * Mathf.PI * 0.5f)
                       * jumpHeight * scale * (Mathf.PI * 0.5f) / jumpDuration;

        StartCoroutine(FallCoroutine(startElevation, -riseRate));
    }

    #endregion

    // CHANGED (v13): the ONE descent. Used by the walk-off drop, by the end of a jump's
    // ascent, and by anything else that leaves the player airborne.
    // The target is not passed in - the fall finds it by crossing test, because a
    // jump's descent can pass through a tile the takeoff position knew nothing about.
    //
    // 'fromElevation' is the height the JUMP started at, which is what maxStepUp is
    // measured against. For a walk-off it is just the current elevation.
    private IEnumerator FallCoroutine(float fromElevation, float entrySpeed = 0f)
    {
        isFalling = true;                       // ApplyMovement reads this - no drop checks while true

        Debug.Log($"FALL START area={currentArea?.name} stopFlag={currentArea?.stopAtGroundPoint} elev={elevation}");


        // ADDED (stop pass): remember the ground this fall began on. Captured ONCE
        // rather than read per frame, because SetArea can reassign currentArea
        // mid-descent and the rule belongs to where the fall STARTED. Null when the
        // flag is off, which makes the check below a single null test.
        GroundArea fallGuard = (currentArea != null && currentArea.stopAtGroundPoint)
            ? currentArea
            : null;


        // CHANGED (variable jump): was "float speed = 0f". Negative means still rising -
        // the integration below handles that for free, so the arc continues into the
        // fall instead of restarting at it.
        float speed = entrySpeed;

        while (true)
        {
            // ADDED (v13): accelerating fall. Integrating a speed instead of lerping to
            // a known target means the fall does not need to know where it ends - which
            // is what lets the crossing test below decide that mid-flight.
            speed += fallAcceleration * Time.deltaTime;

            float previousHeight = elevation;
            elevation -= speed * Time.deltaTime;

            // The Mario landing test, moved here from JumpCoroutine. A CROSSING, not an
            // overlap: were the feet above the surface last frame and at or below it
            // this frame. Frame-rate independent, which the overlap tests never were.
            GroundArea tile = TileStep.JumpTarget(transform.position, fromElevation, maxStepUp);
            Debug.Log($"falling elev={elevation} tile={tile?.name} pos={transform.position}");

            // ADDED (fall fix): a tile ABOVE the falling player is not a landing - they
            // are already inside it. JumpTarget measures reach from where the jump
            // STARTED, so drifting into a raised platform mid-descent used to return
            // that platform even after the player had fallen below its surface, which
            // hid the floor underneath and let the fall run to fallFloor.
            if (tile != null && tile.ElevationAt(transform.position) > previousHeight)
                tile = TileStep.DropTarget(transform.position, previousHeight);

            // ADDED (v13 fix): the descent half of the shadow. Same three jobs the
            // ascent loop does - what is underneath, whether it is a gap, and pushing
            // that to ApplyHeights - so the shrink keeps animating all the way to the
            // landing instead of freezing at the apex.
            shadowGround = tile;
            gapTime = (tile == null) ? gapTime + Time.deltaTime : 0f;
            SetShadowVisible(tile != null || gapTime < shadowHideDelay);

            // CHANGED (ramp pass): sampled once per frame at the player's position. This
            // is the compromise noted in ElevationRamp's header - the crossing test needs
            // a single number to compare against, so drifting sideways mid-fall lands you
            // at the height under you THIS frame, not a continuously resolved surface.
            float tileHeight = (tile != null) ? tile.ElevationAt(transform.position) : 0f;

            bool crossed = tile != null && previousHeight > tileHeight && elevation <= tileHeight;
            Debug.Log($"prev={previousHeight} elev={elevation} tileH={tileHeight} crossed={crossed}");

            if (crossed)
            {
                Debug.Log($"LANDED on {tile.name} at {tileHeight}");
                isFalling = false;
                SetArea(tile);      // snaps to exactly the tile's height and restores the shadow
                yield break;
            }

            // ADDED (stop pass): the floor under this fall. Where the test above catches
            // a tile the player is CURRENTLY over, this one catches the ground they LEFT
            // - so a jump that comes down past the platform's edge still returns to it
            // rather than dropping all the way to fallFloor.
            // Placed AFTER the crossing test on purpose: a real landing on some other
            // tile should still win. This is the fallback, not the first answer.
            if (fallGuard != null && elevation <= fallGuard.ElevationAt(transform.position))
            {
                Debug.Log($"STOPPED at {fallGuard.name}");
                isFalling = false;
                SetArea(fallGuard);
                yield break;
            }

            // ADDED (v13): nothing caught us and we are below every tile here - a pit.
            // CHANGED (surface types): was SetArea(null), which parked the player at
            // elevation 0 with no area and no bounds - a state nothing recovered from.
            if (elevation < fallFloor)
            {
                Debug.Log($"PIT RETURN fired, elev={elevation}");
                isFalling = false;
                transform.position = lastSafePosition;
                SetArea(lastSafeArea);
                yield break;
            }

            ApplyHeights();
            yield return null;
        }
    }


    private void AdjustPlayerScale()
    {
        float rawScale = PlayerScale * (PlayerRatio - transform.position.y);
        rawScale = Mathf.Max(rawScale, 0.01f);

        currentScale = rawScale;

        Vector3 scale = transform.localScale;
        scale.x = rawScale;
        scale.y = rawScale;
        transform.localScale = scale;
    } //end of function >;D


    public IEnumerator MoveToPoint(Transform target, Vector2 destination)
    {
        Vector2 positionDifference = destination - (Vector2)target.position;

        while (positionDifference.magnitude > moveAccuracy)
        {
            target.Translate(moveSpeed * positionDifference.normalized * Time.deltaTime);
            positionDifference = destination - (Vector2)target.position;
            yield return null;
        }

        target.position = destination;
    } //end of function >:D
}