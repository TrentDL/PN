// GroundMask.cs
//
// ADDED (painted ground pass): reads ground height from a PAINTED image instead of
// polygons. Paint it in any art program, lined up over the level art:
//   * TRANSPARENT pixel  -> no ground, the player falls
//   * OPAQUE pixel       -> ground. RED channel (0-255) = height in PIXELS
//                           (red 0 = floor, red 16 = a ledge 16 pixels up)
// The script only ANSWERS "how high is the ground here?" - it moves nothing.
//
// WHY A CPU IMAGE and not a stencil/depth buffer: those live on the GPU and only
// control drawing. Gameplay code would have to copy them back each frame (slow and
// a frame late). Reading the image's pixels once at startup is simple and instant.
//
// IMPORT SETTINGS the mask texture NEEDS (or the edges will not be exact):
//   Read/Write ON, Filter Mode Point, Compression None, Generate Mip Maps OFF.

using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class GroundMask : MonoBehaviour
{
    // What "no ground here" reads as. Very low, so any fall check treats it as a gap.
    public const float NoGround = -1000f;

    [Tooltip("Hide the mask in play mode. Leave it visible while lining it up.")]
    public bool hideInPlayMode = true;

    // ADDED (freeze scale pass): ported from GroundArea.freezeScaleWhileOn. The mask has no
    // separate areas, so it applies to ALL of this mask's ground - floors (red 0) included.
    // Only the SETTING lives here; PlayerControl3 reads it in AdjustPlayerScale, because
    // that is the one place the scale is written. Can be ticked/unticked during Play.
    [Tooltip("ON: perspective scale stops updating while using this mask - floors included. Debug / temporary fix for depth perception.")]
    public bool freezeScaleWhileOn = false;

    private Sprite sprite;
    private Color32[] pixels;   // copied ONCE in Awake - reading a cached array is fast
    private int width;

    void Awake()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        sprite = sr.sprite;
        if (hideInPlayMode) sr.enabled = false;

        // GetPixels32 throws if Read/Write is off, so say so plainly instead.
        if (sprite == null || !sprite.texture.isReadable)
        {
            Debug.LogError(name + ": GroundMask needs a sprite with Read/Write enabled in its import settings.", this);
            enabled = false;
            return;
        }

        pixels = sprite.texture.GetPixels32();
        width = sprite.texture.width;
    }// end of function >:D


    /// <summary>Ground height in WORLD units at a world position, or NoGround.</summary>
    public float HeightAt(Vector2 worldPosition)
    {
        if (pixels == null) return NoGround;

        // World -> this object's local space -> pixels. Handles moving, scaling and
        // rotating the mask object, and the sprite's pivot.
        Vector2 local = transform.InverseTransformPoint(worldPosition);
        float ppu = sprite.pixelsPerUnit;
        int x = Mathf.FloorToInt(local.x * ppu + sprite.pivot.x + sprite.rect.x);
        int y = Mathf.FloorToInt(local.y * ppu + sprite.pivot.y + sprite.rect.y);

        // Outside the painted image counts as no ground.
        if (x < sprite.rect.xMin || x >= sprite.rect.xMax ||
            y < sprite.rect.yMin || y >= sprite.rect.yMax) return NoGround;

        Color32 c = pixels[y * width + x];
        if (c.a < 128) return NoGround;   // transparent = fall

        return c.r / ppu;                 // red = height in pixels -> world units
    }// end of function >:D
}