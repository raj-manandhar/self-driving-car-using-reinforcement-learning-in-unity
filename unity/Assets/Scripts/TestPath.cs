using UnityEngine;
using System.Collections.Generic;

public class TestPath : MonoBehaviour
{
    public Transform waypointParent;
    public List<Waypoint> allWaypoints;

    private List<Waypoint> path;
    public List<Waypoint> CurrentPath => path;

    [Header("Fixed Path Settings")]
    public Waypoint fixedStart;
    public Waypoint fixedGoal;

    public Waypoint CurrentStart { get; private set; }
    public Waypoint CurrentGoal { get; private set; }

    // ── Minimap integration ───────────────────────────────────────────────────
    public bool HasManualPath { get; private set; }

    public void SetPath(List<Waypoint> newPath, Waypoint start, Waypoint goal)
    {
        path = newPath;
        CurrentStart = start;
        CurrentGoal = goal;
        HasManualPath = newPath != null && newPath.Count > 0;
    }

    public void ClearManualPath()
    {
        HasManualPath = false;
    }

    // ── Original ──────────────────────────────────────────────────────────────
    void Awake()
    {
        if (waypointParent != null && waypointParent.childCount > 0)
        {
            allWaypoints = new List<Waypoint>();
            foreach (Transform child in waypointParent)
            {
                Waypoint wp = child.GetComponent<Waypoint>();
                if (wp != null)
                    allWaypoints.Add(wp);
            }
        }
    }

    public void GetFixedPath()
    {
        if (fixedStart == null || fixedGoal == null)
        {
            Debug.LogWarning("Fixed start or goal not assigned!");
            return;
        }
        CurrentStart = fixedStart;
        CurrentGoal = fixedGoal;
        path = PathFinder.FindPathAStar(CurrentStart, CurrentGoal);
        if (path == null) Debug.LogWarning("No path found for fixed waypoints!");
    }

    public void GenerateRandomPath()
    {
        if (HasManualPath) return;   // minimap already set a path

        if (allWaypoints == null || allWaypoints.Count < 2) return;

        CurrentStart = allWaypoints[Random.Range(0, allWaypoints.Count)];
        do { CurrentGoal = allWaypoints[Random.Range(0, allWaypoints.Count)]; }
        while (CurrentGoal == CurrentStart);

        path = PathFinder.FindPathAStar(CurrentStart, CurrentGoal);
        if (path == null) Debug.Log("No path found!");
    }
}