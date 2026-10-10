// Dash.cs
//
// Add-on to PlayerControl3. Lives on the SAME GameObject as PlayerControl3.
//
// CHUNK 1 (input): Shift is detected once per press and logged.
//   * REMOVED: using System.Runtime.InteropServices - unused.
//
// CHUNK 2 (timing): a dash has a length and a cooldown.
//   * ADDED: dashDuration, dashCooldown, and the read-only IsDashing property.
//   * ADDED: EndDash(). The cooldown starts when the dash ENDS.
//
// CHUNK 3 (movement): the dash now moves the player.
//   * ADDED: Velocity - the ONLY thing PlayerControl3 reads. Dash decides the
//     speed and direction; PlayerControl3 still does the actual moving, so walls,
//     EdgeZones, pits and lockMovementInside all still apply to a dash.
//   * ADDED: the direction is captured ONCE, at the press, and locked for the
//     whole dash.
//   * ADDED: depthSpeedRatio - up/down is depth, and walking already moves at
//     half speed in depth (moveYspeed 2.5 vs moveXspeed 5).
//   * No direction held = no dash for now. Chunk 4 makes it use the facing.
//   * ADDED: [DefaultExecutionOrder(-10)] so Dash.Update always runs BEFORE
//     PlayerControl3.Update.
//
// CHUNK 5 (air rules): a toggle that blocks STARTING a dash in the air.
//   * ADDED: allowDashWhileAirborne. Off = Shift does nothing while jumping or
//     falling.
//   * ADDED: player - cached once in Start (GetComponent is a search, so it is
//     not repeated every press). MAY BE NULL: without PlayerControl3 there is no
//     jump to check, so the rule is simply skipped.
//   * Reads player.IsAirborne, NOT a jumping flag alone. WHY: a jump is two
//     coroutines - JumpCoroutine rises, then FallCoroutine comes down - and
//     isJumping is only true for the rise. Checking it alone would let a dash
//     through on the way down.

using UnityEngine;

[DefaultExecutionOrder(-10)]
public class Dash : MonoBehaviour
{
    #region fields and properties

    [Header("Dash Settings")]
    [Tooltip("Key that starts a dash.")]
    public KeyCode dashKey = KeyCode.LeftShift;

    [Tooltip("Units per second while dashing. Walk speed is 5 (moveXspeed) for comparison.")]
    public float dashSpeed = 15f;

    [Tooltip("Multiplier on the up/down part of the dash. 0.5 matches moveYspeed / moveXspeed.")]
    public float depthSpeedRatio = 0.5f;

    [Tooltip("Seconds a dash lasts.")]
    public float dashDuration = 0.2f;

    [Tooltip("Seconds after a dash ENDS before another can start.")]
    public float dashCooldown = 0.5f;

    [Header("Air Rules")]
    // ADDED (chunk 5): the toggle.
    [Tooltip("On = Shift works mid-jump and mid-fall. Off = dashing is ground-only.")]
    public bool allowDashWhileAirborne = false;

    // Other scripts can READ these; only Dash can change them.
    public bool IsDashing { get; private set; }
    public Vector2 Velocity { get; private set; }

    private PlayerControl3 player;      // ADDED (chunk 5): MAY BE NULL - always guard

    private float dashTimer = 0f;       // time left in the current dash
    private float cooldownTimer = 0f;   // time left before the next dash is allowed

    #endregion


    // ADDED (chunk 5): Start came back - there is now something to set up.
    void Start()
    {
        player = GetComponent<PlayerControl3>();   // fine if null
    }


    void Update()
    {
        // Tick the timers FIRST so a press on the frame a dash ends is judged
        // against up-to-date state.
        if (IsDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f) EndDash();
        }
        else if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }

        // GetKeyDown = true for ONE frame per press, so holding Shift
        // does not fire a dash every frame.
        if (Input.GetKeyDown(dashKey))
        {
            DashLogic();
        }
    }


    private void DashLogic()
    {
        if (IsDashing || cooldownTimer > 0f)
        {
            Debug.Log($"Dash blocked (dashing={IsDashing}, cooldown left={cooldownTimer:F2})", this);
            return;
        }

        // ADDED (chunk 5): the air rule. Checked before the direction so a
        // blocked press costs nothing - no cooldown is spent on it.
        if (!allowDashWhileAirborne && player != null && player.IsAirborne)
        {
            Debug.Log("Dash blocked: airborne", this);
            return;
        }

        // Same input PlayerControl3 reads, taken once and locked in.
        Vector2 dir = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;

        if (dir == Vector2.zero)
        {
            Debug.Log("Dash ignored: no direction held (chunk 4 adds facing).", this);
            return;
        }

        Velocity = new Vector2(dir.x * dashSpeed, dir.y * dashSpeed * depthSpeedRatio);
        IsDashing = true;
        dashTimer = dashDuration;
        Debug.Log($"Dash START at {Time.time:F2} velocity={Velocity}", this);
    } //end of function >:d


    private void EndDash()
    {
        IsDashing = false;
        Velocity = Vector2.zero;
        cooldownTimer = dashCooldown;
        Debug.Log($"Dash END at {Time.time:F2}", this);
    } //end of function >:d
}