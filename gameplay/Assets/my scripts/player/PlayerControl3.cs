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
             "MUST exceed your tallest tile's elevation or the model never crests it.")]
    public float jumpHeight = 2f;
    // CHANGED (v13): this is now the RISE time only. The descent is FallCoroutine's,
    // and its length comes from the distance it actually has to cover.
    [Tooltip("How long the ASCENT takes. The fall is separate and self-timing.")]
    public float jumpDuration = 0.5f;
    [Tooltip("Must be a CHILD of this object so it follows the player horizontally.")]
    public GameObject playerShadow;

    // CHANGED (v7): was CurrentHeight, a property used to test the jump arc against a
    // surface. Tile stepping decides by crossing test instead, so the arc's height is
    // cosmetic and nothing reads it. This is the reach limit.
    [Tooltip("How many elevation units a single jump can climb. A tile higher than " +
             "this above you cannot be reached, however you time the jump.")]
    public float maxStepUp = 1.5f;

    // CHANGED (v13): was fallSpeed, a flat rate. An acceleration reads as gravity and
    // makes a long drop visibly faster than a short one without any extra tuning.
    [Tooltip("Elevation units per second squared. Higher = heavier.")]
    public float fallAcceleration = 20f;

    // ADDED (v13): how far below zero the player can fall before the fall gives up.
    // Set below your lowest tile. This is where pit handling will hook in.
    [Tooltip("Elevation at which a fall ends with nothing underneath. Set below your lowest tile.")]
    public float fallFloor = -5f;

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

                // CHANGED (v13): FallCoroutine finds its own landing now, so it only
                // needs the height we are falling from.
                if (drop != null) StartCoroutine(FallCoroutine(elevation));
                else if (currentArea != null) desired = currentArea.ClampInside(desired);
            }
            else if (ahead != currentArea)
            {
                // CHANGED (v10): was an unconditional SetArea, which snapped the player
                // down whenever JumpTarget returned a LOWER tile. JumpTarget accepts
                // anything at or below your height, so stepping off a ledge onto the
                // overlapping floor took this branch instead of the fall branch.
                if (ahead.elevation < elevation)
                    StartCoroutine(FallCoroutine(elevation));   // it is a drop
                else
                    SetArea(ahead);                             // same height - snapping is correct
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
    // CHANGED (v13): this is now the ASCENT ONLY. The descent belongs to FallCoroutine,
    // so there is exactly ONE piece of code that brings the player down - jump, walk-off
    // and pit all share it (DRY). jumpDuration is now the rise time alone, and no longer
    // has to be tuned against jumpHeight, because the fall's length comes from the
    // distance it actually has to cover.
    private IEnumerator JumpCoroutine()
    {
        if (visualRoot == null) yield break;

        isJumping = true;                       // ApplyMovement reads this - clamp is off while true
        float startElevation = elevation;       // height we are leaving from
        float elapsedTime = 0f;

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / jumpDuration;

            // CHANGED (v13): was Sin(t * PI), a full up-and-down arc. Sin(t * PI/2)
            // rises to the peak and stops there, easing out as it arrives.
            jumpYOffset = Mathf.Sin(t * Mathf.PI * 0.5f) * jumpHeight
                * (scaleSpeedWithSize ? currentScale / referenceScale : 1f);

            // The shadow's gap check needs to know what is underneath right now, so it
            // vanishes over a pit and returns over solid ground.
            GroundArea under = TileStep.JumpTarget(transform.position, startElevation, maxStepUp);
            gapTime = (under == null) ? gapTime + Time.deltaTime : 0f;
            SetShadowVisible(gapTime <= shadowHideDelay);

            // CHANGED: one call now covers model position, shadow position and
            // shadow scale. The inline shadow block that used to live here is gone.
            ApplyHeights();

            yield return null;
        }

        // ADDED (v13): apex reached. Fold the arc into elevation so the handoff happens
        // at the exact height the feet are already at - no pop - then let the fall take
        // over. REMOVED with it: the landing block, the crossing test and the
        // DropTarget/takeoffArea fallbacks, which all live in FallCoroutine now.
        elevation = startElevation + jumpYOffset;
        jumpYOffset = 0f;
        isJumping = false;

        StartCoroutine(FallCoroutine(startElevation));
    }


    // CHANGED (v13): the ONE descent. Used by the walk-off drop, by the end of a jump's
    // ascent, and by anything else that leaves the player airborne.
    // The target is no longer passed in - the fall finds it by crossing test, because a
    // jump's descent can pass through a tile the takeoff position knew nothing about.
    //
    // 'fromElevation' is the height the JUMP started at, which is what maxStepUp is
    // measured against. For a walk-off it is just the current elevation.
    private IEnumerator FallCoroutine(float fromElevation)
    {
        isFalling = true;                       // ApplyMovement reads this - no drop checks while true

        float speed = 0f;

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

            if (tile != null && previousHeight > tile.elevation && elevation <= tile.elevation)
            {
                isFalling = false;
                SetArea(tile);      // snaps to exactly the tile's height and restores the shadow
                yield break;
            }

            // ADDED (v13): nothing caught us and we are below every tile here - a pit.
            // fallFloor is the level the scene's base ground sits at.
            if (elevation < fallFloor)
            {
                isFalling = false;
                SetArea(null);      // elevation 0, no area - movement is unbounded until landing
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