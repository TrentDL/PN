// PlayerControlShadowed.cs
//
// RENAMED: PlayerControlGrounded -> PlayerControlShadowed  (file must match the class)
// Works with the SAME GroundArea.cs - that script is unchanged.
//
// CHANGED vs PlayerControlGrounded - all of it about the shadow:
//   * ADDED: shadowStartLocalPos - the shadow's authored local position, so the
//     elevation offset can be added to it instead of overwriting it.
//   * CHANGED: the shadow is now lifted by `elevation`, so when you jump up onto
//     GroundBounds 2 the shadow rides up with you and sits on that ledge's
//     surface instead of staying stuck on the lower floor.
//   * ADDED (latest): the shadow object is deactivated while the player is over
//     a gap - nothing under it to cast onto. shadowHideDelay keeps it on across
//     narrow gaps so it does not flicker; only a "large" gap hides it.
//   * The shadow is NOT lifted by jumpYOffset - it stays planted on the ground
//     while the model arcs above it, which is what sells the jump. (If you want
//     it glued to the feet instead, see the one-line note in ApplyHeights.)
//   * RENAMED: ApplyVisualHeight() -> ApplyHeights(). It now owns the shadow's
//     position and scale too, so there is exactly one place the jump/elevation
//     values reach the visuals (DRY). The duplicate shadow-reset block at the
//     end of JumpCoroutine is gone because of it.
//
// Everything else (facing, sprint, area clamping, scale-with-depth) is unchanged.

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
             "MUST exceed your tallest tile's elevation or the model passes through the ledge.")]
    public float jumpHeight = 2f;
    [Tooltip("How long the jump takes to complete")]
    public float jumpDuration = 0.5f;
    [Tooltip("Must be a CHILD of this object so it follows the player horizontally.")]
    public GameObject playerShadow;

    // CHANGED (v7): was CurrentHeight, a property used to test the jump arc against a
    // surface. Tile stepping decides at touchdown instead, so the arc's height is
    // cosmetic and nothing reads it. This is the reach limit, and since v9 it is
    // checked against where you LAND - it is the real difficulty knob.
    [Tooltip("How many elevation units a single jump can climb. A tile higher than " +
             "this above you cannot be reached, however you time the jump.")]
    public float maxStepUp = 1.5f;

    // ADDED (v8): the walk-off fall is a RATE, not a duration, so a one-tile step and
    // a long drop take different amounts of time instead of looking identical.
    [Tooltip("Elevation units fallen per second when walking off a ledge. " +
             "Higher = snappier. This is a rate, so a taller drop takes longer.")]
    public float fallSpeed = 6f;

    // ADDED: how long the player must be over open space before the shadow turns
    // off. This is what makes it a "large gap only" thing - crossing a hairline
    // seam between two grounds never reaches this, so no flicker. Raise it if
    // your grounds have wider seams than you want the shadow reacting to.
    [Tooltip("Seconds over open space before the shadow is deactivated. 0 = hide the instant there is no ground below.")]
    public float shadowHideDelay = 0.08f;

    private float gapTime = 0f;   // ADDED: how long we have had nothing beneath us

    // ADDED: test toggle for jump momentum. ON = speed carries through the jump
    // when you let go of the keys. OFF = releasing stops the player dead in the
    // air, which is the old behaviour. Flip it in the Inspector while playing.
    [Tooltip("Keep moving at takeoff speed if the movement keys are released mid-jump. Off = hard stop in the air.")]
    public bool JumpCarryOver = true;

    // ADDED: the velocity the jump started with, refreshed every grounded frame.
    private Vector2 jumpMomentum;

    private float jumpYOffset = 0f;
    private bool isJumping = false;

    // ADDED (v8): true while the walk-off fall is playing. Separate from isJumping
    // because the two can never overlap and each guards different things.
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

    // Visual lift from currentArea.elevation, kept separate from jumpYOffset so the
    // jump arc can be added on top without either overwriting the other.
    private float elevation = 0f;

    #endregion


    void Start()
    {
        rb = GetComponent<Rigidbody2D>();   // fine if null

        if (visualRoot == null)
        {
            Debug.LogError("PlayerControlShadowed: visualRoot is not assigned. " +
                           "Drag the Live2D model child onto it - jumping does nothing without it.", this);
        }
        else
        {
            visualStartLocalPos = visualRoot.localPosition;
            visualRoot.localRotation = Quaternion.identity;   // undo any flip left over from the old rotation method
        }

        if (animator == null)
        {
            Debug.LogError("PlayerControlShadowed: animator is not assigned. Facing will not change.", this);
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
            Debug.LogWarning("PlayerControlShadowed: player did not start inside any GroundArea. " +
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

        //Debug.Log($"ApplyMovement running, isFalling={isFalling}, elevation={elevation}", this);
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
    // Must run AFTER ApplySprintSpeed and ApplyScaleToSpeed so the stored velocity
    // is the final one, sprint and perspective scale included.
    // Grounded: remember the current velocity as the takeoff speed.
    // Airborne with keys held: untouched, so you can still steer mid-air.
    // Airborne with keys released: velocity is restored to the takeoff speed.
    private void JumpPhysics()
    {
        if (!isJumping)
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
        // ledge edge. Battletoads drops you instead, so we now only clamp when there is
        // nothing below to fall onto.
        // CHANGED (v8): added !isFalling. Without it the drop check re-fires every
        // frame of the fall and restarts the coroutine, freezing the player mid-air.
        if (enableBoundaryChecking && !isJumping && !isFalling)
        {
            GroundArea ahead = TileStep.JumpTarget(desired, elevation, 0f);

            if (ahead == null)
            {
                // Stepped off the tile. Anything below catches us; otherwise this is a
                // real gap and we stop at the edge.
                GroundArea drop = TileStep.DropTarget(desired, elevation);

                // CHANGED (v8): was SetArea(drop), which snapped the model to the new
                // height in a single frame. Now it plays out over time.
                if (drop != null) StartCoroutine(FallCoroutine(drop, elevation));
                else if (currentArea != null) desired = currentArea.ClampInside(desired);
            }
            else if (ahead != currentArea)
            {
                // CHANGED (v10): was an unconditional SetArea, which snapped the player
                // down whenever JumpTarget returned a LOWER tile. JumpTarget accepts
                // anything at or below your height, so stepping off a ledge onto the
                // overlapping floor took this branch instead of the fall branch.
                if (ahead.elevation < elevation)
                    StartCoroutine(FallCoroutine(ahead, elevation));   // it is a drop
                else
                    SetArea(ahead);                                    // same height - snapping is correct
            }

            // Extra zones - props, obstacles. Delete these two lines if your
            // project has no BoundsZone script.
            if (!BoundsZone.IsWithinBounds(desired, "player"))
                desired = BoundsZone.ClampToBounds(desired, "player");
        }

        if (rb != null)
            rb.MovePosition(desired);
        else
            transform.position = new Vector3(desired.x, desired.y, transform.position.z);
    }


    // RENAMED from ApplyVisualHeight - it drives the shadow as well now.
    // The one and only place jumpYOffset / elevation reach the visuals (DRY).
    // Model  = jump arc + ground height.
    // Shadow = ground height only, so it stays on the surface while the model
    //          arcs above it. To glue the shadow to the feet instead, add
    //          jumpYOffset to the line below.
    private void ApplyHeights()
    {
        if (visualRoot != null)
        {
            Vector3 lp = visualStartLocalPos;
            lp.y = visualStartLocalPos.y + jumpYOffset + elevation;
            visualRoot.localPosition = lp;
        }

        if (playerShadow == null) return;

        // ADDED: the shadow rides the ground height change.
        Vector3 sp = shadowStartLocalPos;
        sp.y = shadowStartLocalPos.y + elevation;
        playerShadow.transform.localPosition = sp;

        // MOVED here from JumpCoroutine so the shadow's whole state lives in one
        // function. Shrinks as the model rises, back to 1x when jumpYOffset is 0.
        float shadowScale = (jumpHeight > 0f) ? 1f - (jumpYOffset / jumpHeight) * 0.1f : 1f;
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


    // One place that adopts an area and its height (DRY - used by Start and by the
    // end of a jump).
    private void SetArea(GroundArea area)
    {
        currentArea = area;

        // CHANGED (v7): was GroundHeightMap.HeightAt. Tiles are discrete heights, so
        // the area's own elevation is the whole answer - no texture to sample.
        elevation = (area != null) ? area.elevation : 0f;

        ApplyHeights();   // CHANGED: was ApplyVisualHeight

        // ADDED: standing on ground always means a visible shadow, so landing (and
        // the fallback that puts us back on the takeoff ledge) restores it here.
        gapTime = 0f;
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


    // Visual only. The root keeps moving normally underneath, so you can steer mid-air.
    // Also decides which ground you land on and how high that ground is.
    private IEnumerator JumpCoroutine()
    {
        if (visualRoot == null) yield break;

        isJumping = true;                       // ApplyMovement reads this - clamp is off while true
        GroundArea takeoffArea = currentArea;   // where to put us back if we miss
        float startElevation = elevation;       // height we are leaving from
        float elapsedTime = 0f;

                while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / jumpDuration;

            // ADDED (v11): remember last frame's height before overwriting it. The
            // landing test needs both to detect a CROSSING.
            float previousHeight = startElevation + jumpYOffset;

            jumpYOffset = Mathf.Sin(t * Mathf.PI) * jumpHeight
                * (scaleSpeedWithSize ? currentScale / referenceScale : 1f);

            float currentHeight = startElevation + jumpYOffset;

            // ADDED (v11): Mario-style landing. Only while DESCENDING, and only if the
            // feet passed THROUGH the tile's surface between the last frame and this
            // one. A crossing test instead of an overlap test, so it cannot be missed
            // at low frame rates the way v1-v6 could - that was the whole problem.
            if (currentHeight < previousHeight)
            {
                GroundArea tile = TileStep.JumpTarget(transform.position, startElevation, maxStepUp);

                if (tile != null && previousHeight > tile.elevation && currentHeight <= tile.elevation)
                {
                    // Touched down early - end the jump here rather than running out
                    // the timer. This is what makes landing on a raised tile feel like
                    // a real contact instead of a scheduled event.
                    jumpYOffset = 0f;
                    isJumping = false;
                    SetArea(tile);
                    yield break;
                }
            }

            // ADDED (v9): the shadow's gap check still needs to know what is underneath
            // right now, so it vanishes over a pit and returns over solid ground.
            GroundArea under = TileStep.JumpTarget(transform.position, startElevation, maxStepUp);
            gapTime = (under == null) ? gapTime + Time.deltaTime : 0f;
            SetShadowVisible(gapTime <= shadowHideDelay);

            ApplyHeights();

            yield return null;
        }

         jumpYOffset = 0f;
        isJumping = false;

        // CHANGED (v11): reaching here means no surface was crossed during the arc -
        // we jumped off an edge. SetArea would snap; FallCoroutine eases us down.
        GroundArea landedOn = TileStep.JumpTarget(transform.position, startElevation, maxStepUp);
        if (landedOn == null) landedOn = TileStep.DropTarget(transform.position, startElevation);

        if (landedOn == null && takeoffArea != null)
        {
            Vector2 back = takeoffArea.ClampInside(transform.position);
            transform.position = new Vector3(back.x, back.y, transform.position.z);
            landedOn = takeoffArea;
        }

        if (landedOn != null && landedOn.elevation < elevation)
            StartCoroutine(FallCoroutine(landedOn, elevation));
        else
            SetArea(landedOn);
    }


      // ADDED (v8): the fall you get from WALKING off a ledge, as opposed to jumping.
    // Kept separate from JumpCoroutine because the shapes are genuinely different -
    // a jump is a symmetric arc up and back down, a fall is one-way and accelerating.
    // Sharing one coroutine would mean a mode flag threaded through every line.
    private IEnumerator FallCoroutine(GroundArea target, float fromElevation)
    {
        isFalling = true;                       // ApplyMovement reads this - no drop checks while true

        float toElevation = (target != null) ? target.elevation : 0f;
        float distance = fromElevation - toElevation;

        // Taller drops take longer, which is what makes a one-tile step and a long
        // fall feel like different events instead of the same animation stretched.
        float duration = (fallSpeed > 0f) ? distance / fallSpeed : 0f;
        float elapsedTime = 0f;

        // TEMPORARY DEBUG: delete once the snapping is diagnosed.
        // Logs ONCE per drop  -> the !isFalling guard is working, the problem is timing.
        // Logs REPEATEDLY     -> ApplyMovement is re-firing the drop branch every frame
        //                        and each new coroutine starts from an already-lowered
        //                        fromElevation, which collapses into a snap.
        // A duration at or near 0 also explains a snap on its own - the while loop
        // below never runs and SetArea fires in the same frame.
        Debug.Log($"Fall started: {fromElevation} -> {toElevation}, distance {distance}, duration {duration}", this);

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / duration;

            // Squared so it accelerates. Mathf.Lerp on its own is constant speed,
            // which reads as floating down rather than falling.
            elevation = Mathf.Lerp(fromElevation, toElevation, t * t);

            ApplyHeights();
            yield return null;
        }

        isFalling = false;
        SetArea(target);   // lands us exactly on the target height and restores the shadow
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