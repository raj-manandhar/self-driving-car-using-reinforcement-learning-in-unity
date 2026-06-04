using UnityEngine;
using System.Collections.Generic;

public class Waypoint : MonoBehaviour
{
    public List<Waypoint> connectedWaypoints = new List<Waypoint>();
    public bool isIntersection = false;

    private void OnValidate()
    {
        foreach (var wp in connectedWaypoints)
        {
            if (wp != null && !wp.connectedWaypoints.Contains(this))
            {
                wp.connectedWaypoints.Add(this);
            }
        }
    }

    private void OnDrawGizmos()
    {
        foreach (var wp in connectedWaypoints)
        {
            if (wp != null)
            {
                if (!this.isIntersection && !wp.isIntersection)
                {
                    Gizmos.color = Color.green;
                }
                else
                {
                    Gizmos.color = Color.red;
                }

                Gizmos.DrawLine(transform.position, wp.transform.position);
            }
        }
    }
}