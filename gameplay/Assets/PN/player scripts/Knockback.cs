using UnityEngine;

public class Knockback : MonoBehaviour
{

    public bool GotHit = false;


     [Header("PLAYER HIT SETTINGS")]
     
     public int HitPower = 0;

     
    [Tooltip("Push speed, in the same units as PlayerControl4's moveXspeed.")]
    [SerializeField] private float knockbackSpeed = 6f;

    [Tooltip("Seconds the push lasts. Distance pushed = speed x duration.")]
    [SerializeField] private float knockbackDuration = 0.2f;


     [SerializeField] private Animator animator;

    private static readonly int HurtTrigger = Animator.StringToHash("Ouch1");


    private float timeLeft = 0f;

    public bool IsKnockedBack => timeLeft > 0f;
    public Vector2 Velocity { get; private set; }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsKnockedBack) return;

        timeLeft -= Time.deltaTime;

        // The push just ended this frame, so clear the hit flag.
        if (!IsKnockedBack) GotHit = false;
    }

   public void TakeHit(int power, float direction)
    {
        GotHit = true;
        HitPower = power;
        Velocity = new Vector2(Mathf.Sign(direction) * knockbackSpeed, 0f);
        timeLeft = knockbackDuration;   // a second hit mid-push simply restarts it
        animator.SetTrigger(HurtTrigger);
    }

    [ContextMenu("TEST: Hit from right (push left)")]
    private void TestHitLeft() => TakeHit(1, -1f);

    [ContextMenu("TEST: Hit from left (push right)")]
    private void TestHitRight() => TakeHit(1, 1f);




}
