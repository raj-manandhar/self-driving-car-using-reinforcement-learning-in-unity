using System.Collections.Generic;
using UnityEngine;

public class PathFinder
{
    // Find path from start to goal using A*
    public static List<Waypoint> FindPathAStar(Waypoint start, Waypoint goal)
    {
        if (start == null || goal == null)
            return null;

        // Open set = nodes to explore
        List<Waypoint> openSet = new List<Waypoint> { start };
        // Closed set = already evaluated nodes
        HashSet<Waypoint> closedSet = new HashSet<Waypoint>();
        // Maps node → parent (for path reconstruction)
        Dictionary<Waypoint, Waypoint> cameFrom = new Dictionary<Waypoint, Waypoint>();
        // Cost from start to a node
        Dictionary<Waypoint, float> gScore = new Dictionary<Waypoint, float>();
        gScore[start] = 0f;

        while (openSet.Count > 0)
        {
            // Pick node in openSet with lowest fScore = g + heuristic
            Waypoint current = openSet[0];
            float currentF = gScore[current] + Heuristic(current, goal);
            foreach (Waypoint node in openSet)
            {
                float f = gScore.ContainsKey(node) ? gScore[node] + Heuristic(node, goal) : float.MaxValue;
                if (f < currentF)
                {
                    current = node;
                    currentF = f;
                }
            }

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            openSet.Remove(current);
            closedSet.Add(current);

            foreach (Waypoint neighbor in current.connectedWaypoints)
            {
                if (closedSet.Contains(neighbor))
                    continue;

                float tentativeG = gScore[current] + Vector3.Distance(current.transform.position, neighbor.transform.position);

                if (!openSet.Contains(neighbor))
                    openSet.Add(neighbor);
                else if (tentativeG >= (gScore.ContainsKey(neighbor) ? gScore[neighbor] : float.MaxValue))
                    continue;

                // This path is the best so far
                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
            }
        }

        return null; // No path found
    }

    private static float Heuristic(Waypoint a, Waypoint b)
    {
        // Euclidean distance as heuristic
        return Vector3.Distance(a.transform.position, b.transform.position);
    }

    private static List<Waypoint> ReconstructPath(Dictionary<Waypoint, Waypoint> cameFrom, Waypoint current)
    {
        List<Waypoint> path = new List<Waypoint> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}