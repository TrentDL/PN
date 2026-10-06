// PlayerControl4.cs
//
// ADDED (root only pass): a copy of PlayerControl3 where ONE object moves - the
// root this script sits on (playerAlt). Made as a separate class so PlayerControl3
// stays untouched for comparison. Unity needs the file name to match the class name.
//
// THE IDEA, in one line:
//      root position = groundPosition + (elevation + jumpYOffset) * 5.349
//
//      groundPosition - the chalk mark on the floor (left/right and depth)
//      elevation      - how high the floor under you is
//      jumpYOffset    - extra height from the jump, only while rising
//      5.349          - GroundArea.ElevationToWorld, elevation units -> scene units
//
// WHY THIS FIXES THE JUMP: at the top of a jump, jumpYOffset is moved INTO
//      elevation and then reset to 0. One goes up by exactly what the other goes
//      down, so their SUM - the only thing the root reads - does not change. No
//      hand-off between two objects, so nothing can pop or dip.
//
// REMOVED from PlayerControl3 (Trent asked for these to go):
//   * visualRoot, visualStartLocalPos and the "visualRoot is not assigned" check.
//     VisualsRoot is no longer moved by script - it simply rides inside the root.
//   * playerShadow, shadowStartScale, shadowStartLocalPos, shadowHideDelay,
//     gapTime, shadowGround and SetShadowVisible - all of the platform shadow.
//   * ApplyHeights and ToLocalLift. ApplyHeights only existed to move visualRoot
//     and the shadow; ToLocalLift only existed to undo the root's scale for those
//     children. With no children to move, neither has a job. That also removes
//     every division by currentScale, which is where the "too high, then snap"
//     bug came from.
//   * liftRootWithElevation. This script ALWAYS lifts the root - that is its point.
//     For the old behaviour, use PlayerControl3 with the toggle off.
//   * The long change-history header. It is still in PlayerControl3.cs.
//
// CHANGED:
//   * PlaceRoot adds jumpYOffset (see THE IDEA above).
//   * PlaceRoot is called from ONE place only: the end of ApplyMovement, which
//     runs every frame. The jump and fall only change the numbers; ApplyMovement
//     draws them. One writer = one place to look when debugging.
//   * LogVisualHeight -> LogHeight. It reported visualRoot; now it reports the root.
//
// KEPT, unchanged: walking, sprint, dash hookup, facing, areas, walls, edges,
//   pits, ramps, the variable jump, falling, landing, fall-floor recovery and
//   depth scaling. Every rules lookup still reads groundPosition, never the
//   lifted root - see the DEPTH note in TileStep.cs for why.
//
// SIDE EFFECTS to expect: every child of the root (VisualsRoot, GroundPoint,
//   Circle, ItemHolder, PlayerUI) and any collider on it now rise with the jump.
//   A shadow child will rise too - disable it until the shadow comes back.
//
// ADDED (unnested shadow pass): the platform shadow is back, as a SEPARATE scene
//   object - not a child of the root. WHY: a child rides up with the jump and
//   inherits the root's scale, which is what forced the old divide-by-scale maths.
//   A separate object is simply PLACED in the world each frame, so neither applies.
//   * PlaceShadow() - the one place the shadow is written. Called right after
//     PlaceRoot, so the player and its shadow are always drawn from the same numbers.
//   * Position: the footprint, lifted to the height of the ground UNDER the player
//     (not the player's own height) - so it stays on the floor during a jump.
//   * Hidden over a gap, after shadowHideDelay so narrow gaps do not flicker. Same
//     rule as PlayerControl3, but the timer now lives in PlaceShadow instead of
//     being repeated in both coroutines (DRY).
//   * Size: shadowSize * the player's current scale, shrunk a little the higher
//     the player is - same shrink formula as PlayerControl3.
//   * The coroutines only choose WHICH ground the shadow sits on (shadowGround);
//     they no longer draw anything.
//   FIXED with it: currentScale now starts from the scene's scale instead of 1f.
//   The shadow multiplies by it, so a freeze area entered at startup would otherwise
//   shrink the shadow to scale 1 until the freeze ended.

using System.Collections;
using UnityEngine;


public class PlayerControl4 : MonoBehaviour
{
    static float moveSpeed = 5f, moveAccuracy = 0.15f;

    // Hashed ONCE for the whole game (static readonly) instead of every call. The
    // string MUST match the parameter name in the Animator window exactly.
    static readonly int FacingRightHash = Animator.StringToHash("FacingRight");

    #region movement fields/properties

