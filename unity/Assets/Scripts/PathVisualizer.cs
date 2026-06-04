using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class PathVisualizer : MonoBehaviour
{
    public TestPath pathProvider;
    public Color lineColor = Color.cyan;
    public float lineThickness = 6f;   // 👈 thickness control

    private void OnDrawGizmos()
    {
        if (pathProvider == null)
            return;

        List<Waypoint> path = pathProvider.CurrentPath;

        if (path == null || path.Count < 2)
            return;

#if UNITY_EDITOR
        Handles.color = lineColor;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Handles.DrawAAPolyLine(
                lineThickness,
                path[i].transform.position,
                path[i + 1].transform.position
            );
        }
#endif
    }
}