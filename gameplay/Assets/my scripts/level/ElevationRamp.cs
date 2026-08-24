// ElevationRamp.cs
//
// ADDED: per-position elevation for one GroundArea. Add it alongside a GroundArea
// to turn that area's flat height into a slope.
//
// WHY A SEPARATE COMPONENT: most areas are flat, and GroundArea is already carrying
//      seven fields. Anything that reads elevation asks GroundArea, which asks this
//      only if it is present - so a flat area costs one null check and no new state.
//
// WHY A RAMP AND NOT A HEIGHT MAP: a texture answers a float per pixel, which is
//      the right shape, but it cannot be authored in the Scene view, has a fixed
//      pixel-to-world ratio, and adds a sample-and-threshold step to every lookup.
//      Two points and a lerp covers slopes, which is what the art actually needs.
//
// KNOWN LIMIT: TileStep and FallCoroutine still treat a tile as ONE height. The
//      landing test samples this once at the moment of the fall, so a player who
//      drifts sideways mid-fall lands at the height they were heading for, not the
//      height directly beneath them. Keep ramps shallow and this is invisible.

using UnityEngine;

[RequireComponent(typeof(GroundArea))]
public class ElevationRamp : MonoBehaviour
{
    [Header("Slope")]
    [Tooltip("World position where the ramp is at its LOW height.")]
    public Transform lowEnd;

    [Tooltip("World position where the ramp is at its HIGH height.")]
    public Transform highEnd;

    [Tooltip("Elevation at the low end. Usually the surrounding floor's height.")]
    public float lowElevation = 0f;

    [Tooltip("Elevation at the high end.")]
    public float highElevation = 1f;

    [Header("Debug Visualization")]
    public bool showDebugGizmos = true;

    /// <summary>Elevation at this world position, projected onto the low-to-high axis.</summary>
    // The projection is what makes this work for a polygon of any shape - the player
    // can be anywhere in the area, and only their distance ALONG the slope matters.
    public float ElevationAt(Vector2 worldPosition)
    {
        if (lowEnd == null || highEnd == null) return lowElevation;

        Vector2 low = lowEnd.position;
        Vector2 axis = (Vector2)highEnd.position - low;

        float lengthSquared = axis.sqrMagnitude;
        if (lengthSquared < 0.0001f) return lowElevation;   // guard: both ends in the same spot

        // Dot product over squared length gives 0..1 along the axis. Clamped, so a
        // point past either end holds that end's height instead of extrapolating.
        float t = Mathf.Clamp01(Vector2.Dot(worldPosition - low, axis) / lengthSquared);

        return Mathf.Lerp(lowElevation, highElevation, t);
    }// end of function >:D

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || lowEnd == null || highEnd == null) return;

        // Blue for the slope axis - distinct from every GroundArea colour, since this
        // draws on top of one and the two should not be confused.
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(lowEnd.position, highEnd.position);
        Gizmos.DrawSphere(lowEnd.position, 0.08f);
        Gizmos.DrawSphere(highEnd.position, 0.12f);   // bigger sphere marks the HIGH end
    }// end of function >:D
}