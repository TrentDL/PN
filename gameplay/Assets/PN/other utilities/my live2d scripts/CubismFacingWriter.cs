// CubismFacingWriter.cs
// Put this on the Live2D model object (spongebob), alongside CubismModel.
//
// WHY THIS EXISTS:
// Cubism runs its updatables in a fixed order each frame via CubismUpdateController.
// Writing parameters from MonoBehaviour.Update lands outside that pipeline, so the
// value gets saved/restored or read at the wrong point and the flip lags a frame.
// Registering as ICubismUpdatable puts the write inside the pipeline, in a slot we
// control, so the renderer sees the value on the same frame it's set.

using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEngine;

public class CubismFacingWriter : MonoBehaviour, ICubismUpdatable
{
    [Header("Parameter IDs")]
    public string leftOpacityId  = "ParamLeftIdleOpacity";
    public string rightOpacityId = "Param2RightIdleOpacity";

    [Header("Values")]
    [Tooltip("Parameter value where the side IS visible.")]
    public float opacityOn  = -9.9f;
    [Tooltip("Parameter value where the side is hidden.")]
    public float opacityOff = -10f;

    /// <summary>Set this from PlayerControl. false = facing left.</summary>
    [HideInInspector] public bool FacingRight;

    private CubismParameter _left, _right;

    [HideInInspector]
    public bool HasUpdateController { get; set; }

    // Runs after the parameter store saves and after animation evaluation,
    // but before the renderer builds meshes. This is the slot that matters.
    public int ExecutionOrder
    {
        get { return CubismUpdateExecutionOrder.CubismParameterStoreSaveParameters + 1; }
    }

    public bool NeedsUpdateOnEditing
    {
        get { return true; }   // also apply while scrubbing in the editor
    }

    private void OnEnable()
    {
        var model = this.FindCubismModel();
        if (model == null)
        {
            Debug.LogError("CubismFacingWriter: no CubismModel found.", this);
            enabled = false;
            return;
        }

        _left  = model.Parameters.FindById(leftOpacityId);
        _right = model.Parameters.FindById(rightOpacityId);

        if (_left == null)
            Debug.LogError($"CubismFacingWriter: no parameter '{leftOpacityId}'.", this);
        if (_right == null)
            Debug.LogError($"CubismFacingWriter: no parameter '{rightOpacityId}'.", this);

        HasUpdateController = (GetComponent<CubismUpdateController>() != null);
    }

    // Called by CubismUpdateController at our ExecutionOrder slot.
    public void OnLateUpdate()
    {
        if (!enabled) return;

        // OverrideValue, not Value: this bypasses the blend/restore path that was
        // eating the plain assignment. Same call RestoreParameters uses internally.
        if (_left  != null) _left.OverrideValue(FacingRight  ? opacityOff : opacityOn);
        if (_right != null) _right.OverrideValue(FacingRight ? opacityOn  : opacityOff);
    }

    // Fallback if no CubismUpdateController is present on the model.
    private void LateUpdate()
    {
        if (!HasUpdateController) OnLateUpdate();
    }
}