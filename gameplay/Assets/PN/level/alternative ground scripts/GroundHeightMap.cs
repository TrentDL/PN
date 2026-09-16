// GroundHeightMap.cs
//
// ADDED: per-point ground height for a GroundArea, sampled from a grayscale
// texture. Optional add-on - a GroundArea without one keeps using its flat
// 'elevation' float, so existing ground is untouched.
//
// WHY: 'elevation' is one number for a whole area, so a ramp or an uneven
//      surface needed a separate GroundArea per height. This samples a texture
//      instead, so one area can slope.
//
// REPLACES: PlatformBounds. The reach test is now a height comparison
//      (is the jump arc at least as high as the ground here?) rather than a
//      second collider, so platforms need no extra Inspector wiring.
//
// DEPTH WARNING: world Y is depth in this project. The GroundArea polygon is a
//      FOOTPRINT ON THE FLOOR - draw it where the object's base sits, never at
//      the height the player will stand. Height comes from this map, and it
//      moves the visual only. A polygon drawn up in the air makes the player
//      shrink and sort behind everything, because AdjustPlayerScale and
//      CubismDepthSort both read transform.position.y.
//
// TEXTURE: grayscale. Black = minHeight, white = maxHeight. Must have
//      Read/Write Enabled ticked in its import settings or GetPixelBilinear
//      throws. The texture is stretched across the collider's bounds, so the
//      image's aspect should roughly match the footprint's.

using UnityEngine;

[RequireComponent(typeof(GroundArea))]
public class GroundHeightMap : MonoBehaviour
{
    [Tooltip("Grayscale height map. Read/Write Enabled must be ON in import settings.")]
    public Texture2D heightMap;

    [Tooltip("Height where the map is black.")]
    public float minHeight = 0f;

    [Tooltip("Height where the map is white.")]
    public float maxHeight = 1f;

    private GroundArea area;
    private PolygonCollider2D footprint;   // supplies the bounds the map stretches over

    void Awake()
    {
        area = GetComponent<GroundArea>();
        footprint = GetComponent<PolygonCollider2D>();
    }// end of function >:D

    /// <summary>Ground height at a world point inside this area.</summary>
    // Falls back to the area's flat elevation if there is no usable map, so this
    // component is safe to add and leave empty.
    public float Sample(Vector2 worldPosition)
    {
        if (heightMap == null || footprint == null) return area.elevation;

        Bounds b = footprint.bounds;

        // Normalize the point into 0-1 across the footprint, which is how the
        // texture is mapped. Clamped so an edge point never samples off-texture.
        float u = Mathf.Clamp01(Mathf.InverseLerp(b.min.x, b.max.x, worldPosition.x));
        float v = Mathf.Clamp01(Mathf.InverseLerp(b.min.y, b.max.y, worldPosition.y));

        // Bilinear so the surface is smooth instead of stepping at pixel edges.
        float grey = heightMap.GetPixelBilinear(u, v).grayscale;

        return Mathf.Lerp(minHeight, maxHeight, grey);
    }// end of function >:D

    /// <summary>Ground height under a world point. Flat elevation if the area has no map.</summary>
    // ADDED: the one lookup callers use (DRY). Keeps the "has a map?" branch in a
    // single place instead of at every call site.
    public static float HeightAt(GroundArea groundArea, Vector2 worldPosition)
    {
        if (groundArea == null) return 0f;

        GroundHeightMap map = groundArea.GetComponent<GroundHeightMap>();
        return (map != null) ? map.Sample(worldPosition) : groundArea.elevation;
    }// end of function >:D

    /// <summary>
    /// Which ground the player is on, given how high their jump currently is.
    /// </summary>
    // REPLACES PlatformBounds.AreaAt / Target / Land. A raised surface is only
    // entered when the arc is at least as high as that surface, so walking under
    // the tooth no longer puts you on top of it. Highest reachable wins.
    public static GroundArea AreaAt(Vector2 worldPosition, float currentHeight)
    {
        GroundArea best = null;
        float bestHeight = 0f;

        for (int i = 0; i < GroundArea.All.Count; i++)
        {
            GroundArea a = GroundArea.All[i];
            if (!a.Contains(worldPosition)) continue;

            float height = HeightAt(a, worldPosition);
            if (height > currentHeight) continue;              // arc is below this surface

            if (best == null || height > bestHeight)
            {
                best = a;
                bestHeight = height;
            }
        }

        return best;
    }// end of function >:D

    // ADDED: draws the sampled surface so heights can be checked without pressing
    // Play. Each dot is one sample - higher ground draws brighter and further up.
    void OnDrawGizmosSelected()
    {
        if (footprint == null) footprint = GetComponent<PolygonCollider2D>();
        if (area == null) area = GetComponent<GroundArea>();
        if (footprint == null || heightMap == null) return;

        Bounds b = footprint.bounds;
        const int steps = 12;

        for (int x = 0; x <= steps; x++)
        {
            for (int y = 0; y <= steps; y++)
            {
                Vector2 p = new Vector2(
                    Mathf.Lerp(b.min.x, b.max.x, x / (float)steps),
                    Mathf.Lerp(b.min.y, b.max.y, y / (float)steps));

                if (!footprint.OverlapPoint(p)) continue;

                float h = Sample(p);
                float t = (maxHeight > minHeight) ? Mathf.InverseLerp(minHeight, maxHeight, h) : 0f;

                Gizmos.color = Color.Lerp(Color.blue, Color.red, t);
                Gizmos.DrawSphere(p + Vector2.up * h, 0.04f);
            }
        }
    }// end of function >:D
}