using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Minimap with fullscreen path-selection mode.
///
/// FLOW:
///   1. CarAgent.OnEpisodeBegin() calls minimap.RequestPathSelection(callback)
///   2. Minimap animates to fullscreen, user picks Start then Goal (or Random)
///   3. User clicks Begin → minimap animates back to corner → callback fires
///   4. CarAgent receives the chosen path and starts the episode
///
/// SCENE SETUP  (same as before, plus two new UI children):
///   Canvas
///   └── MinimapPanel        ← MinimapController here, Image component present
///         ├── OverlayPanel  ← RectTransform stretch-fill, Image alpha 0
///         ├── SelectionUI   ← new: Panel with buttons (created below)
///         │     ├── StatusText
///         │     ├── RandomBtn
///         │     └── BeginBtn
///         └── RawImage      ← optional camera feed (behind OverlayPanel)
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))]
public class MinimapController : MonoBehaviour, IPointerClickHandler
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Scene References")]
    public TestPath pathProvider;
    public Transform agentTransform;

    [Header("UI References")]
    public RectTransform overlayPanel;
    public Text statusLabel;   // inside SelectionUI
    public Button randomButton;  // inside SelectionUI
    public Button beginButton;   // inside SelectionUI
    public GameObject selectionUI;   // parent panel shown only during selection

    [Header("Sprites")]
    public Sprite dotSprite;
    public Sprite agentSprite;

    [Header("Colours")]
    public Color edgeColor = new Color(0.30f, 0.30f, 0.30f, 0.75f);
    public Color waypointColor = new Color(0.65f, 0.65f, 0.65f, 1.00f);
    public Color intersectionColor = new Color(1.00f, 0.78f, 0.10f, 1.00f);
    public Color pathColor = new Color(0.15f, 0.85f, 1.00f, 1.00f);
    public Color startColor = new Color(0.10f, 1.00f, 0.35f, 1.00f);
    public Color goalColor = new Color(1.00f, 0.20f, 0.20f, 1.00f);
    public Color agentColor = new Color(0.25f, 0.60f, 1.00f, 1.00f);

    [Header("Sizes")]
    public float waypointDotSize = 7f;
    public float edgeThickness = 1.5f;
    public float pathThickness = 5f;
    public float agentMarkerSize = 16f;
    public float selectionMarkerSize = 14f;

    [Header("Corner Rect  (filled automatically on first run)")]
    [Tooltip("Leave all zero — saved automatically from your scene position.")]
    public Vector2 cornerAnchorMin = Vector2.zero;
    public Vector2 cornerAnchorMax = Vector2.zero;
    public Vector2 cornerOffsetMin = Vector2.zero;
    public Vector2 cornerOffsetMax = Vector2.zero;

    [Header("Animation")]
    [Tooltip("Seconds to animate between corner and fullscreen.")]
    public float animDuration = 0.35f;

    [Header("Padding")]
    public float worldPadding = 30f;

    // ──────────────────────────────────────────────────────────────────────────
    //  Private state
    // ──────────────────────────────────────────────────────────────────────────

    private RectTransform minimapRect;
    private Bounds worldBounds;
    private bool ready;
    private bool selectionActive;

    private Waypoint selectedStart;
    private Waypoint selectedGoal;
    private List<Waypoint> currentPath;

    private System.Action<List<Waypoint>, Waypoint, Waypoint> onPathConfirmed;

    private enum Phase { PickStart, PickGoal }
    private Phase phase = Phase.PickStart;

    private readonly List<RectTransform> edgeRTs = new List<RectTransform>();
    private readonly List<RectTransform> dotRTs = new List<RectTransform>();
    private readonly List<RectTransform> pathRTs = new List<RectTransform>();

    private RectTransform agentRT;
    private RectTransform startRT;
    private RectTransform goalRT;

    // ══════════════════════════════════════════════════════════════════════════
    //  Unity lifecycle
    // ══════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        minimapRect = GetComponent<RectTransform>();
        if (overlayPanel == null) overlayPanel = minimapRect;

        // Save corner rect on first run (so we can return to it after fullscreen)
        if (cornerAnchorMin == Vector2.zero && cornerAnchorMax == Vector2.zero)
            SaveCornerRect();
    }

    void Start()
    {
        if (pathProvider == null)
        {
            Debug.LogError("[Minimap] pathProvider not assigned!", this);
            return;
        }

        if (pathProvider.allWaypoints == null || pathProvider.allWaypoints.Count == 0)
        {
            Debug.LogWarning("[Minimap] allWaypoints is empty – check waypointParent on TestPath.", this);
            return;
        }

        // Wire buttons
        if (randomButton != null) randomButton.onClick.AddListener(OnRandomClicked);
        if (beginButton != null) beginButton.onClick.AddListener(OnBeginClicked);

        // Hide selection UI until an episode begins
        if (selectionUI != null) selectionUI.SetActive(false);

        ComputeWorldBounds();
        RebuildGraph();
        CreateAgentMarker();
        CreateSelectionMarkers();
        ready = true;

        Debug.Log($"[Minimap] Ready – {pathProvider.allWaypoints.Count} waypoints.");
    }

    void LateUpdate()
    {
        if (!ready || agentTransform == null || agentRT == null) return;
        agentRT.anchoredPosition = ToMap(agentTransform.position);
        float yaw = agentTransform.eulerAngles.y;
        agentRT.localRotation = Quaternion.Euler(0f, 0f, -yaw);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Public API  — called by CarAgent
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called at the start of every episode.
    /// Animates minimap to fullscreen and waits for the user to pick a path.
    /// <paramref name="callback"/> fires when the user clicks Begin.
    /// </summary>
    public void RequestPathSelection(System.Action<List<Waypoint>, Waypoint, Waypoint> callback)
    {
        if (!ready) { callback?.Invoke(null, null, null); return; }

        onPathConfirmed = callback;
        selectionActive = true;

        // Reset previous selection
        selectedStart = null;
        selectedGoal = null;
        currentPath = null;
        DestroyAll(pathRTs);
        if (startRT != null) startRT.gameObject.SetActive(false);
        if (goalRT != null) goalRT.gameObject.SetActive(false);
        if (beginButton != null) beginButton.interactable = false;

        ApplyPhase(Phase.PickStart);

        if (selectionUI != null) selectionUI.SetActive(true);

    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Button handlers
    // ══════════════════════════════════════════════════════════════════════════

    private void OnRandomClicked()
    {
        if (!selectionActive) return;

        var wps = pathProvider.allWaypoints;
        selectedStart = wps[Random.Range(0, wps.Count)];
        do { selectedGoal = wps[Random.Range(0, wps.Count)]; }
        while (selectedGoal == selectedStart);

        currentPath = PathFinder.FindPathAStar(selectedStart, selectedGoal);

        startRT.anchoredPosition = ToMap(selectedStart.transform.position);
        startRT.gameObject.SetActive(true);
        goalRT.anchoredPosition = ToMap(selectedGoal.transform.position);
        goalRT.gameObject.SetActive(true);

        DrawPath();

        if (beginButton != null) beginButton.interactable = true;
        SetStatus("Random path selected — click Begin to start.");
    }

    private void OnBeginClicked()
    {
        if (!selectionActive) return;
        if (selectedStart == null || selectedGoal == null) return;

        selectionActive = false;
        if (selectionUI != null) selectionUI.SetActive(false);

        pathProvider.SetPath(currentPath, selectedStart, selectedGoal);
        onPathConfirmed?.Invoke(currentPath, selectedStart, selectedGoal);
        onPathConfirmed = null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Click on map  (IPointerClickHandler)
    // ══════════════════════════════════════════════════════════════════════════

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!ready || !selectionActive) return;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                minimapRect, eventData.position, eventData.pressEventCamera, out local))
            return;

        Vector3 worldClick = FromMap(local);
        Waypoint nearest = NearestWaypoint(worldClick);
        if (nearest == null) return;

        if (phase == Phase.PickStart)
        {
            selectedStart = nearest;
            selectedGoal = null;
            currentPath = null;
            DestroyAll(pathRTs);

            startRT.anchoredPosition = ToMap(selectedStart.transform.position);
            startRT.gameObject.SetActive(true);
            goalRT.gameObject.SetActive(false);

            if (beginButton != null) beginButton.interactable = false;
            ApplyPhase(Phase.PickGoal);
        }
        else
        {
            if (nearest == selectedStart) return;

            selectedGoal = nearest;
            goalRT.anchoredPosition = ToMap(selectedGoal.transform.position);
            goalRT.gameObject.SetActive(true);

            currentPath = PathFinder.FindPathAStar(selectedStart, selectedGoal);
            DrawPath();

            bool found = currentPath != null && currentPath.Count > 0;
            if (beginButton != null) beginButton.interactable = found;
            SetStatus(found ? "Path found — click Begin to start." : "No path found. Try different points.");
            ApplyPhase(Phase.PickStart);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Animation
    // ══════════════════════════════════════════════════════════════════════════

    private void SaveCornerRect()
    {
        cornerAnchorMin = minimapRect.anchorMin;
        cornerAnchorMax = minimapRect.anchorMax;
        cornerOffsetMin = minimapRect.offsetMin;
        cornerOffsetMax = minimapRect.offsetMax;
    }

    private IEnumerator AnimateToFullscreen()
    {
        // Save corner state first
        SaveCornerRect();

        Vector2 fromAMin = minimapRect.anchorMin;
        Vector2 fromAMax = minimapRect.anchorMax;
        Vector2 fromOMin = minimapRect.offsetMin;
        Vector2 fromOMax = minimapRect.offsetMax;

        Vector2 toAMin = Vector2.zero;
        Vector2 toAMax = Vector2.one;
        Vector2 toOMin = Vector2.zero;
        Vector2 toOMax = Vector2.zero;

        yield return AnimateRect(fromAMin, fromAMax, fromOMin, fromOMax,
                                 toAMin, toAMax, toOMin, toOMax);
    }

    private IEnumerator AnimateToCorner(System.Action onComplete)
    {
        Vector2 fromAMin = minimapRect.anchorMin;
        Vector2 fromAMax = minimapRect.anchorMax;
        Vector2 fromOMin = minimapRect.offsetMin;
        Vector2 fromOMax = minimapRect.offsetMax;

        yield return AnimateRect(fromAMin, fromAMax, fromOMin, fromOMax,
                                 cornerAnchorMin, cornerAnchorMax, cornerOffsetMin, cornerOffsetMax);

        onComplete?.Invoke();
    }

    private IEnumerator AnimateRect(
        Vector2 fromAMin, Vector2 fromAMax, Vector2 fromOMin, Vector2 fromOMax,
        Vector2 toAMin, Vector2 toAMax, Vector2 toOMin, Vector2 toOMax)
    {
        float elapsed = 0f;
        while (elapsed < animDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / animDuration));

            minimapRect.anchorMin = Vector2.Lerp(fromAMin, toAMin, t);
            minimapRect.anchorMax = Vector2.Lerp(fromAMax, toAMax, t);
            minimapRect.offsetMin = Vector2.Lerp(fromOMin, toOMin, t);
            minimapRect.offsetMax = Vector2.Lerp(fromOMax, toOMax, t);

            yield return null;
        }

        minimapRect.anchorMin = toAMin;
        minimapRect.anchorMax = toAMax;
        minimapRect.offsetMin = toOMin;
        minimapRect.offsetMax = toOMax;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Coordinate conversion
    // ══════════════════════════════════════════════════════════════════════════

    private void ComputeWorldBounds()
    {
        worldBounds = new Bounds(pathProvider.allWaypoints[0].transform.position, Vector3.zero);
        foreach (var wp in pathProvider.allWaypoints)
            worldBounds.Encapsulate(wp.transform.position);
        worldBounds.Expand(new Vector3(worldPadding * 2f, 200f, worldPadding * 2f));
    }

    private Vector2 ToMap(Vector3 world)
    {
        Rect r = minimapRect.rect;
        float u = Mathf.InverseLerp(worldBounds.min.x, worldBounds.max.x, world.x);
        float v = Mathf.InverseLerp(worldBounds.min.z, worldBounds.max.z, world.z);
        return new Vector2(Mathf.Lerp(r.xMin, r.xMax, u),
                           Mathf.Lerp(r.yMin, r.yMax, v));
    }

    private Vector3 FromMap(Vector2 local)
    {
        Rect r = minimapRect.rect;
        float u = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
        float v = Mathf.InverseLerp(r.yMin, r.yMax, local.y);
        return new Vector3(
            Mathf.Lerp(worldBounds.min.x, worldBounds.max.x, u),
            0f,
            Mathf.Lerp(worldBounds.min.z, worldBounds.max.z, v));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Graph rendering
    // ══════════════════════════════════════════════════════════════════════════

    public void RebuildGraph()
    {
        DestroyAll(edgeRTs);
        DestroyAll(dotRTs);

        var drawn = new HashSet<long>();
        for (int i = 0; i < pathProvider.allWaypoints.Count; i++)
        {
            Waypoint wp = pathProvider.allWaypoints[i];
            foreach (Waypoint nb in wp.connectedWaypoints)
            {
                if (nb == null) continue;
                int j = pathProvider.allWaypoints.IndexOf(nb);
                if (j < 0) continue;
                long lo = Mathf.Min(i, j), hi = Mathf.Max(i, j);
                long key = lo * 100000L + hi;
                if (drawn.Contains(key)) continue;
                drawn.Add(key);
                edgeRTs.Add(MakeLine(ToMap(wp.transform.position),
                                     ToMap(nb.transform.position),
                                     edgeColor, edgeThickness));
            }
        }

        foreach (Waypoint wp in pathProvider.allWaypoints)
        {
            Color c = wp.isIntersection ? intersectionColor : waypointColor;
            dotRTs.Add(MakeDot(ToMap(wp.transform.position), c, waypointDotSize));
        }
    }

    private void DrawPath()
    {
        DestroyAll(pathRTs);
        if (currentPath == null || currentPath.Count < 2) return;

        int insertAfter = edgeRTs.Count;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            RectTransform rt = MakeLine(
                ToMap(currentPath[i].transform.position),
                ToMap(currentPath[i + 1].transform.position),
                pathColor, pathThickness);
            rt.SetSiblingIndex(insertAfter + i);
            pathRTs.Add(rt);
        }

        foreach (var d in dotRTs) d.SetAsLastSibling();
        if (startRT != null) startRT.SetAsLastSibling();
        if (goalRT != null) goalRT.SetAsLastSibling();
        if (agentRT != null) agentRT.SetAsLastSibling();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Markers
    // ══════════════════════════════════════════════════════════════════════════

    private void CreateAgentMarker()
    {
        agentRT = MakeDot(Vector2.zero, agentColor, agentMarkerSize);
        agentRT.name = "AgentMarker";
        if (agentSprite != null) agentRT.GetComponent<Image>().sprite = agentSprite;
        agentRT.SetAsLastSibling();
    }

    private void CreateSelectionMarkers()
    {
        startRT = MakeDot(Vector2.zero, startColor, selectionMarkerSize);
        startRT.name = "StartMarker";
        startRT.gameObject.SetActive(false);

        goalRT = MakeDot(Vector2.zero, goalColor, selectionMarkerSize);
        goalRT.name = "GoalMarker";
        goalRT.gameObject.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private void ApplyPhase(Phase p)
    {
        phase = p;
        SetStatus(p == Phase.PickStart
            ? "Click map to set START waypoint"
            : "Click map to set DESTINATION waypoint");
    }

    private void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
    }

    private Waypoint NearestWaypoint(Vector3 worldPos)
    {
        Waypoint best = null;
        float bestSq = float.MaxValue;
        foreach (Waypoint wp in pathProvider.allWaypoints)
        {
            float dx = wp.transform.position.x - worldPos.x;
            float dz = wp.transform.position.z - worldPos.z;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; best = wp; }
        }
        return best;
    }

    private RectTransform MakeDot(Vector2 pos, Color color, float size)
    {
        var go = new GameObject("MapDot", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(overlayPanel, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = pos;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.sprite = dotSprite;
        img.raycastTarget = false;
        return rt;
    }

    private RectTransform MakeLine(Vector2 a, Vector2 b, Color color, float thickness)
    {
        var go = new GameObject("MapLine", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(overlayPanel, false);
        Vector2 dir = b - a;
        float len = dir.magnitude;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(len, thickness);
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    private void DestroyAll(List<RectTransform> list)
    {
        foreach (var rt in list) if (rt != null) Destroy(rt.gameObject);
        list.Clear();
    }
}