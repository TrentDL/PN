// TestEnemy.cs
//
// ADDED (contact damage pass):
//   * ADDED: contactDamage - how much health one touch removes. Inspector-tunable,
//     private to this script (same pattern as PlayerHealth.maxHealth).
//   * ADDED: OnTriggerEnter2D - Unity calls this ONCE when another collider first
//     overlaps this trigger. Enter, not Stay: Stay runs every physics step, which
//     would drain all health in a fraction of a second while touching.
//   * The damage itself is NOT done here. This script only ASKS PlayerHealth to take
//     damage through its public TakeDamage method - PlayerHealth still owns the
//     number and its rules (clamp, no damage when dead). Encapsulation stays intact.
//   * ADDED (TEMP): a log of what touched the trigger, so a missing hit can be told
//     apart from "hit something that has no PlayerHealth". Remove after the DoD passes.

using UnityEngine;

public class TestEnemy : MonoBehaviour
{
    // ADDED (contact damage pass)
    [Tooltip("Health removed each time the player starts touching this collider.")]
    [SerializeField] private int contactDamage = 1;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // ADDED (contact damage pass): 'other' is the collider that walked in.
    // GetComponentInParent searches that object AND its parents, so the player's
    // collider can sit on a child (e.g. GroundPoint) while PlayerHealth sits on the
    // root. Anything without PlayerHealth (props, other enemies) is simply ignored.
    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"[{name}] touched by {other.name}", this);   // TEMP (contact check)

        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health != null) health.TakeDamage(contactDamage);
    }
}