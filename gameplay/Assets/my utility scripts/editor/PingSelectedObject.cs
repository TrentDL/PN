using UnityEditor;
using UnityEngine;

public class PingSelectedObject : MonoBehaviour
{
    // Assigns Ctrl+Shift+P on Windows, or Cmd+Shift+P on macOS
    [MenuItem("Tools/Ping Selected Asset %#p")]
    private static void PingAsset()
    {
        GameObject selectedTarget = Selection.activeGameObject;

        if (selectedTarget != null)
        {
            // Find the source prefab asset matching this Hierarchy instance
            Object prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(selectedTarget);

            if (prefabSource != null)
            {
                // Ping the actual file in the Project Window
                EditorGUIUtility.PingObject(prefabSource);
            }
            else
            {
                Debug.LogWarning($"'{selectedTarget.name}' is a pure scene object and not linked to a Project prefab asset.");
                
                // Fallback: Just highlight the target in the Hierarchy window
                EditorGUIUtility.PingObject(selectedTarget);
            }
        }
        else if (Selection.activeObject != null)
        {
            // If you already selected a regular asset file (like a material/texture), ping it directly
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
        else
        {
            Debug.LogWarning("No object selected to ping.");
        }
    }
}