    [Header("Facing")]
    [Tooltip("Animator holding the flip state.")]
    public Animator animator;

    private Rigidbody2D rb;              // MAY BE NULL - always guard
    private Dash dash;
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
    // CHANGED (root only pass): the jump now lifts the whole root, so this is in
    // elevation units - the same units as GroundArea.elevation.
    [Tooltip("Maximum height of a held jump, in elevation units (same as GroundArea.elevation). " +
             "MUST exceed your tallest tile's elevation or the player never crests it.")]
    public float jumpHeight = 2f;
    [Tooltip("How long the ASCENT takes. The fall is separate and self-timing.")]
    public float jumpDuration = 0.5f;

    [Tooltip("Elevation units per second squared. Higher = heavier.")]
    public float fallAcceleration = 20f;

    // ADDED (unnested shadow pass): see header.
    [Header("Shadow")]
    [Tooltip("A SEPARATE scene object, NOT a child of the player. The script places it on the ground under the player every frame. Leave empty for no shadow.")]
    public Transform playerShadow;

    [Tooltip("Shadow scale when the player's scale is 1. Multiplied by the player's current scale. " +
             "To match the old nested shadow, copy the Scale it had as a child.")]
    public Vector3 shadowSize = Vector3.one;

    [Tooltip("Nudge from the footprint, multiplied by the player's current scale. " +
             "To match the old nested shadow, copy the Position it had as a child.")]
    public Vector2 shadowOffset = Vector2.zero;

    [Tooltip("Seconds over open space before the shadow is hidden. 0 = hide the instant there is no ground below.")]
    public float shadowHideDelay = 0.08f;

    private GroundArea shadowGround;   // the ground the shadow sits on - null over a gap
    private float gapTime = 0f;        // how long there has been nothing below

    [Tooltip("Keep moving at takeoff speed if the movement keys are released mid-jump. Off = hard stop in the air.")]
    public bool JumpCarryOver = true;

    private Vector2 jumpMomentum;

    private float jumpYOffset = 0f;

    private bool isJumping = false;

    private bool isFalling = false;

    public bool IsAirborne => isJumping || isFalling;

    private float currentScale = 1f;

    [Header("Boundary Settings")]
    public bool enableBoundaryChecking = true;

    private GroundArea currentArea;

    private float elevation = 0f;

    // The footprint on the floor, unlifted. The root is drawn above it; every rules
    // lookup uses this instead of the root's own position.
    private Vector2 groundPosition;

    // Read-only, so other scripts (sorting, camera, enemies) can ask for depth
    // without being able to move the player.
    public Vector2 GroundPosition => groundPosition;

    private Vector2 lastSafePosition;
    private GroundArea lastSafeArea;

    [Header("Variable Jump")]
    // A jump released early stops rising here instead of at jumpHeight, so a tap
    // hops and a hold clears a platform.
    [Tooltip("Height a tapped jump reaches. Must be below jumpHeight or holding does nothing.")]
    public float tapJumpHeight = 0.8f;

    // Scale captured the moment a freezeScaleWhileOn area is entered.
    private float frozenScale = 1f;
    private bool scaleIsFrozen = false;

    [Header(" TEMP Debug")]
    // CHANGED (root only pass): was logVisualHeight. Same idea, reports the root.
    [Tooltip("Log the root's height at takeoff, at the top of the jump and at every landing.")]
    public bool logHeight = false;

    [Tooltip("Log why sprint turned on or off, per key.")]
    public bool logSprint = false;

    [Tooltip("Log every ground-area change and whether the scale freeze is on.")]
    public bool logAreaChanges = false;

    #endregion


