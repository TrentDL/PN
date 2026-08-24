using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoundsManager : MonoBehaviour
{
   // ADDED: Static reference for global access (Singleton pattern)
    public static BoundsManager Instance { get; private set; }

    // ADDED: Reference to the polygon collider that defines walkable area
    private PolygonCollider2D boundsCollider;

    // ADDED: Optional visual feedback in Scene view
    [Header("Debug Visualization")]
    [Tooltip("Show boundary gizmos in Scene view")]
    public bool showDebugGizmos = true;
    public Color gizmoColor = Color.green;

    void Awake()
    {
        // ADDED: Singleton pattern - ensures only one BoundsManager exists
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("Multiple BoundsManagers detected! Destroying duplicate.");
            Destroy(gameObject);
            return;
        }
        
        // ADDED: Get the PolygonCollider2D component
        boundsCollider = GetComponent<PolygonCollider2D>();
        
        // ADDED: Safety check for missing collider
        if (boundsCollider == null)
        {
            Debug.LogError("BoundsManager requires a PolygonCollider2D component!");
        }
    }// end of function >:D

/// <summary>
    /// Checks if a world position is within the walkable bounds
    /// </summary>
    /// <param name="worldPosition">Position to check in world space</param>
    /// <returns>True if position is inside bounds, false otherwise</returns>
    public bool IsWithinBounds(Vector2 worldPosition)
    {
        // ADDED: Null check for safety
        if (boundsCollider == null) return true; // Fail-safe: allow movement if no collider
        
        // ADDED: Use OverlapPoint to check if position is inside the collider
        // This is more efficient than raycasting for polygon containment
        return boundsCollider.OverlapPoint(worldPosition);

    }// end of function >:D

    /// <summary>
    /// Clamps a position to stay within bounds by finding the nearest valid point
    /// </summary>
    /// <param name="desiredPosition">The position player wants to move to</param>
    /// <returns>Clamped position within bounds</returns>
    public Vector2 ClampToBounds(Vector2 desiredPosition)
    {
        // ADDED: If already within bounds, return as-is
        if (IsWithinBounds(desiredPosition))
        {
            return desiredPosition;
        }
        
        // ADDED: If outside bounds, find closest point on the boundary
        Vector2 closestPoint = boundsCollider.ClosestPoint(desiredPosition);
        
        // ADDED: Move slightly inward from boundary to prevent edge-case jittering
        Vector2 directionInward = (closestPoint - desiredPosition).normalized;
        return closestPoint + directionInward * 0.01f; // 0.01 unit buffer

    }// end of function >:D

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || boundsCollider == null) return;
        
        Gizmos.color = gizmoColor;
        
        // Draw lines connecting all polygon points
        Vector2[] points = boundsCollider.points;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 currentPoint = transform.TransformPoint(points[i]);
            Vector2 nextPoint = transform.TransformPoint(points[(i + 1) % points.Length]);
            Gizmos.DrawLine(currentPoint, nextPoint);
        }

    }// end of function >:D


}
