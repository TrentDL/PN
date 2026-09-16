// AnimationZone.cs
//
// ADDED: a polygon that fires an Animator trigger when the player walks into it.
//
// DELIBERATELY UNRELATED to GroundArea / TileStep. It answers "is the player in
// here" and nothing about height, walkability or falling, so sharing a base class
// would only mean inheriting fields that must stay at their defaults.
//
// WHY NOT OnTriggerEnter2D: that needs a Rigidbody2D on the player or the zone.
// PlayerControl3 treats its Rigidbody2D as optional, so a physics callback would
// silently never fire on a setup without one. A point test always works.

using UnityEngine;
using Live2D.Cubism.Rendering;
using Live2D.Cubism.Core;        // CubismModel, CubismDrawable, CubismParameter
using Live2D.Cubism.Framework;   // CubismUpdateController and friends


[RequireComponent(typeof(PolygonCollider2D))]
public class AnimationZone : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Whose position is tested. Usually the player root.")]
    public Transform watched;

    [Tooltip("Animator that plays the clip. Can be on any object.")]
    public Animator target;

    [Tooltip("Trigger parameter name on that Animator.")]
    public string triggerName = "Play";

    [Header("Behaviour")]
    [Tooltip("ON: fires every time the player re-enters. OFF: fires once, ever.")]
    public bool repeatable = true;

        // ADDED: how long to keep the sort order after the player leaves. A fall carries
    // them out of a small polygon in a few frames, but the descent runs much longer -
    // without this they pop back in front while still visibly inside the pit.
    [Tooltip("Seconds to hold the sorting order after the player exits. Cover the full fall duration.")]
    public float restoreDelay = 1f;

    private float exitTime = -1f;   // when they left; negative means they are inside

    [Header("Debug Visualization")]
    public bool showDebugGizmos = true;

    private PolygonCollider2D area;
    private bool wasInside = false;   // edge detection - fire on ENTRY, not every frame

    // In AnimationZone, replacing Fire()'s SetTrigger call
    public CubismRenderer[] drawables;   // drag the specific art meshes in
    public int sortOrderInside = 10;


    // ADDED: what the drawables were before this zone touched them. Captured in Awake
    // rather than hardcoded, so the zone does not need to know the model's authored
    // order - it just puts back whatever was there.
    private int[] originalOrders;

    void Awake()
    {
        area = GetComponent<PolygonCollider2D>();

        originalOrders = new int[drawables.Length];
        for (int i = 0; i < drawables.Length; i++)
        originalOrders[i] = drawables[i].LocalSortingOrder;
    }// end of function >:D

    void Update()
    {
        bool inside = area.OverlapPoint(watched.position);

        if (inside && !wasInside)
        {
            SetOrder(sortOrderInside);
            exitTime = -1f;              // cancel any pending restore
        }
        else if (!inside && wasInside)
        {
            exitTime = Time.time;        // CHANGED: start the clock instead of restoring now
        }

        // ADDED: the delayed restore. Kept as a timer rather than a coroutine so a
        // re-entry can cancel it by simply resetting exitTime.
        if (exitTime >= 0f && Time.time - exitTime >= restoreDelay)
        {
            Restore();
            exitTime = -1f;
        }

        wasInside = inside;
    }// end of function >:D


     private void SetOrder(int order)
    {
        for (int i = 0; i < drawables.Length; i++)
            drawables[i].LocalSortingOrder = order;
    }// end of function >:D

    private void Restore()
    {
        for (int i = 0; i < drawables.Length; i++)
            drawables[i].LocalSortingOrder = originalOrders[i];
    }// end of function >:D


    private void Fire()
    {
        for (int i = 0; i < drawables.Length; i++)
            drawables[i].LocalSortingOrder = sortOrderInside;
    }// end of function >:D

    void OnDrawGizmos()
    {
        PolygonCollider2D poly = area != null ? area : GetComponent<PolygonCollider2D>();
        if (!showDebugGizmos || poly == null) return;

        // Magenta - not used by any GroundArea type, so these read as "not ground".
        Gizmos.color = Color.magenta;
        Gizmos.matrix = transform.localToWorldMatrix;

        for (int p = 0; p < poly.pathCount; p++)
        {
            Vector2[] path = poly.GetPath(p);
            for (int i = 0; i < path.Length; i++)
                Gizmos.DrawLine(path[i] + poly.offset, path[(i + 1) % path.Length] + poly.offset);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }// end of function >:D
}