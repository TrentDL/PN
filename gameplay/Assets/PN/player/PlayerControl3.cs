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
//   * ADDED: the stopAtGroundPoint floor inside FallCoroutine. Same fall, same arc,
//     same shadow shrink - it just cannot pass below the height it started from.
//
// CHANGED (Drop Target toggle):
//   * ADDED: useDropTarget, read once from the area the fall STARTED on. Guards the
//     "tile above the falling player" rejection so a level can restore the old
//     JumpTarget-only landing per-area if this ever conflicts with it.
//
// CHANGED (variable jump):
//   * ADDED: tapJumpHeight / minRise - a released jump key stops the ascent early
//     instead of always reaching jumpHeight, once past a minimum rise.
//   * ADDED: entrySpeed passed into FallCoroutine - the ascent's exit velocity
//     (the Sin curve's derivative) carries into the descent so a short hop does not
//     stop dead for a frame before gravity takes over.
//
// CHANGED (tuning pass): fallFloor and maxStepUp are now RESOLVED per-fall/per-jump
//     from the area the descent/ascent started on, via GroundArea.ResolveFallFloor /
//     ResolveMaxStepUp. The fields below remain the DEFAULT for any area that does
//     not set an override - nothing was removed, only made overridable.
//
// CHANGED (ownership pass): the note above is superseded. The fallFloor and
//     maxStepUp FIELDS are gone from this script - GroundArea holds the values and
//     GroundArea.DefaultFallFloor / DefaultMaxStepUp hold the no-area fallback.
//     Resolve* became the static GroundArea.FallFloorFor / MaxStepUpFor, which
//     accept a null area, so the "currentArea != null ? ... : ..." ternary that used
//     to wrap every call is gone from both coroutines.
//   * What did NOT move, and cannot: the fall itself. The floor TEST reads
//     'elevation' and the recovery writes lastSafePosition/lastSafeArea - all three
//     are private state of this script that only FallCoroutine advances.
//   * FIXED in the same pass: FallCoroutine's TileStep.JumpTarget call passed the
//     raw maxStepUp field while JumpCoroutine passed the resolved value, so an area
//     with an override silently disagreed with itself between ascent and descent.
//
// ADDED (wall pass): the blockEntry branch in ApplyMovement's confinement loop.
//     Makes a GroundArea solid on foot and passable over the top, which is what
//     EdgeZone already did for lines.
//   * The height it compares is elevation + jumpYOffset, NOT elevation. WHY: the
//     ascent's rise lives in jumpYOffset and is only folded back into elevation
//     after JumpCoroutine's loop ends, so elevation is frozen at the takeoff height
//     for the whole jump. A gate compared against elevation alone could never open
//     mid-jump - which is the entire point of the gate.
//   * The branch sits in the confinement loop rather than the boundary block above
//     it on purpose: that block is guarded by !isJumping && !isFalling, and a wall
//     that stopped applying mid-jump would be defeated by jumping.
//
// CHANGED (ramp pass):
//   * CHANGED: every read of area.elevation is now area.ElevationAt(position).
//   * ADDED: the per-frame resample at the end of ApplyMovement. WHY: SetArea only
//     fires when the AREA changes, so a ramp sampled on entry stayed frozen while
//     you walked across it. Walking within one area changes position, not area.
//
// CHANGED (height gate pass): EdgeZone.Blocker now takes the player's elevation, so
//     an edge can refuse to open its canJumpOver/canFallThrough permissions until
//     the player's height clears a threshold set on that EdgeZone.
//
// CHANGED (facing layer pass):
//   * REMOVED: the frame-scrub of the niel_flip clip (flipStateName, leftFrame,
//     rightFrame, clipLengthFrames, flipDuration, flipT, flipTarget, flipStateHash,
//     ApplyFacing, and animator.speed = 0 in Start).
//     WHY: animator.speed = 0 freezes the WHOLE Animator - every layer - so an idle
//     could never play at the same time as the facing pose.
//   * ADDED: FacingRightHash + a two-line UpdateFacing. The script now only REPORTS
//     which way the player faces; the Animator's "facing" layer decides WHAT that
//     looks like, by keyframing the art meshes' GameObject.IsActive.
//     Result: art changes (new hands, a head-turn clip, a back view) happen in the
//     Animator and clips, not in this script.
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

    // ADDED (facing layer pass): the Animator Bool both layers read. Hashed ONCE for
    // the whole game (static readonly) instead of every call - same reason the old
    // flipStateHash was cached. The string MUST match the parameter name in the
    // Animator window exactly, including capitals.
    static readonly int FacingRightHash = Animator.StringToHash("FacingRight");

    #region movement fields/properties

    [Header("References")]
    [Tooltip("Live2D model root. Gets lifted during jumps and by ground elevation.")]
    public Transform visualRoot;

    [Header("Facing")]
    [Tooltip("Animator holding the flip state.")]
    public Animator animator;

    // REMOVED (facing layer pass): the flip-clip scrub fields. Kept as comments so
    // the old values are on record - Unity drops serialized data for a field that
    // no longer exists, same as the maxStepUp note below.
    // [Tooltip("Name of the state playing the flip clip.")]
    // public string flipStateName = "flip";
    // [Tooltip("Frame of the flip clip that shows the left-facing pose.")]
    // public int leftFrame = 0;
    // [Tooltip("Frame of the flip clip that shows the right-facing pose.")]
    // public int rightFrame = 13;
    // [Tooltip("Total length of the flip clip in frames. Used to convert a frame to normalized time.")]
    // public int clipLengthFrames = 13;
    // [Tooltip("Seconds for a full turn from one side to the other.")]
    // public float flipDuration = 0.15f;
    //
    // private int flipStateHash;               // cached - Play(hash) avoids a string lookup per seek
    // private float flipT = 0f;                // where the turn actually is:  0 = left, 1 = right
    // private float flipTarget = 0f;           // where the input wants it to be

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
    [Tooltip("How long the ASCENT takes. The fall is separate and self-timing.")]
    public float jumpDuration = 0.5f;
    [Tooltip("Must be a CHILD of this object so it follows the player horizontally.")]
    public GameObject playerShadow;

    // REMOVED (single knob pass): public float elevationToWorld = 5.5f;
    // Moved to GroundArea.ElevationToWorld, a const shared with the preview gizmo.
    // WHY: this field held 5.5 while GroundArea's held 5.349, so the gizmo drew the
    // walkable surface at one scale and ApplyHeights placed the player at another -
    // a 2.8% disagreement on every platform. Two knobs that multiply into the same
    // result can each appear to fix the other's error, which is what made the
    // offset seem to move around. One const cannot disagree with itself.
    // Read via GroundArea.ElevationToWorld.

    // REMOVED (ownership pass): public float maxStepUp = 1.5f;
    // Moved to GroundArea.maxStepUp, with GroundArea.DefaultMaxStepUp as the
    // no-area fallback. Read via GroundArea.MaxStepUpFor(currentArea).
    // NOTE: the inspector value you had set on the player is NOT carried over -
    // Unity drops serialized data for a field that no longer exists. Re-enter it on
    // the GroundArea components that need something other than the default.
    // Unity - Script Serialization: https://docs.unity3d.com/Manual/script-Serialization.html

    [Tooltip("Elevation units per second squared. Higher = heavier.")]
    public float fallAcceleration = 20f;

    // REMOVED (ownership pass): public float fallFloor = -5f;
    // Moved to GroundArea.fallFloor, with GroundArea.DefaultFallFloor as the
    // no-area fallback. Read via GroundArea.FallFloorFor(currentArea).
    // KEPT HERE: fallAcceleration above, and jumpHeight/tapJumpHeight - those
    // describe the CHARACTER's body, not the ground it happens to be standing on.

    [Tooltip("Seconds over open space before the shadow is deactivated. 0 = hide the instant there is no ground below.")]
    public float shadowHideDelay = 0.08f;

    private float gapTime = 0f;   // ADDED: how long we have had nothing beneath us

    private GroundArea shadowGround;

    [Tooltip("Keep moving at takeoff speed if the movement keys are released mid-jump. Off = hard stop in the air.")]
    public bool JumpCarryOver = true;

    private Vector2 jumpMomentum;

    private float jumpYOffset = 0f;

    private bool isJumping = false;

    private bool isFalling = false;

    private Vector3 shadowStartScale;
    private Vector3 shadowStartLocalPos;
    private Vector3 visualStartLocalPos;

    private float currentScale = 1f;

    [Header("Boundary Settings")]
    public bool enableBoundaryChecking = true;

    private GroundArea currentArea;

    private float elevation = 0f;

    private Vector2 lastSafePosition;
    private GroundArea lastSafeArea;

    [Header("Variable Jump")]
    // ADDED (variable jump): the tap version. A jump released early stops rising here
    // instead of at jumpHeight, so a tap hops and a hold clears a platform.
    [Tooltip("Height a tapped jump reaches. Must be below jumpHeight or holding does nothing.")]
    public float tapJumpHeight = 0.8f;

    // ADDED: scale captured the moment a freezeScaleWhileOn area is entered.
    // AdjustPlayerScale writes over currentScale every frame, so this is a separate
    // value that survives that overwrite.
    private float frozenScale = 1f;
    private bool scaleIsFrozen = false;

    [Header(" TEMP Debug")]
    // ADDED (visual offset diagnosis): off by default so the log costs nothing in
    // normal play. Tick it only while chasing the height bug.
    [Tooltip("Log visualRoot's height at takeoff and at every landing.")]
    public bool logVisualHeight = false;


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
        // REMOVED (facing layer pass): the else-branch below. The Animator now runs at
        // normal speed so every layer plays, and the facing layer's DEFAULT state
        // (the orange one in the Animator window) is the starting pose - no manual
        // first seek needed.
        // else
        // {
        //     flipStateHash = Animator.StringToHash(flipStateName);
        //     animator.speed = 0f;   // the clip never plays itself - this script owns its time
        //     ApplyFacing();         // land on the left pose before the first input
        // }

        if (playerShadow != null)
        {
            shadowStartScale = playerShadow.transform.localScale;
            shadowStartLocalPos = playerShadow.transform.localPosition;
        }

        currentArea = GroundArea.AreaAt(transform.position);
        if (enableBoundaryChecking && currentArea == null)
        {
            Debug.LogWarning("PlayerControl3: player did not start inside any GroundArea. " +
                             "Movement will be unbounded until it lands on one.", this);
        }
        SetArea(currentArea);
    }//end of function >:D


    void Update()
    {
        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        Vector2 dir = moveInput.normalized;
        moveVelocity = new Vector2(dir.x * moveXspeed, dir.y * moveYspeed);

        DetectDoubleTap();
        ApplySprintSpeed();
        UpdateFacing();
        AdjustPlayerScale();
        ApplyScaleToSpeed();
        JumpPhysics();

        if (Input.GetKeyDown(KeyCode.Space) && !isJumping && !isFalling)
        {
            StartCoroutine(JumpCoroutine());
        }

        if (rb == null) ApplyMovement(Time.deltaTime);
    }


    void FixedUpdate()
    {
        if (rb != null) ApplyMovement(Time.fixedDeltaTime);
    }

    private void ApplyScaleToSpeed()
    {
        if (!scaleSpeedWithSize || referenceScale <= 0f) return;
        moveVelocity *= currentScale / referenceScale;
    } //end of function >:d


    private void JumpPhysics()
    {
        bool airborne = isJumping || isFalling;

        if (!airborne)
        {
            jumpMomentum = moveVelocity;
            return;
        }

        if (!JumpCarryOver) return;

        if (moveInput == Vector2.zero) moveVelocity = jumpMomentum;
    }


    // Single movement path, used by either Update or FixedUpdate.
    private void ApplyMovement(float deltaTime)
    {
        Vector2 current = (rb != null) ? rb.position : (Vector2)transform.position;
        Vector2 desired = current + moveVelocity * deltaTime;
        
        // TEMP (pit diagnosis): what does each lookup say at the player's target position?
        // Remove once the pit behaves.
        GroundArea ruler = GroundArea.RulingAreaAt(desired);
        GroundArea landing = GroundArea.AreaAt(desired);
        Debug.Log($"ruler={(ruler ? ruler.name : "null")} fallThrough={(ruler ? ruler.canFallThrough.ToString() : "-")} " +
          $"prio={(ruler ? ruler.priority.ToString() : "-")} | AreaAt={(landing ? landing.name : "null")} | " +
          $"elev={elevation:F2} walkOff={(currentArea ? currentArea.allowWalkOffIntoGap.ToString() : "-")}");

          // TEMP (pit diagnosis, second pass): currentArea should NEVER be a canFallThrough
          // area - AreaAt filters those out, so anything that sets one is using the wrong
          // lookup. Remove with the block above.
          if (currentArea != null && currentArea.canFallThrough)
          Debug.LogError($"currentArea is a fall-through area: {currentArea.name}", currentArea);

        if (enableBoundaryChecking && !isJumping && !isFalling)
        {
            GroundArea ahead = TileStep.JumpTarget(desired, elevation, 0f);

            if (ahead == null)
            {
                if (currentArea != null && currentArea.blockWalkOff)
                {
                    desired = currentArea.ClampInside(desired);
                }
                else
                {
                    GroundArea drop = TileStep.DropTarget(desired, elevation);

                    bool insidePit = currentArea != null && currentArea.Contains(desired);
                    bool openLedge = currentArea != null && currentArea.allowWalkOffIntoGap;

                    if (drop != null || insidePit || openLedge) StartCoroutine(FallCoroutine(elevation));
                    else if (currentArea != null) desired = currentArea.ClampInside(desired);
                }
            }
            else if (ahead != currentArea)
            {
                float aheadHeight = ahead.ElevationAt(desired);

                if (aheadHeight < elevation && currentArea != null && currentArea.blockWalkOff)
                    desired = currentArea.ClampInside(desired);
                else if (aheadHeight < elevation)
                    StartCoroutine(FallCoroutine(elevation));
                else
                    SetArea(ahead);
            }

            // Extra zones - props, obstacles. Delete these two lines if your
            // project has no BoundsZone script.
            if (!BoundsZone.IsWithinBounds(desired, "player"))
                desired = BoundsZone.ClampToBounds(desired, "player");

        } 

        // CHANGED (height gate pass): Blocker now takes 'elevation' as a fifth
        // argument, so an EdgeZone can refuse to open its permissions until the
        // player's own height clears a threshold set on that edge.
        // CHANGED (wall pass): elevation -> elevation + jumpYOffset. Same bug as the
        // blockEntry branch below had - elevation is frozen at the takeoff height
        // for the whole ascent, so an EdgeZone's heightGateThreshold could never
        // open during a jump either. Both are 0 on the ground, so walking is
        // unaffected.
        EdgeZone blocker = EdgeZone.Blocker(current, desired, isJumping || isFalling, isFalling, elevation + jumpYOffset);
        if (blocker != null) desired = blocker.StopShortOf(current, desired);

        for (int i = 0; i < GroundArea.All.Count; i++)
        {
            GroundArea a = GroundArea.All[i];

            // ADDED (wall pass): solid on foot, passable over the top. Entry is
            // "outside last frame, inside this frame" - two Contains calls, no new
            // geometry. Sits ABOVE the lockMovementInside guard below because the
            // two are opposites: that one requires Contains(current) to be true,
            // this one requires it to be false, so neither can reach the other.
            //
            // elevation + jumpYOffset, not elevation: the ascent's rise lives in
            // jumpYOffset until JumpCoroutine's loop ends, so elevation alone is
            // frozen at the takeoff height and the gate could never open mid-jump.
            // Both are 0 while grounded, so this reads as plain elevation on foot.
            // NOTE the ungated case is written out rather than folded into the
            // comparison: with threshold 0, "height < threshold" is false for every
            // height, so a plain < would make an ungated wall let EVERYTHING through
            // - the exact opposite of what a 0 default should mean.
            if (a.blockEntry && !a.Contains(current) && a.Contains(desired))
            {
                bool gateOpen = a.heightGateThreshold > 0f
                             && (elevation + jumpYOffset) >= a.heightGateThreshold;

                if (!gateOpen)
                {
                    desired = current;   // refuse the step in - the wall holds
                    continue;
                }
            }

            if (!a.lockMovementInside || !a.Contains(current)) continue;

            if (a.pushBackSpeed <= 0f)
            {
                desired = a.ClampInside(desired);
                continue;
            }

            if (a.Contains(desired)) continue;

            Vector2 inward = (a.ClampInside(desired) - desired).normalized;
            desired = current + inward * a.pushBackSpeed * deltaTime;
        }

        if (!isJumping && !isFalling && currentArea != null)
        {
            elevation = currentArea.ElevationAt(desired);
            ApplyHeights();
        }

        if (rb != null)
            rb.MovePosition(desired);
        else
            transform.position = new Vector3(desired.x, desired.y, transform.position.z);
    }//end of function?


    private void ApplyHeights()
    {
        if (visualRoot != null)
        {
            Vector3 lp = visualStartLocalPos;
            lp.y = visualStartLocalPos.y + jumpYOffset + elevation;
            visualRoot.localPosition = lp;

            // CHANGED (depth decoupling): elevation is divided by currentScale so the
            // parent transform's multiplication cancels it, leaving a FIXED world
            // lift of elevation * GroundArea.ElevationToWorld at any depth.
            //
            // WHY: localPosition is scaled by the parent, and the parent's scale IS
            // currentScale (see AdjustPlayerScale). So the old line lifted the player
            // elevation * currentScale world units - 5.164 at the back of the room,
            // 6.062 at the front. The platform sprite is a fixed world object and does
            // not rescale, so the player slid relative to a surface that never moved.
            //
            // jumpYOffset is deliberately NOT divided: the arc SHOULD look bigger up
            // close, and JumpCoroutine already multiplies it by scale for that reason.
            float lift = (currentScale > 0.0001f)
                ? elevation * GroundArea.ElevationToWorld / currentScale
                : elevation * GroundArea.ElevationToWorld;
    
        }

        if (playerShadow == null) return;

        float groundHeight = (shadowGround != null)
            ? shadowGround.ElevationAt(transform.position)
            : elevation;

        Vector3 sp = shadowStartLocalPos;
        sp.y = shadowStartLocalPos.y + groundHeight;
        playerShadow.transform.localPosition = sp;

        float heightAboveGround = jumpYOffset + (elevation - groundHeight);
        float shadowScale = (jumpHeight > 0f) ? 1f - (heightAboveGround / jumpHeight) * 0.1f : 1f;
        playerShadow.transform.localScale = shadowStartScale * shadowScale;
        
    } //end of function >:D


    // ADDED (visual offset diagnosis): reports what ApplyHeights MEANT to apply
    // against what visualRoot actually ended up at. Kept separate from ApplyHeights
    // so that function keeps its single job - this one only reads and prints.
    private void LogVisualHeight(string moment)
    {
        if (!logVisualHeight || visualRoot == null) return;

        float baseY    = visualStartLocalPos.y;
        float localY   = visualRoot.localPosition.y;
        float applied  = localY - baseY;              // what is actually on the transform
        float intended = jumpYOffset + elevation;      // what ApplyHeights composes
        float parentScaleY = (visualRoot.parent != null) ? visualRoot.parent.lossyScale.y : 1f;

        Debug.Log(
            $"[{moment}] area={(currentArea ? currentArea.name : "null")} " +
            $"elev={elevation:F3} jumpY={jumpYOffset:F3} intended={intended:F3} " +
            $"applied={applied:F3} | localY={localY:F3} baseY={baseY:F3} " +
            $"worldY={visualRoot.position.y:F3} | scale={currentScale:F3} " +
            $"parentScaleY={parentScaleY:F3}", this);
    }// end of function >:D



    private void SetShadowVisible(bool visible)
    {
        if (playerShadow == null || playerShadow.activeSelf == visible) return;
        playerShadow.SetActive(visible);
    }


    private void SetArea(GroundArea area)
    {
        currentArea = area;
        elevation = (area != null) ? area.ElevationAt(transform.position) : 0f;

        if (area != null)
        {
            lastSafeArea = area;
            lastSafePosition = transform.position;
        }

        // ADDED: capture the scale at the moment this area is adopted, before
        // AdjustPlayerScale can change it further. Read by AdjustPlayerScale below.
        scaleIsFrozen = area != null && area.freezeScaleWhileOn;
        if (scaleIsFrozen) frozenScale = currentScale;

        ApplyHeights();

        gapTime = 0f;
        shadowGround = area;
        SetShadowVisible(true);
    }// end of function >:D


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
    // 8. FRAME SCRUB (added facing layer pass) - animator.speed = 0, then Play()
    //    seeked niel_flip to a frame between leftFrame and rightFrame over
    //    flipDuration. Replaced because speed = 0 froze EVERY Animator layer, so
    //    the idle could not play at the same time. Differs from #2: the transition
    //    now lives on its OWN layer whose clips key only GameObject.IsActive, so
    //    there are no unrelated parameters for a cross-fade to drag along.
    #endregion


    // CHANGED (facing layer pass): was a timed seek through the flip clip. Now it only
    // tells the Animator which way we face; the "facing" layer's clips decide which
    // art meshes are active. No input (x == 0) leaves the last direction in place -
    // that is what keeps the player facing left after letting go of A.
    private void UpdateFacing()
    {
        if (animator == null || moveInput.x == 0f) return;
        animator.SetBool(FacingRightHash, moveInput.x > 0f);
    }


    // REMOVED (facing layer pass): ApplyFacing(). It seeked the flip clip by frame;
    // see #8 in the region above.


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


    private IEnumerator JumpCoroutine()
    {
        if (visualRoot == null) yield break;

        if (!GroundArea.CanJumpOverPoint(transform.position)) yield break;

        isJumping = true;
        float startElevation = elevation;
        float elapsedTime = 0f;

        // CHANGED (tuning pass): resolved from the area being jumped FROM.
        // CHANGED (ownership pass): the null check moved INTO MaxStepUpFor, so the
        // ternary that used to be here is gone. Same behaviour, one caller-side
        // branch fewer. Still the area being jumped FROM - see the note on
        // GroundArea.maxStepUp for why the destination cannot be asked.
        float effectiveMaxStepUp = GroundArea.MaxStepUpFor(currentArea);

        float scale = scaleSpeedWithSize ? currentScale / referenceScale : 1f;
        float minRise = tapJumpHeight * scale;

        LogVisualHeight("takeoff");  

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / jumpDuration;

            jumpYOffset = Mathf.Sin(t * Mathf.PI * 0.5f) * jumpHeight * scale;

            GroundArea under = TileStep.JumpTarget(transform.position, startElevation, effectiveMaxStepUp);
            shadowGround = under;

            gapTime = (under == null) ? gapTime + Time.deltaTime : 0f;
            SetShadowVisible(under != null || gapTime < shadowHideDelay);

            ApplyHeights();

            // ADDED (variable jump): released and past the minimum - stop rising and
            // let the descent take over. Placed AFTER ApplyHeights so the frame we
            // leave on is drawn.
            if (!Input.GetKey(KeyCode.Space) && jumpYOffset >= minRise) break;

            yield return null;
        
        }

        elevation = startElevation + jumpYOffset;
        jumpYOffset = 0f;
        LogVisualHeight("apex-resolved"); 
        isJumping = false;

        // ADDED (variable jump): the ascent's speed at the moment it ended, as a
        // NEGATIVE value because FallCoroutine subtracts speed from elevation - still
        // rising is negative downward speed. Derivative of the Sin curve, so it is
        // exact rather than a tuned guess. Named exitT: the loop above already uses t.
        float exitT = Mathf.Clamp01(elapsedTime / jumpDuration);
        float riseRate = Mathf.Cos(exitT * Mathf.PI * 0.5f)
                       * jumpHeight * scale * (Mathf.PI * 0.5f) / jumpDuration;

        StartCoroutine(FallCoroutine(startElevation, -riseRate));
    }


    private IEnumerator FallCoroutine(float fromElevation, float entrySpeed = 0f)
    {
        isFalling = true;

        GroundArea fallGuard = (currentArea != null && currentArea.stopAtGroundPoint)
            ? currentArea
            : null;

        // ADDED (Drop Target toggle): whether the above-me rejection applies to this
        // fall. Read once from the area the fall STARTED on.
        bool useDropTarget = currentArea == null || currentArea.dropTarget;

        // CHANGED (tuning pass): resolved from the area the fall STARTED on.
        // CHANGED (ownership pass): null check moved into FallFloorFor. A fall CAN
        // begin with currentArea null, which is exactly why that method is static
        // and takes the area rather than being called on it.
        float effectiveFallFloor = GroundArea.FallFloorFor(currentArea);

        // ADDED (ownership pass): the descent needs this too, and did not have it.
        // See the FIXED note in the header - the loop below was passing the raw
        // field while the ascent passed the resolved value.
        float effectiveMaxStepUp = GroundArea.MaxStepUpFor(currentArea);

        float speed = entrySpeed;

        while (true)
        {
            speed += fallAcceleration * Time.deltaTime;

            float previousHeight = elevation;
            elevation -= speed * Time.deltaTime;

            // CHANGED (ownership pass): was the raw maxStepUp field. That ignored
            // any per-area value, so a jump's ascent and its descent could search
            // different heights on the same area. Now resolved, once, above.
            GroundArea tile = TileStep.JumpTarget(transform.position, fromElevation, effectiveMaxStepUp);

            if (useDropTarget && tile != null && tile.ElevationAt(transform.position) > previousHeight)
                tile = TileStep.DropTarget(transform.position, previousHeight);

            shadowGround = tile;
            gapTime = (tile == null) ? gapTime + Time.deltaTime : 0f;
            SetShadowVisible(tile != null || gapTime < shadowHideDelay);

            float tileHeight = (tile != null) ? tile.ElevationAt(transform.position) : 0f;

            bool crossed = tile != null && previousHeight > tileHeight && elevation <= tileHeight;

            if (crossed)
            {
                isFalling = false;
                SetArea(tile);
                LogVisualHeight($"land-on-{tile.name}");
                yield break;
            }

            if (fallGuard != null && elevation <= fallGuard.ElevationAt(transform.position))
            {
                isFalling = false;
                SetArea(fallGuard);
                LogVisualHeight($"land-on-fallguard-{fallGuard.name}");
                yield break;
            }

            // CHANGED (tuning pass): was fallFloor, now effectiveFallFloor.
            if (elevation < effectiveFallFloor)
            {
                isFalling = false;
                transform.position = lastSafePosition;
                SetArea(lastSafeArea);
                LogVisualHeight("land-on-fallfloor-recovery");
                yield break;
            }

            ApplyHeights();
            yield return null;
        }
    }


    private void AdjustPlayerScale()
    {
        // ADDED: skip the recompute entirely while frozen, so currentScale (and
        // therefore transform.localScale) holds steady at whatever it was on entry.
        if (scaleIsFrozen) return;

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