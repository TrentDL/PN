using UnityEngine;

/// <summary>
/// Self-contained Live2D capture rig. You place ONE per character, by hand.
/// Creates its own RenderTexture, configures its child capture camera, and feeds
/// the texture to an assigned display quad. Runs in EDIT MODE too (ExecuteAlways).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class Live2DCaptureRig : MonoBehaviour
{
    [Header("Wiring (assign these)")]
    public Camera captureCamera;
    public Transform modelRoot;
    public Live2DDisplay targetDisplay;

    [Header("Capture frustum")]
    [Tooltip("Half the vertical world units the camera captures. SMALLER = tighter frame = higher quality on the model. Set just big enough to contain the model.")]
    public float orthoSize = 3f;        // CHANGED default 5 -> 3 (tighter framing; tune per model)
    public float nearClip  = 0.1f;
    public float farClip   = 50f;

    [Header("Layer")]
    [Tooltip("Layer ALL models share. Capture cams render ONLY this; the MAIN cam must EXCLUDE it.")]
    public int captureLayer = 31;

    [Header("Capture resolution (quality)")]
    public int rtWidth  = 1920;         // CHANGED: was a single square rtResolution = 512
    public int rtHeight = 1080;         // ADDED: separate height so you can match display aspect
    [Tooltip("Anti-aliasing samples: 1 = off, 2/4/8 = progressively smoother edges.")]
    public int msaa = 2;                // ADDED: crisper edges on the captured character

    RenderTexture _rt;

    void OnEnable()  { Build(); }
    void OnDisable() { Teardown(); }

    void Build()
    {
        if (captureCamera == null) return;

        if (modelRoot != null)
            SetLayerRecursively(modelRoot.gameObject, captureLayer);

        // CHANGED: non-square, higher-res RT for quality; MSAA + bilinear for smoothness.
        _rt = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32);
        _rt.antiAliasing = Mathf.Clamp(msaa, 1, 8);   // ADDED
        _rt.filterMode   = FilterMode.Bilinear;        // ADDED
        _rt.name = name + "_RT";
        _rt.Create();

        ApplyCameraSettings();

        // CHANGED: pass orthoSize so the display can size itself to native scale.
        if (targetDisplay != null)
            targetDisplay.SetSource(_rt, orthoSize);
    }

    void ApplyCameraSettings()
    {
        captureCamera.orthographic     = true;
        captureCamera.orthographicSize = orthoSize;
        captureCamera.nearClipPlane    = nearClip;
        captureCamera.farClipPlane     = farClip;
        captureCamera.clearFlags       = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor  = new Color(0f, 0f, 0f, 0f);
        captureCamera.cullingMask      = 1 << captureLayer;
        captureCamera.targetTexture    = _rt;
        // NOTE: Unity sets captureCamera.aspect = rtWidth/rtHeight automatically for an RT,
        // so the capture is undistorted as long as the quad matches that aspect (it does,
        // because Live2DDisplay scales width by the same RT aspect).
    }

    void Update()
    {
        if (_rt == null || captureCamera == null) return;
        captureCamera.orthographic     = true;
        captureCamera.orthographicSize = orthoSize;
        captureCamera.nearClipPlane    = nearClip;
        captureCamera.farClipPlane     = farClip;
        captureCamera.cullingMask      = 1 << captureLayer;
        if (targetDisplay != null)
            targetDisplay.SetSource(_rt, orthoSize);   // CHANGED: pass orthoSize
    }

    void Teardown()
    {
        if (captureCamera != null)
            captureCamera.targetTexture = null;

        if (_rt != null)
        {
            _rt.Release();
            if (Application.isPlaying) Destroy(_rt);
            else DestroyImmediate(_rt);
            _rt = null;
        }
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
