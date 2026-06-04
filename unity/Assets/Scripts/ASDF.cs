using System.Collections.Generic;
using UnityEngine;
using HealthbarGames;

[System.Serializable]
public class WayInfo
{
    public Transform waypoint;
    public TrafficLightBase trafficLight;
}

public class ASDF : MonoBehaviour
{
    [Header("Waypoint Parent")]
    public Transform waypointParent;   // Parent containing all waypoint children
    public float waypointReachDistance = 5f;

    private WayInfo[] waypointsInfo;
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

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.5f, 0);

        // Auto-fill waypointsInfo from waypoint parent
        List<WayInfo> temp = new List<WayInfo>();
        foreach (Transform child in waypointParent)
        {
            CubeWaypoint wp = child.GetComponent<CubeWaypoint>();
            temp.Add(new WayInfo
            {
                waypoint = child,
                trafficLight = wp != null ? wp.trafficLight : null
            });
        }
        waypointsInfo = temp.ToArray();
    }

    void FixedUpdate()
    {
        if (waypointsInfo == null || waypointsInfo.Length == 0) return;

        HandleSensors();
        HandleSteering();
        HandleMotor();
        UpdateWheelVisuals();
        CheckWaypoint();
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
                currentWaypoint = 0;
        }
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