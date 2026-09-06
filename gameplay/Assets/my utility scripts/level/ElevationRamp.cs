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
//      the right shape, but it cannot be authored in the Scene view, needs
//      Read/Write Enabled (an uncompressed CPU copy), and adds a sample to every
//      lookup. Two handles and a lerp covers slopes, which is what the art needs.
//
// KNOWN LIMIT: linear only. The height rises along ONE axis and is flat past either
//      end. A dome or a radial cap cannot be expressed - that would need a
//      "distance from centre" mode, which is about six lines here and no change
//      anywhere else, because GroundArea.ElevationAt is the only seam.
//
// KNOWN LIMIT: TileStep and FallCoroutine still treat a tile as ONE height. The
//      landing test samples this once per frame, so a player drifting sideways
//      mid-fall lands at the height under them that frame, not a continuously
//      resolved surface. Keep ramps shallow and this is invisible.

using UnityEngine;

[RequireComponent(typeof(GroundArea))]
public class ElevationRamp : MonoBehaviour
{
    [Header("Slope")]
    [Tooltip("Transform marking where the ramp is at its LOW height. Must be a DIFFERENT object to High End.")]
    public Transform lowEnd;

    [Tooltip("Transform marking where the ramp is at its HIGH height.")]
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
    // Both handles must sit on the FLOOR PLANE, at the polygon's depth: this measures
    // horizontal distance across the footprint, not height in the art.
    public float ElevationAt(Vector2 worldPosition)
    {
        if (lowEnd == null || highEnd == null) return lowElevation;

        Vector2 low = lowEnd.position;
        Vector2 axis = (Vector2)highEnd.position - low;

        float lengthSquared = axis.sqrMagnitude;

        // Guard: both handles in the same spot. Returning lowElevation makes the ramp
        // a no-op rather than a divide-by-zero, which is the safer failure - but it
        // is also silent, so check this first if a ramp appears to do nothing.
        if (lengthSquared < 0.0001f) return lowElevation;

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