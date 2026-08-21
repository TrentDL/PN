// TileStep.cs
//
// RENAMED: TileStepper -> TileStep   (file must match the class name)
//
// ADDED: Battletoads-style tile stepping. Replaces GroundHeightMap and
// PlatformBounds 
//
// WHY: earlier versions tried to make the jump ARC physically intersect a raised
//      surface, which is why the platform was unreachable and the tolerance needed
//      constant tuning. Battletoads never does that. It arcs you up, and whatever
//      tile you come down on is the one you get.
//
// RULES:
//      Jump    - at TOUCHDOWN, take the highest tile within maxStepUp of the height
//                you left from. Nothing in range means you fall past it.
//      Walk    - stepping off an edge DROPS you to whatever is below, any distance.
//      Stacking- tiles may be layered; the highest reachable one wins.
//
// CHANGED (v9): the caller now asks JumpTarget once at touchdown instead of every
//      frame of the arc. The methods themselves did not change - the timing did.
//      Deciding mid-arc is what made the jump feel automatic, because the game had
//      already chosen the destination before the player finished steering.
//
// CHANGED: was a MonoBehaviour. Nothing here needs a GameObject - these are lookups,
//      not behaviour - and being static-only stops it from being attached by accident.
//
// DEPTH: world Y is depth. A GroundArea polygon is a FOOTPRINT ON THE FLOOR -
//      draw it where the object's base sits. 'elevation' lifts the visual only,
//      which is what keeps AdjustPlayerScale and CubismDepthSort correct.

using UnityEngine;

public static class TileStep
{
    /// <summary>Highest tile at this point that a jump from 'fromElevation' can reach.</summary>
    // Null only if the point is over a true gap. maxStepUp is the jump's reach; there
    // is no reach limit going DOWN, so a jump can also carry you off a ledge.
    public static GroundArea JumpTarget(Vector2 worldPosition, float fromElevation, float maxStepUp)
    {
        GroundArea best = null;

        for (int i = 0; i < GroundArea.All.Count; i++)
        {
            GroundArea a = GroundArea.All[i];
            if (!a.Contains(worldPosition)) continue;
            if (a.elevation > fromElevation + maxStepUp) continue;   // out of reach

            if (best == null || a.elevation > best.elevation) best = a;
        }

        return best;
    }// end of function >:D

    /// <summary>Highest tile strictly BELOW this one at this point. Null if none.</summary>
    // ADDED: the walk-off-an-edge drop. No reach limit - you fall as far as it takes.
    public static GroundArea DropTarget(Vector2 worldPosition, float fromElevation)
    {
        GroundArea best = null;

        for (int i = 0; i < GroundArea.All.Count; i++)
        {
            GroundArea a = GroundArea.All[i];
            if (!a.Contains(worldPosition)) continue;
            if (a.elevation >= fromElevation) continue;   // not below us

            if (best == null || a.elevation > best.elevation) best = a;
        }

        return best;
    }// end of function >:D
}