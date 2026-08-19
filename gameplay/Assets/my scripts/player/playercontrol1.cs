// PlayerControlLegacy.cs
// Attach to the Player root object. The Live2D model stays a child.
//
// CHANGED vs the previous version:
//   * THE FLIP IS NOW ANIMATED, NOT SNAPPED. Same clip, same two keyframes, but
//     instead of jumping straight to frame 0 or 13, a 0..1 value walks toward the
//     target and the clip is seeked to the frame in between each update. Half
//     turned poses are expected and fine.
//   * ADDED: flipDuration - seconds for a full left-to-right turn.
//   * REPLACED: the facingRight bool is gone. flipTarget carries the same intent
//     (0 = left, 1 = right) and flipT carries how far along the turn actually is,
//     which the bool couldn't express.
//   * The seek stops running once flipT reaches flipTarget, so a standing
//     character costs nothing.
//
// NOTE: the flip clip's keys must NOT be stepped/constant tangents anymore. The
// in-between frames have to interpolate or the turn will still look like a snap.

using System.Collections;
using UnityEngine;


public class PlayerControlLegacy : MonoBehaviour
{
    static float moveSpeed = 5f, moveAccuracy = 0.15f;

    #region movement fields/properties

    [Header("References")]
    [Tooltip("Live2D model root. Gets lifted during jumps.")]
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
    // ADDED: the whole point of this version - how long the turn takes.
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
    [Tooltip("Maximum height the visual rises, in local units of visualRoot's parent")]
    public float jumpHeight = 2f;
    [Tooltip("How long the jump takes to complete")]
    public float jumpDuration = 0.5f;
    public GameObject playerShadow;

    private float jumpYOffset = 0f;
    private bool isJumping = false;
    private Vector3 shadowStartScale;
    private Vector3 visualStartLocalPos;

    private float currentScale = 1f;

    [Header("Boundary Settings")]
    public bool enableBoundaryChecking = true;

    #endregion


    void Start()
    {
        rb = GetComponent<Rigidbody2D>();   // fine if null

        if (visualRoot == null)
        {
            Debug.LogError("PlayerControlLegacy: visualRoot is not assigned. " +
                           "Drag the Live2D model child onto it - jumping does nothing without it.", this);
        }
        else
        {
            visualStartLocalPos = visualRoot.localPosition;
            visualRoot.localRotation = Quaternion.identity;   // undo any flip left over from the old rotation method
        }

        if (animator == null)
        {
            Debug.LogError("PlayerControlLegacy: animator is not assigned. Facing will not change.", this);
        }
        else
        {
            flipStateHash = Animator.StringToHash(flipStateName);
            animator.speed = 0f;   // the clip never plays itself - this script owns its time
            ApplyFacing();         // land on the left pose before the first input
        }

        if (enableBoundaryChecking && BoundsManager.Instance == null)
        {
            Debug.LogWarning("PlayerControlLegacy: Boundary checking enabled but no BoundsManager found in scene!", this);
        }

        if (playerShadow != null)
        {
            shadowStartScale = playerShadow.transform.localScale;
        }
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

        if (Input.GetKeyDown(KeyCode.Space) && !isJumping)
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


    // Single movement path, used by either Update or FixedUpdate.
    private void ApplyMovement(float deltaTime)
    {
        Vector2 current = (rb != null) ? rb.position : (Vector2)transform.position;
        Vector2 desired = current + moveVelocity * deltaTime;

        if (enableBoundaryChecking && BoundsManager.Instance != null)
        {
            if (!BoundsManager.Instance.IsWithinBounds(desired))
                desired = BoundsManager.Instance.ClampToBounds(desired);
        }

        // Extra zones - props, obstacles, sub-areas. Runs after the global area
        // so BoundsManager stays authoritative for the overall walkable region.
        if (enableBoundaryChecking)
        {
            if (!BoundsZone.IsWithinBounds(desired, "player"))
                desired = BoundsZone.ClampToBounds(desired, "player");
        }

        if (rb != null)
            rb.MovePosition(desired);
        else
            transform.position = new Vector3(desired.x, desired.y, transform.position.z);
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


    // CHANGED: no longer an early-out on "same side as last time". The turn takes
    // time now, so this has to keep running until flipT catches up to flipTarget.
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


    // CHANGED: seeks to the frame flipT lands on, not to leftFrame or rightFrame.
    // Single place the clip is seeked, shared by Start and UpdateFacing (DRY).
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
    private IEnumerator JumpCoroutine()
    {
        if (visualRoot == null) yield break;

        isJumping = true;
        float elapsedTime = 0f;

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / jumpDuration;

            jumpYOffset = Mathf.Sin(t * Mathf.PI) * jumpHeight
                * (scaleSpeedWithSize ? currentScale / referenceScale : 1f);

            Vector3 lp = visualStartLocalPos;
            lp.y = visualStartLocalPos.y + jumpYOffset;
            visualRoot.localPosition = lp;

            if (playerShadow != null && jumpHeight > 0f)
            {
                float shadowScale = 1f - (jumpYOffset / jumpHeight) * 0.1f;
                playerShadow.transform.localScale = shadowStartScale * shadowScale;
            }

            yield return null;
        }

        jumpYOffset = 0f;
        isJumping = false;
        visualRoot.localPosition = visualStartLocalPos;

        if (playerShadow != null)
        {
            playerShadow.transform.localScale = shadowStartScale;
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


    // Renamed from PlayerMove so the signature can never collide with the old
    // PlayerControl declaration again (CS0111).
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