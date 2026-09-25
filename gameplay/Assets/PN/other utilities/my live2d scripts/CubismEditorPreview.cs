using UnityEngine;
using Live2D.Cubism.Core;

#if UNITY_EDITOR
using UnityEditor;                         // editor-only namespace for EditorApplication
#endif

[ExecuteAlways]
public class CubismEditorPreview : MonoBehaviour
{
    private CubismModel _model;

    private void OnEnable()
    {
        _model = this.FindCubismModel();
#if UNITY_EDITOR
        EditorApplication.update += OnEditorUpdate;   // KEY: fires during Animation-window preview, LateUpdate does not
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= OnEditorUpdate;   // always unsubscribe to avoid leaks/errors
#endif
    }

#if UNITY_EDITOR
    private void OnEditorUpdate()
    {
        if (Application.isPlaying) return;            // in Play mode the SDK's own loop handles it
        if (_model == null) _model = this.FindCubismModel();
        if (_model == null) return;

        _model.ForceUpdateNow();                      // rebuild mesh from the values the Animation window just wrote
        SceneView.RepaintAll();                       // make sure the Scene view actually redraws
    }
#endif
}