using UnityEngine;
using TMPro;

public class PlayerHealth : MonoBehaviour
{

    // CHANGED (step 1): was   private int myHealth = 3;
    [Tooltip("Health this character starts with and can heal back up to.")]
    [SerializeField] private int maxHealth = 3;

    [Tooltip("TextMesh that displays health. Leave empty for no display.")]
    [SerializeField] private TMP_Text healthText;

    // ADDED (step 1): the live value. Set in Awake, changed ONLY by SetHealth.
    private int currentHealth;

    [Header("DEBUG SETTINGS")]
    public bool Godmode = false;

    // ADDED (step 1): read-only views for other scripts. "=>" means "this property
    // just returns this expression" - there is no setter, so nobody outside can write.
    public int Current => currentHealth;
    public int Max => maxHealth;
    public bool IsDead => currentHealth <= 0;


    // ADDED (step 1): Awake runs before any Start, so health is already full by the
    // time another script (the display in step 2) reads it in its own Start.
    void Awake()
    {
        currentHealth = maxHealth;
    }


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // CHANGED (step 1): no per-frame health work - see header. Safe to delete
        // Start/Update entirely once you are happy with that; kept for now.
        // PlayerHealthLogic();
    }


    // ADDED (step 1): public entry point for anything that hurts this character.
    // Negative or zero amounts are ignored so a bad value cannot secretly heal.
    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead) return;
        SetHealth(currentHealth - amount);
    }


    // ADDED (step 1): the mirror of TakeDamage. The dead cannot be healed - a respawn
    // or revive would be its own method, because it is a different rule.
    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        SetHealth(currentHealth + amount);
    }


    // CHANGED (step 1): was the empty PlayerHealthLogic(). Renamed because it now has
    // one specific job: the ONLY line in the project that writes currentHealth.
    // Mathf.Clamp keeps the value between 0 and maxHealth no matter what is passed.
    private void SetHealth(int value)
    {
        currentHealth = Mathf.Clamp(value, 0, maxHealth);

        if (healthText != null) healthText.text = $"{currentHealth}/{maxHealth}";

        Debug.Log($"[{name}] health {currentHealth}/{maxHealth}", this);   // TEMP (step 1 check)
    }


    // TEMP (step 1 check): right-click the component header in the Inspector during
    // Play mode to run these. Delete both once real damage sources exist.
    [ContextMenu("TEST: Take 1 Damage")]
    private void TestDamage() => TakeDamage(1);

    [ContextMenu("TEST: Heal 1")]
    private void TestHeal() => Heal(1);

}