using System.Collections.Generic;
using UnityEngine;
using HealthbarGames;

[System.Serializable]
public class WaypointInfo
{
    public Transform waypoint;
    public TrafficLightBase trafficLight;
}

public class NPCCarController : MonoBehaviour
{
    [Header("Waypoint Parent")]
    public Transform waypointParent;   // Parent containing all waypoint children
    public float waypointReachDistance = 5f;

    private WaypointInfo[] waypointsInfo;
    private int currentWaypoint = 0;

    [Header("Wheel Colliders")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    [Header("Wheel Meshes")]
    public Transform frontLeftMesh;
    public Transform frontRightMesh;
    public Transform rearLeftMesh;
    public Transform rearRightMesh;

    [Header("Car Settings")]
    public float maxMotorTorque = 500f;
    public float maxSteerAngle = 30f;
    public float maxSpeed = 40f;
    public float brakeForce = 5000f;

    [Header("Sensors")]
    public float sensorLength = 4f;
    public float sensorSideOffset = 0.8f;
    public float sensorForwardOffset = 1.5f;
    public float sensorUpwardOffset = 1f;
    public LayerMask obstacleMask;

    private Rigidbody rb;
    private float currentSteer;
    private float currentTorque;
    private bool obstacleAhead;

    private Vector3 initialPosition;
    private Quaternion initialRotation;

    public float stuckTimeLimit = 60f;
    public float movementThreshold = 0.5f;

    private Vector3 lastPosition;
    private float stuckTimer = 0f;


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.5f, 0);

        initialPosition = transform.position;
        initialRotation = transform.rotation;

        // Auto-fill waypointsInfo from waypoint parent
        List<WaypointInfo> temp = new List<WaypointInfo>();
        foreach (Transform child in waypointParent)
        {
            CubeWaypoint wp = child.GetComponent<CubeWaypoint>();
            temp.Add(new WaypointInfo
            {
                waypoint = child,
                trafficLight = wp != null ? wp.trafficLight : null
            });
        }
        waypointsInfo = temp.ToArray();

        lastPosition = transform.position;
    }

    void FixedUpdate()
    {
        if (waypointsInfo == null || waypointsInfo.Length == 0) return;

        HandleSensors();
        HandleSteering();
        HandleMotor();
        UpdateWheelVisuals();
        CheckWaypoint();
        CheckIfStuck();

    }

    // ---------------- SENSORS ----------------
    void HandleSensors()
    {
        obstacleAhead = false;

        Vector3 origin = transform.position + transform.up * sensorUpwardOffset + transform.forward * sensorForwardOffset;

        Vector3[] offsets =
        {
            Vector3.zero,
            transform.right * sensorSideOffset,
            -transform.right * sensorSideOffset,
            transform.right * sensorSideOffset * 0.5f,
            -transform.right * sensorSideOffset * 0.5f
        };

        foreach (Vector3 offset in offsets)
        {
            Vector3 rayOrigin = origin + offset;

            if (Physics.Raycast(rayOrigin, transform.forward, sensorLength, obstacleMask))
            {
                obstacleAhead = true;
                Debug.DrawRay(rayOrigin, transform.forward * sensorLength, Color.red);
                return;
            }

            Debug.DrawRay(rayOrigin, transform.forward * sensorLength, Color.green);
        }
    }

    // ---------------- STEERING ----------------
    void HandleSteering()
    {
        Vector3 target = waypointsInfo[currentWaypoint].waypoint.position;
        Vector3 localTarget = transform.InverseTransformPoint(target);

        float steerPercent = localTarget.x / localTarget.magnitude;
        currentSteer = steerPercent * maxSteerAngle;

        frontLeft.steerAngle = currentSteer;
        frontRight.steerAngle = currentSteer;
    }

    // ---------------- MOTOR ----------------
    void HandleMotor()
    {
        float speed = rb.linearVelocity.magnitude * 3.6f; // km/h

        if (obstacleAhead || speed > maxSpeed || IsStopAtWaypoint())
        {
            ApplyBrakes();
            currentTorque = 0f;
        }
        else
        {
            ReleaseBrakes();

            float steerSlowdown = Mathf.Abs(currentSteer) / maxSteerAngle;
            float speedLimit = Mathf.Lerp(maxSpeed, maxSpeed * 0.35f, steerSlowdown);

            currentTorque = speed < speedLimit ? maxMotorTorque : 0f;
        }

        rearLeft.motorTorque = currentTorque;
        rearRight.motorTorque = currentTorque;
    }

    // ---------------- RED LIGHT CHECK ----------------
    bool IsStopAtWaypoint()
    {
        var light = waypointsInfo[currentWaypoint].trafficLight;
        if (light == null) return false;

        var state = light.GetState();

        if (state == TrafficLightBase.State.Stop ||
            state == TrafficLightBase.State.PrepareToStop)
        {
            float distance = Vector3.Distance(
                transform.position,
                waypointsInfo[currentWaypoint].waypoint.position
            );

            if (distance < 15f)
                return true;
        }

        return false;
    }

    // ---------------- BRAKES ----------------
    void ApplyBrakes()
    {
        frontLeft.brakeTorque = brakeForce;
        frontRight.brakeTorque = brakeForce;
        rearLeft.brakeTorque = brakeForce;
        rearRight.brakeTorque = brakeForce;
    }

    void ReleaseBrakes()
    {
        frontLeft.brakeTorque = 0f;
        frontRight.brakeTorque = 0f;
        rearLeft.brakeTorque = 0f;
        rearRight.brakeTorque = 0f;
    }

    // ---------------- WAYPOINT CHECK ----------------
    void CheckWaypoint()
    {
        if (Vector3.Distance(transform.position, waypointsInfo[currentWaypoint].waypoint.position) < waypointReachDistance)
        {
            currentWaypoint++;
            if (currentWaypoint >= waypointsInfo.Length)
            {
                transform.position = initialPosition;
                transform.rotation = initialRotation;
                currentWaypoint = 0;
            }
        }

    }

    // ---------------- STUCK DETECTION ----------------
    void CheckIfStuck()
    {
        float movedDistance = Vector3.Distance(transform.position, lastPosition);

        if (movedDistance < movementThreshold)
        {
            stuckTimer += Time.fixedDeltaTime;

            if (stuckTimer > stuckTimeLimit)
            {
                ResetCar();
            }
        }
        else
        {
            stuckTimer = 0f;
            lastPosition = transform.position;
        }
    }

    // ---------------- RESET CAR ----------------
    void ResetCar()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = initialPosition;
        transform.rotation = initialRotation;

        currentWaypoint = 0;
        stuckTimer = 0f;
    }



    // ---------------- WHEEL VISUALS ----------------
    void UpdateWheelVisuals()
    {
        UpdateWheel(frontLeft, frontLeftMesh);
        UpdateWheel(frontRight, frontRightMesh);
        UpdateWheel(rearLeft, rearLeftMesh);
        UpdateWheel(rearRight, rearRightMesh);
    }

    void UpdateWheel(WheelCollider col, Transform mesh)
    {
        Vector3 pos;
        Quaternion rot;
        col.GetWorldPose(out pos, out rot);
        mesh.position = pos;
        mesh.rotation = rot;
    }
}