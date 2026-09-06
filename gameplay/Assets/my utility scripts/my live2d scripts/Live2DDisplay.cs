using UnityEngine;

/// <summary>
/// Sits on a display quad (MeshRenderer) placed in the visible game world.
/// Shows a capture rig's RenderTexture through the quad's material and, optionally,
/// auto-sizes the quad so the captured character appears at its true NATIVE size.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
[DisallowMultipleComponent]
public class Live2DDisplay : MonoBehaviour
{
    [Tooltip("Shader texture property the material samples. Live2DMotionBlur uses _MainTex.")]
    public string textureProperty = "_MainTex";

    [Header("Sizing")]
    [Tooltip("Auto-scale the quad so quad height = 2 * capture ortho size => character shows at native size.")]
    public bool matchNativeSize = true;              // ADDED
    [Tooltip("Multiply native size (1 = true native, 2 = twice as big, etc.).")]
    public float sizeMultiplier = 1f;                // ADDED

    MeshRenderer _mr;
    MaterialPropertyBlock _mpb;

    /// <summary>
    /// Point this quad at the RenderTexture (no material clone) and, if enabled,
    /// size the quad to display the capture at native scale, undistorted.
    /// CHANGED: signature now takes the capture camera's ortho size for the sizing math.
    /// </summary>
    public void SetSource(RenderTexture rt, float captureOrthoSize)   // CHANGED: added captureOrthoSize
    {
        if (_mr == null)  _mr  = GetComponent<MeshRenderer>();
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        // Texture override via MaterialPropertyBlock (from the earlier leak fix).
        _mr.GetPropertyBlock(_mpb);
        _mpb.SetTexture(textureProperty, rt);
        _mr.SetPropertyBlock(_mpb);

        // --- ADDED: native-size sizing ---
        // The camera captures a region (2*orthoSize) tall. Mapping that onto a quad
        // whose height is ALSO (2*orthoSize) makes 1 captured unit = 1 quad unit, so the
        // character displays at its real size. Width follows the RT aspect so nothing
        // gets stretched. orthoSize then only controls margin/quality, not apparent size.
        if (matchNativeSize && rt != null && rt.height > 0)
        {
            float aspect = (float)rt.width / rt.height;          // ADDED: RT aspect, prevents stretch
            float h = 2f * captureOrthoSize * sizeMultiplier;    // ADDED: quad world height
            float w = h * aspect;                                // ADDED: quad world width
            transform.localScale = new Vector3(w, h, 1f);        // ADDED: apply (Unity Quad is 1x1, so scale = world size)
        }
        // --- END ADDED ---
    }
}