    void Start()
    {
        rb = GetComponent<Rigidbody2D>();   // fine if null

        dash = GetComponent<Dash>();         // fine if null - no Dash component, no dash

        // REMOVED (root only pass): the visualRoot check and the shadow setup.

        if (animator == null)
        {
            Debug.LogError("PlayerControl4: animator is not assigned. Facing will not change.", this);
        }

        // The scene places the player on the floor, so the starting transform IS the
        // footprint. Captured before anything lifts it.
        groundPosition = transform.position;

        // ADDED (unnested shadow pass): start from the REAL scene scale, not the
        // field's 1f. SetArea below can freeze the scale before AdjustPlayerScale
        // has ever run, and the shadow's size multiplies by this number.
        currentScale = transform.localScale.y;

        if (playerShadow != null && playerShadow.IsChildOf(transform))
        {
            // ADDED (unnested shadow pass): a nested shadow would rise with the jump
            // and get the root's scale on top of shadowSize.
            Debug.LogWarning("PlayerControl4: playerShadow is a child of the player. " +
                             "Drag it out of the player in the Hierarchy so it stays on the ground.", this);
        }

        currentArea = GroundArea.AreaAt(groundPosition);
        if (enableBoundaryChecking && currentArea == null)
        {
            Debug.LogWarning("PlayerControl4: player did not start inside any GroundArea. " +
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

        if (dash != null && dash.IsDashing) moveVelocity = dash.Velocity;

        UpdateFacing();
        AdjustPlayerScale();
        ApplyScaleToSpeed();
        JumpPhysics();

        if (Input.GetKeyDown(KeyCode.Space) && !isJumping && !isFalling)
        {
            StartCoroutine(JumpCoroutine());
        }

        if (rb == null) ApplyMovement(Time.deltaTime);
    }//end of function >:D


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
        if (!IsAirborne)
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
        Vector2 current = groundPosition;
        Vector2 desired = current + moveVelocity * deltaTime;

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

        // elevation + jumpYOffset, not elevation: elevation is frozen at the takeoff
        // height while rising, so a height gate could never open mid-jump otherwise.
        EdgeZone blocker = EdgeZone.Blocker(current, desired, isJumping || isFalling, isFalling, elevation + jumpYOffset);
        if (blocker != null) desired = blocker.StopShortOf(current, desired);

        for (int i = 0; i < GroundArea.All.Count; i++)
        {
            GroundArea a = GroundArea.All[i];

            // Solid on foot, passable over the top. Entry = outside last frame,
            // inside this frame. The ungated case (threshold 0) is written out so a
            // 0 means "absolute wall", not "lets everything through".
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

        // Ramp resample: walking within one area changes position, not area, so the
        // height has to be re-read here every grounded frame.
        // CHANGED (root only pass): the ApplyHeights() call that followed is gone -
        // PlaceRoot below draws the new height.
        if (!isJumping && !isFalling && currentArea != null)
        {
            elevation = currentArea.ElevationAt(desired);
        }

        groundPosition = desired;
        PlaceRoot();
        PlaceShadow(deltaTime);   // ADDED (unnested shadow pass): drawn from the same numbers, same frame
    }//end of function >:D


    // ADDED (unnested shadow pass): the ONE place the shadow is written.
    // Three jobs, in order: hide it over a gap, put it on the ground, size it.
    private void PlaceShadow(float deltaTime)
    {
        if (playerShadow == null) return;

        // 1. Hide over a gap - but only after shadowHideDelay, so a narrow gap does
        //    not make it flicker. gapTime counts up while there is nothing below.
        gapTime = (shadowGround == null) ? gapTime + deltaTime : 0f;
        bool visible = shadowGround != null || gapTime < shadowHideDelay;

        if (playerShadow.gameObject.activeSelf != visible)
            playerShadow.gameObject.SetActive(visible);

        if (!visible) return;   // nothing to place

        // 2. Put it on the ground. The height of the ground UNDER the player, not the
        //    player's own height - that is what keeps it down during a jump. During
        //    the short delay over a gap there is no ground, so it holds the player's
        //    height for those few frames, as PlayerControl3 did.
        float groundHeight = (shadowGround != null)
            ? shadowGround.ElevationAt(groundPosition)
            : elevation;

        Vector2 spot = groundPosition
                     + shadowOffset * currentScale
                     + Vector2.up * (groundHeight * GroundArea.ElevationToWorld);

        // Setting .position (world) rather than .localPosition: no parent is involved,
        // so nothing gets multiplied by the player's scale behind our back.
        playerShadow.position = new Vector3(spot.x, spot.y, playerShadow.position.z);

        // 3. Size it. Same shrink as PlayerControl3: a full jump above the ground
        //    makes it 10% smaller. heightAboveGround / jumpHeight = "how much of a
        //    full jump am I above the ground" - 0 standing, 1 at the top of a jump.
        float heightAboveGround = (elevation + jumpYOffset) - groundHeight;
        float shrink = (jumpHeight > 0f) ? 1f - (heightAboveGround / jumpHeight) * 0.1f : 1f;

        playerShadow.localScale = shadowSize * (currentScale * shrink);
    }// end of function >:D


    // CHANGED (root only pass): the ONE place the root's position is written, and
    // now it includes the jump. Only addition and multiplication:
    //      height in scene units = (elevation + jumpYOffset) * 5.349
    // No division by scale - the root lives in world space, so Unity's parent
    // "zoom" never applies to it.
    private void PlaceRoot()
    {
        float lift = (elevation + jumpYOffset) * GroundArea.ElevationToWorld;
        Vector2 drawn = groundPosition + Vector2.up * lift;

        if (rb != null)
            rb.MovePosition(drawn);
        else
            transform.position = new Vector3(drawn.x, drawn.y, transform.position.z);
    }// end of function >:D


    // CHANGED (root only pass): was LogVisualHeight. Reports the root instead of
    // visualRoot. 'expectedY' is what PlaceRoot meant to draw; 'rootY' is where the
    // root actually is. If they differ by more than a hair, something else moved it.
    private void LogHeight(string moment)
    {
        if (!logHeight) return;

        float expectedY = groundPosition.y + (elevation + jumpYOffset) * GroundArea.ElevationToWorld;

        Debug.Log(
            $"[{moment}] area={(currentArea ? currentArea.name : "null")} " +
            $"elev={elevation:F3} jumpY={jumpYOffset:F3} groundY={groundPosition.y:F3} " +
            $"expectedY={expectedY:F3} rootY={transform.position.y:F3} " +
            $"scale={currentScale:F3} frame={Time.frameCount}", this);
    }// end of function >:D


    // Prints the sprint state at one moment. 'gap' is the time since the previous
    // press of the same key (0 when there is no press to measure).
    private void LogSprint(string moment, float gap)
    {
        if (!logSprint) return;

        Debug.Log(
            $"[sprint {moment}] gap={gap:F3}s window={doubleTapWindow:F3}s " +
            $"L={isSprintingLeft} R={isSprintingRight} moveX={moveInput.x} " +
            $"timeScale={Time.timeScale:F2} frame={Time.frameCount}", this);
    }// end of function >:D


    // One line per SetArea call - area left, area entered, scale freeze afterwards.
    private void LogAreaChange(GroundArea previous, GroundArea next)
    {
        if (!logAreaChanges) return;

        Debug.Log(
            $"[area] {(previous ? previous.name : "null")} -> {(next ? next.name : "null")} " +
            $"frozen={scaleIsFrozen} scale={currentScale:F3} pos={groundPosition} " +
            $"jumping={isJumping} falling={isFalling} frame={Time.frameCount}", this);
    }// end of function >:D


    private void SetArea(GroundArea area)
    {
        GroundArea previous = currentArea;   // remembered for the log

        currentArea = area;
        elevation = (area != null) ? area.ElevationAt(groundPosition) : 0f;

        if (area != null)
        {
            lastSafeArea = area;
            lastSafePosition = groundPosition;
        }

        // Capture the scale at the moment this area is adopted.
        scaleIsFrozen = area != null && area.freezeScaleWhileOn;
        if (scaleIsFrozen) frozenScale = currentScale;

        LogAreaChange(previous, area);

        // REMOVED (root only pass): ApplyHeights(), and the gapTime / shadowGround /
        // SetShadowVisible lines. The next ApplyMovement draws the new height.

        // ADDED (unnested shadow pass): standing on an area = the shadow sits on it.
        shadowGround = area;
        gapTime = 0f;
    }// end of function >:D


    // Tells the Animator which way we face; the "facing" layer's clips decide which
    // art meshes are active. No input (x == 0) keeps the last direction.
    private void UpdateFacing()
    {
        if (animator == null || moveInput.x == 0f) return;
        animator.SetBool(FacingRightHash, moveInput.x > 0f);
    }


    private void DetectDoubleTap()
    {
        if (Input.GetKeyDown(sprintKeyLeft))
        {
            float gap = Time.time - lastTapTimeLeft;
            if (gap < doubleTapWindow) isSprintingLeft = true;
            lastTapTimeLeft = Time.time;
            LogSprint("left down", gap);
        }

        if (Input.GetKeyDown(sprintKeyRight))
        {
            float gap = Time.time - lastTapTimeRight;
            if (gap < doubleTapWindow) isSprintingRight = true;
            lastTapTimeRight = Time.time;
            LogSprint("right down", gap);
        }

        if (Input.GetKeyUp(sprintKeyLeft))  { isSprintingLeft = false;  LogSprint("left up", 0f); }
        if (Input.GetKeyUp(sprintKeyRight)) { isSprintingRight = false; LogSprint("right up", 0f); }

        // Stuck-flag detector: a flag on while its key is NOT held means the "up"
        // was never seen.
        if (isSprintingLeft && !Input.GetKey(sprintKeyLeft))   LogSprint("STUCK left", 0f);
        if (isSprintingRight && !Input.GetKey(sprintKeyRight)) LogSprint("STUCK right", 0f);
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
        // REMOVED (root only pass): if (visualRoot == null) yield break;

        if (!GroundArea.CanJumpOverPoint(groundPosition)) yield break;

        isJumping = true;
        float startElevation = elevation;
        float elapsedTime = 0f;

        // Reach is decided by the area being jumped FROM - the destination is not
        // known until the jump lands.
        float effectiveMaxStepUp = GroundArea.MaxStepUpFor(currentArea);

        float scale = scaleSpeedWithSize ? currentScale / referenceScale : 1f;
        float minRise = tapJumpHeight * scale;

        LogHeight("takeoff");

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / jumpDuration;

            // Rises fast at first and slows near the top - that is all Sin does here.
            jumpYOffset = Mathf.Sin(t * Mathf.PI * 0.5f) * jumpHeight * scale;

            // REMOVED (root only pass): the shadow lookup and ApplyHeights() that
            // were here. This loop now only changes the number; PlaceRoot draws it.

            // ADDED (unnested shadow pass): which ground is below right now - the
            // highest tile this jump could reach, or null over a gap. Same lookup
            // PlayerControl3 used, so the shadow snaps onto a platform as you pass
            // over it. PlaceShadow does the drawing.
            shadowGround = TileStep.JumpTarget(groundPosition, startElevation, effectiveMaxStepUp);

            // Released and past the minimum - stop rising and let the descent take over.
            if (!Input.GetKey(KeyCode.Space) && jumpYOffset >= minRise) break;

            yield return null;
        }

        // The hand-off. elevation goes UP by jumpYOffset and jumpYOffset goes DOWN
        // to 0, so (elevation + jumpYOffset) - what PlaceRoot reads - is unchanged.
        elevation = startElevation + jumpYOffset;
        jumpYOffset = 0f;
        LogHeight("apex-resolved");
        isJumping = false;

        // The ascent's speed at the moment it ended, carried into the fall so a
        // short hop does not stop dead for a frame. Negative because the fall
        // SUBTRACTS speed from elevation - still rising = negative downward speed.
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

        // Read once from the area the fall STARTED on.
        bool useDropTarget = currentArea == null || currentArea.dropTarget;
        float effectiveFallFloor = GroundArea.FallFloorFor(currentArea);
        float effectiveMaxStepUp = GroundArea.MaxStepUpFor(currentArea);

        float speed = entrySpeed;

        while (true)
        {
            speed += fallAcceleration * Time.deltaTime;

            float previousHeight = elevation;
            elevation -= speed * Time.deltaTime;

            GroundArea tile = TileStep.JumpTarget(groundPosition, fromElevation, effectiveMaxStepUp);

            if (useDropTarget && tile != null && tile.ElevationAt(groundPosition) > previousHeight)
                tile = TileStep.DropTarget(groundPosition, previousHeight);

            // REMOVED (root only pass): shadowGround / gapTime / SetShadowVisible.
            // ADDED (unnested shadow pass): shadowGround is back - the tile the fall
            // is heading for, or null over a gap. PlaceShadow does the rest.
            shadowGround = tile;

            float tileHeight = (tile != null) ? tile.ElevationAt(groundPosition) : 0f;

            bool crossed = tile != null && previousHeight > tileHeight && elevation <= tileHeight;

            if (crossed)
            {
                isFalling = false;
                SetArea(tile);
                LogHeight($"land-on-{tile.name}");
                yield break;
            }

            if (fallGuard != null && elevation <= fallGuard.ElevationAt(groundPosition))
            {
                isFalling = false;
                SetArea(fallGuard);
                LogHeight($"land-on-fallguard-{fallGuard.name}");
                yield break;
            }

            if (elevation < effectiveFallFloor)
            {
                isFalling = false;
                // Moving the footprint is enough - SetArea restores the height and
                // the next ApplyMovement draws the root there.
                groundPosition = lastSafePosition;
                SetArea(lastSafeArea);
                LogHeight("land-on-fallfloor-recovery");
                yield break;
            }

            // REMOVED (root only pass): ApplyHeights(). PlaceRoot draws the fall.
            yield return null;
        }
    }


    private void AdjustPlayerScale()
    {
        // Skip the recompute while frozen, so the scale holds steady.
        if (scaleIsFrozen) return;

        // Depth comes from the footprint, not the lifted root - otherwise standing
        // on a platform would shrink the player as if they walked toward the back.
        float rawScale = PlayerScale * (PlayerRatio - groundPosition.y);
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