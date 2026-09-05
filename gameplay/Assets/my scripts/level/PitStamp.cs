// PitStamp.cs
//
// ADDED (pit fix, option A): stamps this object's polygon into another polygon as
// a HOLE, so the floor beneath a pit stops answering TileStep.JumpTarget.
//
// WHY A SEPARATE SCRIPT and not a method on GroundArea: this is an EDITING action,
//      not a runtime rule. GroundArea answers questions about ground during play;
//      nothing in it runs once and mutates another object's collider. Putting it
//      there would give every area in the scene a button it must never press.
//
// WHY [ContextMenu] and not Awake: the hole is authored data. Cutting it at runtime
//      would redo the same work every play session and leave the scene file unedited,
//      so the change would never persist.

using UnityEngine;

[RequireComponent(typeof(PolygonCollider2D))]
public class PitStamp : MonoBehaviour
{
    [Tooltip("The floor this pit should punch a hole in. Usually GroundBounds.")]
    public PolygonCollider2D floor;

    [ContextMenu("Stamp Hole Into Floor")]
    private void StampHole()
    {
        if (floor == null) { Debug.LogWarning("No floor assigned.", this); return; }

        PolygonCollider2D pit = GetComponent<PolygonCollider2D>();
        Vector2[] hole = pit.GetPath(0);

        // The two objects have different transforms, so the pit's LOCAL points would
        // land in the wrong place inside the floor. Out to world, then back into the
        // floor's local space.
        for (int i = 0; i < hole.Length; i++)
            hole[i] = floor.transform.InverseTransformPoint(
                          pit.transform.TransformPoint(hole[i] + pit.offset));

        // Path 0 is the outer shape - never touch it. Append as the next path.
        floor.pathCount = floor.pathCount + 1;
        floor.SetPath(floor.pathCount - 1, hole);

        Debug.Log($"Stamped hole into {floor.name}. pathCount is now {floor.pathCount}.", floor);
    }// end of function >:D
}