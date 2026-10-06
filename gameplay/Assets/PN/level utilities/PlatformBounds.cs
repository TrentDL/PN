// PlatformBounds.cs
//
// ADDED: add-on component for GroundArea. Marks that area as a raised platform
// the player can only reach by jumping high enough.
//
// WHY: GroundArea.AreaAt() picks the highest elevation under a point, so a
//      platform grabs the player even when they walk underneath it.
//
// CHANGED (v4 - fixes "still falls through"): reaching a platform is now a
//      COLLIDER test against the player's visual, not a height number.
//      v1-v3 compared jump height to elevation and never worked, because the
//      raised collider sits far above the ground plane - the player's ground
//      point can never be inside it, so no tolerance or snap radius could help.
//      The visual is the part that rises, so the visual is what must touch it.
//      REMOVED with it: landTolerance, snapRadius, jumpPeak - the geometry
//      answers the question now, so there is nothing left to tune by hand.
//
// DEPTH: world Y is depth in this project, so a GroundArea polygon is a footprint
//      on the floor. 'elevation' is a VISUAL lift only - the ground point stays in
//      the depth plane, which is what keeps AdjustPlayerScale and CubismDepthSort
//      correct. That is why the two colliders below are not interchangeable.
//
// USAGE: put this on the SAME GameObject as the platform's GroundArea.
//        GroundArea's PolygonCollider2D = the walkable footprint on the floor.
//        'reach'                        = a collider drawn at the raised surface.
//        GroundArea.elevation           = how high the model sits once up there.

using UnityEngine;

[RequireComponent(typeof(GroundArea))]
public class PlatformBounds : MonoBehaviour
{
    [Tooltip("Collider drawn at this platform's RAISED surface, up where the model " +
             "arrives. The jump reaches the platform when the player's visual feet " +
             "enter this. Leave empty and the platform can never be reached.")]
    public Collider2D reach;

    private GroundArea area;   // the footprint this platform gates

    void Awake()
    {
        area = GetComponent<GroundArea>();
    }// end of function >:D

    /// <summary>The walkable footprint this platform gates.</summary>
    public GroundArea Area
    {
        get { return area; }
    }// end of function >:D

    /// <summary>True if the player's visual feet are inside the raised collider.</summary>
    // visualFeet is the ground point lifted by the jump arc and the current ground
    // height - see PlayerControl3.VisualFeet.
    public bool Reached(Vector2 visualFeet)
    {
        return reach != null && reach.OverlapPoint(visualFeet);
    }// end of function >:D

    /// <summary>Highest platform whose raised collider the visual is currently in.</summary>
    // ADDED: the single reach search. Highest wins so stacked ledges pick the top one.
    public static PlatformBounds ReachedBy(Vector2 visualFeet)
    {
        PlatformBounds best = null;

        for (int i = 0; i < GroundArea.All.Count; i++)
        {
            PlatformBounds platform = GroundArea.All[i].GetComponent<PlatformBounds>();
            if (platform == null || !platform.Reached(visualFeet)) continue;

            if (best == null || platform.Area.elevation > best.Area.elevation) best = platform;
        }

        return best;
    }// end of function >:D

    /// <summary>
    /// Which ground is under the player. Same job as GroundArea.AreaAt, but a
    /// platform's footprint only counts while its raised collider is being touched.
    /// </summary>
    // Without the gate, walking under the tooth would put you on top of it, since
    // AreaAt picks the highest elevation. Areas WITHOUT this component are
    // untouched, so plain ground keeps working exactly as before.
    public static GroundArea AreaAt(Vector2 groundPoint, Vector2 visualFeet)
    {
        GroundArea best = null;

        for (int i = 0; i < GroundArea.All.Count; i++)
        {
            GroundArea a = GroundArea.All[i];
            if (!a.Contains(groundPoint)) continue;

            PlatformBounds platform = a.GetComponent<PlatformBounds>();
            if (platform != null && !platform.Reached(visualFeet)) continue;

            if (best == null || a.elevation > best.elevation) best = a;
        }

        return best;
    }// end of function >:D
}