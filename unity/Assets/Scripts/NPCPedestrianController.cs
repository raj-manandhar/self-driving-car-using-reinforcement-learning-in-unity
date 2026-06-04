using UnityEngine;
using UnityEngine.AI;
using HealthbarGames;

public class NPCPedestrianController : MonoBehaviour
{
    public NavMeshAgent agent;
    public Animator animator;

    public GameObject PATH;
    private SphereWaypoint[] waypoints;

    public float minDistance = 0.5f;
    private int index = 0;

    private bool waitingAtCrossing = false;

    [Header("Obstacle Detection")]
    public float sensorLength = 1.5f;
    public float sensorHeight = 1f;
    public LayerMask obstacleMask;

    [Header("Reset Settings")]
    public float maxStopTime = 60f;
    private float stopTimer = 0f;
    private Vector3 startPosition;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        agent.updateRotation = true;

        startPosition = transform.position;

        // Load waypoints
        int count = PATH.transform.childCount;
        waypoints = new SphereWaypoint[count];

        for (int i = 0; i < count; i++)
        {
            waypoints[i] = PATH.transform
                .GetChild(i)
                .GetComponent<SphereWaypoint>();
        }

        if (waypoints.Length > 0)
            agent.SetDestination(waypoints[index].transform.position);
    }

    void Update()
    {
        if (DetectObstacle())
        {
            agent.isStopped = true;
        }
        else
        {
            if (agent.isStopped)
                agent.isStopped = false;

            Move();
        }

        HandleStopTimer();

        HandleAnimation();
    }

    void Move()
    {
        if (waypoints.Length == 0) return;

        var currentWP = waypoints[index];

        // Stop at crossing start if light not red
        if (currentWP.isCrossingStart)
        {
            float distanceToWP = Vector3.Distance(transform.position, currentWP.transform.position);

            if (!CanCross(currentWP))
            {
                if (distanceToWP < 1f)
                {
                    agent.isStopped = true;
                    waitingAtCrossing = true;
                    return;
                }
            }
            else if (waitingAtCrossing)
            {
                agent.isStopped = false;
                waitingAtCrossing = false;
            }
        }

        if (!agent.pathPending && agent.remainingDistance < minDistance)
        {
            index++;

            if (index >= waypoints.Length)
                index = 0;

            agent.SetDestination(waypoints[index].transform.position);
        }
    }

    void HandleStopTimer()
    {
        if (agent.isStopped || agent.velocity.magnitude < 0.05f)
        {
            stopTimer += Time.deltaTime;

            if (stopTimer >= maxStopTime)
            {
                ResetPedestrian();
            }
        }
        else
        {
            stopTimer = 0f;
        }
    }

    void ResetPedestrian()
    {
        stopTimer = 0f;
        index = 0;

        agent.Warp(startPosition); // teleport safely on navmesh
        agent.ResetPath();

        if (waypoints.Length > 0)
            agent.SetDestination(waypoints[index].transform.position);

        agent.isStopped = false;
    }

    bool DetectObstacle()
    {
        Vector3 origin = transform.position + Vector3.up * sensorHeight;

        if (Physics.Raycast(origin, transform.forward, sensorLength, obstacleMask))
        {
            Debug.DrawRay(origin, transform.forward * sensorLength, Color.red);
            return true;
        }

        Debug.DrawRay(origin, transform.forward * sensorLength, Color.green);
        return false;
    }

    bool CanCross(SphereWaypoint wp)
    {
        if (wp.trafficLight == null) return true;

        // Example if you want traffic light logic later
        // return wp.trafficLight.GetState() == TrafficLightBase.State.Stop;

        return true;
    }

    void HandleAnimation()
    {
        if (animator != null)
            animator.SetFloat("Speed", agent.velocity.magnitude);
    }
}
