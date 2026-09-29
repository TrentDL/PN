// Dash.cs
//
// Add-on to PlayerControl3. Lives on the SAME GameObject as PlayerControl3.
//
// CHUNK 1 (input): Shift is detected once per press and logged. Nothing moves yet.
//   * REMOVED: using System.Runtime.InteropServices - unused.
//   * REMOVED: empty Start() - nothing to set up yet.
//
// CHUNK 2 (timing): a dash now has a length and a cooldown. Still nothing moves.
//   * ADDED: dashDuration, dashCooldown, and the read-only IsDashing property.
//     PlayerControl3 will read IsDashing in chunk 3 - "private set" means only
//     this script can start or stop a dash.
//   * ADDED: EndDash(). The cooldown starts when the dash ENDS, not when it
//     starts, so dashCooldown is the true gap between two dashes.
//   * Timers count DOWN to 0 in Update - no coroutine needed for two numbers.

using UnityEngine;

public class Dash : MonoBehaviour
{
    #region fields and properties

    [Header("Dash Settings")]
    [Tooltip("Key that starts a dash.")]
    public KeyCode dashKey = KeyCode.LeftShift;

    [Tooltip("Not used yet - wired up in chunk 3.")]
    public float dashSpeed = 0f;

    [Tooltip("Seconds a dash lasts.")]
    public float dashDuration = 0.2f;

    [Tooltip("Seconds after a dash ENDS before another can start.")]
    public float dashCooldown = 0.5f;

    // Other scripts can READ this; only Dash can change it.
    public bool IsDashing { get; private set; }

    private float dashTimer = 0f;       // time left in the current dash
    private float cooldownTimer = 0f;   // time left before the next dash is allowed

    #endregion


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

        IsDashing = true;
        dashTimer = dashDuration;
        Debug.Log($"Dash START at {Time.time:F2}", this);
    } //end of function >:d


    private void EndDash()
    {
        IsDashing = false;
        cooldownTimer = dashCooldown;
        Debug.Log($"Dash END at {Time.time:F2}", this);
    } //end of function >:d
}