// TileStep.cs
//
// RENAMED: TileStepper -> TileStep   (file must match the class name)
//
// ADDED: Battletoads-style tile stepping. Replaces GroundHeightMap and PlatformBounds.
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
// CHANGED: was a MonoBehaviour. Nothing here needs a GameObject - these are lookups,
//      not behaviour - and being static-only stops it from being attached by accident.
//
// CHANGED (surface types): both loops skip fall-through surfaces. That test also
//      lives in GroundArea.AreaAt. A shared helper would need a filtering iterator or
//      a predicate delegate, and both cost more to read later than two copies.
//
// CHANGED (overlap pass): both tie-breaks match GroundArea.RulingAreaAt - priority
//      first, elevation second. WHY: these answer "which tile do I land on" while
//      RulingAreaAt answers "whose rules apply". Sorting differently would let the
//      player land on one tile while obeying another's rules.
//
// CHANGED (ramp pass): heights come from a.ElevationAt(worldPosition), not the
//      elevation field, so a sloped area is measured where the player actually is.
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

            // ADDED: a surface you fall through is not a landing. Skipping it here is
            // what makes a Pit drawn over the floor return the floor - or null when
            // there is no floor under it, which is the signal to fall.
            if (a.canFallThrough) continue;

            // ADDED (pit fix): a higher-priority fall-through area covering this point means
            // there is no floor here, even though THIS area is solid. Without this, a pit
            // drawn over the base floor is invisible to the walk-off check - the floor
            // underneath still answers, so the player walks across the pit.
            GroundArea ruler = GroundArea.RulingAreaAt(worldPosition);
            if (ruler != null && ruler != a && ruler.canFallThrough) continue;

            if (!a.Contains(worldPosition)) continue;

            // CHANGED (ramp pass): was a.elevation. A ramped area's height depends on
            // WHERE on it you are, so the reach test has to ask at this position.
            float here = a.ElevationAt(worldPosition);

            if (here > fromElevation + maxStepUp) continue;   // out of reach

            if (best == null
                || a.priority > best.priority
                || (a.priority == best.priority && here > best.ElevationAt(worldPosition)))
                best = a;
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

            if (a.canFallThrough) continue;   // ADDED: same reason as JumpTarget

            if (!a.Contains(worldPosition)) continue;

            float here = a.ElevationAt(worldPosition);   // CHANGED (ramp pass)

            if (here >= fromElevation) continue;   // not below us

            if (best == null
                || a.priority > best.priority
                || (a.priority == best.priority && here > best.ElevationAt(worldPosition)))
                best = a;
        }

        return best;
    }// end of function >:D
}