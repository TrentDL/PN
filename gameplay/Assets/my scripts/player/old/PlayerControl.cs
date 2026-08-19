// PlayerControl.cs
// Put this on Player object (the root). The Live2D model ("objects") stays a child.
//
// CHANGED vs your version:
//   * Rigidbody2D is now OPTIONAL. No RB = Transform movement in Update.
//   * SpriteRenderer lookups removed. Cubism models have no SpriteRenderer;
//     assign the model root to "visualRoot" in the Inspector instead.
//   * Facing flip is a Y-rotation on visualRoot, so it can't fight root scaling.
//   * Jump is purely visual now, so you can move while airborne.

using System.Collections;
using UnityEngine;


public class PlayerControl : MonoBehaviour
{
    static float moveSpeed = 5f, moveAccuracy = 0.15f;

    #region movement fields/properties

    [Header("References")]
    [Tooltip("Live2D model root (drag 'objects' here). Gets lifted during jumps and flipped for facing.")]
    public Transform visualRoot;

    // [Tooltip("Animator on the Live2D model. Drives idle/RightIdle via the facingLeft bool.")]
    // public Animator animator;

    // private bool FacingLeft = false;   // false = facing left, matches the default 'idle' state


    [Tooltip("Animator on the left-facing model.")]
    public Animator leftAnimator;
    [Tooltip("Animator on the right-facing model.")]
    public Animator rightAnimator;

    private Animator ActiveAnimator => facingRight ? rightAnimator : leftAnimator;


    [Header("Facing")]

    private bool facingRight = false;   // false = facing left

    [Tooltip("Left-facing model instance.")]
    public GameObject leftModel;
    [Tooltip("Right-facing model instance.")]
    public GameObject rightModel;

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

    // [SerializeField] private float flipOffsetX = 0f; //unused 

    // private Vector3 basePosition;
    
    private float currentScale = 1f;


    [Header("Boundary Settings")]
    public bool enableBoundaryChecking = true;

    #endregion 

    void Start()
    {
        /* Application.targetFrameRate = 30; //CHANGE FRAME RATE DEBUG */

        rb = GetComponent<Rigidbody2D>();   // fine if null

        if (visualRoot == null)
        {
            Debug.LogError("PlayerControl: visualRoot is not assigned. " +
                           "Drag the Live2D model child onto it - jumping and flipping do nothing without it.", this);
        }
        else
        {
            visualStartLocalPos = visualRoot.localPosition;
            visualRoot.localRotation = Quaternion.identity;   // undo any flip left over from the old method
        }



        if (enableBoundaryChecking && BoundsManager.Instance == null)
        {
            Debug.LogWarning("PlayerControl: Boundary checking enabled but no BoundsManager found in scene!", this);
        }

        if (playerShadow != null)
        {
            shadowStartScale = playerShadow.transform.localScale;
        }

        if (leftModel  != null) leftModel.SetActive(!facingRight);
        if (rightModel != null) rightModel.SetActive(facingRight);
        


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


      private void ApplyScaleToSpeed()
    {
        if (!scaleSpeedWithSize || referenceScale <= 0f) return;
        moveVelocity *= currentScale / referenceScale;
    }

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

    //     private void Awake()
    // {
    //     if (visualRoot != null)
    //         basePosition = visualRoot.localPosition;
    // }

#region Old updating facing code
    // // Cubism has no flipX. Rotating 180 on Y mirrors the model without touching scale.
    // // REQUIRES CubismRenderController -> Sorting -> Mode = BackToFrontOrder.
    // // The ...Z modes sort drawables by local Z and will render the model inside-out when rotated.
    // private void UpdateFacing()
    // {
    //     if (visualRoot == null || moveInput.x == 0) return;

    //     bool facingLeft = moveInput.x < 0;
    //     visualRoot.localRotation = Quaternion.Euler(0f, facingLeft ? 180f : 0f, 0f);
    //     visualRoot.localPosition = basePosition + new Vector3(facingLeft ? flipOffsetX : 0f, 0f, 0f);

    //     // ALTERNATIVE if you must keep a Z-based sorting mode - mirror instead of rotate:
    //     // Vector3 s = visualRoot.localScale;
    //     // s.x = Mathf.Abs(s.x) * (facingLeft ? -1f : 1f);
    //     // visualRoot.localScale = s;
    // }
#endregion



    // Sets intent only. CubismFacingWriter.OnLateUpdate does the actual parameter
    // write, in a slot ordered by CubismUpdateExecutionOrder.
    private void UpdateFacing()
    {
        if (moveInput.x == 0) return;   // no horizontal input - keep last facing


        bool wantsRight = moveInput.x > 0;
        if (wantsRight == facingRight) return;
        facingRight = wantsRight;

        if (leftModel  != null) leftModel.SetActive(!facingRight);
        if (rightModel != null) rightModel.SetActive(facingRight);

        var anim = ActiveAnimator;
        if (anim != null) anim.SetBool("IsGrounded", !isJumping);
        
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

        var jumpAnim = ActiveAnimator;
        if (jumpAnim != null) jumpAnim.SetBool("IsGrounded", false);

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
                float shadowScale = 1f - (jumpYOffset / jumpHeight) * 0.5f;
                playerShadow.transform.localScale = shadowStartScale * shadowScale;
            }

            yield return null;
        } //end of function

        jumpYOffset = 0f;
        isJumping = false;
        visualRoot.localPosition = visualStartLocalPos;

        if (playerShadow != null)
        {
            playerShadow.transform.localScale = shadowStartScale;
        }

        isJumping = false;

        var landAnim = ActiveAnimator;
        if (landAnim != null) landAnim.SetBool("IsGrounded", true);
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


    public IEnumerator PlayerMove(Transform myObject, Vector2 point)
    {
        Vector2 positionDifference = point - (Vector2)myObject.position;

        while (positionDifference.magnitude > moveAccuracy)
        {
            myObject.Translate(moveSpeed * positionDifference.normalized * Time.deltaTime);
            positionDifference = point - (Vector2)myObject.position;
            yield return null;
        }

        myObject.position = point;
    } //end of function >:D
}
