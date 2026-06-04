using System.Collections.Generic;
using UnityEngine;

public class w : MonoBehaviour
{
    public void FindMaxConnectedDistance()
    {
        Waypoint[] waypoints = FindObjectsByType<Waypoint>(FindObjectsSortMode.None);
        float maxDistance = 0f;
        Waypoint wpA = null;
        Waypoint wpB = null;

        HashSet<(Waypoint, Waypoint)> checkedPairs =
            new HashSet<(Waypoint, Waypoint)>();

        foreach (Waypoint wp in waypoints)
        {
            foreach (Waypoint connected in wp.connectedWaypoints)
            {
                if (connected == null) continue;

                // Avoid duplicate A-B and B-A
                if (checkedPairs.Contains((connected, wp)))
                    continue;

                float distance = Vector3.Distance(
                    wp.transform.position,
                    connected.transform.position
                );

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    wpA = wp;
                    wpB = connected;
                }

                checkedPairs.Add((wp, connected));
            }
        }

        if (wpA != null && wpB != null)
        {
            Debug.Log("Max Distance: " + maxDistance +
                      " between " + wpA.name +
                      " and " + wpB.name);
        }
        else
        {
            Debug.Log("No connected waypoints found.");
        }
    }

    private void Start()
    {
        FindMaxConnectedDistance();
    }

}