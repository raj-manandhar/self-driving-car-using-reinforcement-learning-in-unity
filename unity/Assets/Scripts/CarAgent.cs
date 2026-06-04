using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class CarAgent : Agent
{
    private Rigidbody rb;
    private RayPerceptionSensorComponent3D raySensor;
    private RayDetail rayDetail;

    [SerializeField] private WheelCollider frontLeftWheelCollider, frontRightWheelCollider;
    [SerializeField] private WheelCollider rearLeftWheelCollider, rearRightWheelCollider;

    [SerializeField] private Transform frontLeftWheelTransform, frontRightWheelTransform;
    [SerializeField] private Transform rearLeftWheelTransform, rearRightWheelTransform;

    [SerializeField] private float motorForce = 1500f;
    [SerializeField] private float maxSteerAngle = 30f;
    [SerializeField] private float brakeForce = 4000f;

    [SerializeField] private TestPath pathProvider;

    [SerializeField] private MinimapController minimapController;

    private List<Waypoint> path;
    private int currentWaypointIndex = 0;
    private float previousDistanceToWaypoint = 0f;
    private bool redLightViolated = false;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float currentSteerAngle;
    private float previousSteerAngle;
    private float previousSpeed;


    private int successfulEpisodes = 0;
    private int totalEpisodes = 0;
    private int windowSuccessCount = 0;

    private bool waitingForPath = false;


    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        raySensor = GetComponent<RayPerceptionSensorComponent3D>();
        rayDetail = GetComponent<RayDetail>();
    }

    public override void OnEpisodeBegin()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        previousSpeed = 0f;
        previousSteerAngle = 0f;
        currentSteerAngle = 0f;
        redLightViolated = false;
        totalEpisodes++;

        rayDetail.ResetState();

        // Freeze the agent until a path is chosen
        rb.isKinematic = true;
        waitingForPath = true;

        if (minimapController != null)
        {
            minimapController.RequestPathSelection((path, start, goal) =>
            {
                // Called when the user clicks Begin (or Random + Begin)
                waitingForPath = false;
                rb.isKinematic = false;

                // Path was already pushed to pathProvider by MinimapController.
                // SelectPath() will pick it up via HasManualPath.
                SelectPath();
            });
        }
        else
        {
            // No minimap — fall back to random immediately
            waitingForPath = false;
            rb.isKinematic = false;
            SelectPath();
        }
    }

    private void SelectPath()
    {
        if (pathProvider != null)
        {
            pathProvider.GenerateRandomPath();
            path = pathProvider.CurrentPath;
            currentWaypointIndex = 0;
        }

        if (path != null && path.Count > 1)
        {
            Vector3 startPos = path[0].transform.position;
            Vector3 nextPos = path[1].transform.position;
            Vector3 direction = (nextPos - startPos).normalized;

            transform.position = startPos;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
        else
        {
            transform.localPosition = startPosition;
            transform.localRotation = startRotation;
        }

        previousDistanceToWaypoint = path != null && path.Count > 0
            ? Vector3.Distance(transform.position, path[0].transform.position)
            : 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {

        float speed = Vector3.Dot(rb.velocity, transform.forward);
        sensor.AddObservation(Mathf.Clamp(speed / 20f, -1f, 1f));

        sensor.AddObservation(currentSteerAngle / maxSteerAngle);

        float center = LaneDetail.LaneCenter(raySensor);
        sensor.AddObservation(center);

        var (gameObject, distance, trafficLight) = rayDetail.GetRayInfo();
        sensor.AddObservation(gameObject == RayDetail.HitGameObject.None ? 1 : 0);
        sensor.AddObservation(gameObject == RayDetail.HitGameObject.TrafficLight ? 1 : 0);
        sensor.AddObservation(gameObject == RayDetail.HitGameObject.Human ? 1 : 0);
        sensor.AddObservation(gameObject == RayDetail.HitGameObject.Car ? 1 : 0);


        sensor.AddObservation(distance);

        sensor.AddObservation(trafficLight == TrafficLight.None ? 1 : 0);
        sensor.AddObservation(trafficLight == TrafficLight.Red ? 1 : 0);
        sensor.AddObservation(trafficLight == TrafficLight.Yellow ? 1 : 0);
        sensor.AddObservation(trafficLight == TrafficLight.Green ? 1 : 0);




        // --------------------------------------------------------------------------------------------------------------
        // var server = CameraStreamer.LatestResult;  // server input

        // // ── Lane ──
        // if (server != null)
        // {
        //     float lane = server.LaneConfidence;
        //     Debug.Log($"Lane Confidence Score: {lane}");
        //     if (lane < 0.99)
        //         sensor.AddObservation(center);
        //     else
        //         sensor.AddObservation(1 - lane); // lane score from server

        //     var detected = server.DetectedClasses; // string[]
        //     bool hasPedestrian = false;
        //     bool hasCar = false;
        //     bool hasTL_Red = false;
        //     bool hasTL_Yellow = false;
        //     bool hasTL_Green = false;

        //     if (detected != null && detected.Length > 0)
        //     {
        //         foreach (var cls in detected)
        //         {
        //             if (cls == "Pedistrian" && gameObject == RayDetail.HitGameObject.Human)
        //             {
        //                 hasPedestrian = true;
        //                 Debug.Log($"🚶 Pedestrian: {distance * 40f}m");
        //             }
        //             else if (cls == "Car" && gameObject == RayDetail.HitGameObject.Car)
        //             {
        //                 hasCar = true;
        //                 Debug.Log($"🚗 Car: {distance * 40f}m");
        //             }
        //             else if (cls == "Traffic-Red" && trafficLight == TrafficLight.Red)
        //             {
        //                 hasTL_Red = true;
        //                 Debug.Log($"🚦 Traffic Light - Red 🔴: {distance * 40f}m");

        //             }
        //             else if (cls == "Traffic-Yellow" && trafficLight == TrafficLight.Yellow)
        //             {
        //                 hasTL_Yellow = true;
        //                 Debug.Log($"🚦 Traffic Light - Yellow 🟡: {distance * 40f}m");
        //             }
        //             else if (cls == "Traffic-Green" && trafficLight == TrafficLight.Green)
        //             {
        //                 hasTL_Green = true;
        //                 Debug.Log($"🚦 Traffic Light - Green 🟢: {distance * 40f}m ");
        //             }
        //         }
        //     }

        //     // ── GameObject style observations ──
        //     bool noObject = (detected == null || detected.Length == 0) && gameObject == RayDetail.HitGameObject.None;
        //     bool noTrafficLight = !hasTL_Red && !hasTL_Yellow && !hasTL_Green && trafficLight == TrafficLight.None;

        //     sensor.AddObservation(noObject ? 1f : 0f);         // None
        //     sensor.AddObservation(noTrafficLight ? 1f : 0f);   // TrafficLight missing
        //     sensor.AddObservation(hasPedestrian ? 1f : 0f);   // Human
        //     sensor.AddObservation(hasCar ? 1f : 0f);          // Car

        //     sensor.AddObservation(distance);

        //     // ── Traffic light style observations ──
        //     sensor.AddObservation(!hasTL_Red && !hasTL_Yellow && !hasTL_Green ? 1f : 0f); // None
        //     sensor.AddObservation(hasTL_Red ? 1f : 0f);     // Red
        //     sensor.AddObservation(hasTL_Yellow ? 1f : 0f);  // Yellow
        //     sensor.AddObservation(hasTL_Green ? 1f : 0f);   // Green
        // }
        // else
        // {
        //     // server null → default all zeros except noObject = 1
        //     sensor.AddObservation(1f); // noObject
        //     sensor.AddObservation(0f); // noTrafficLight
        //     sensor.AddObservation(0f); // Human
        //     sensor.AddObservation(0f); // Car
        //     sensor.AddObservation(0f);
        //     sensor.AddObservation(1f); // trafficLight None
        //     sensor.AddObservation(0f); // Red
        //     sensor.AddObservation(0f); // Yellow
        //     sensor.AddObservation(0f); // Green
        // }
        // --------------------------------------------------------------------------------------------------------------




        if (path != null && currentWaypointIndex < path.Count)
        {
            Vector3 localTarget = transform.InverseTransformPoint(path[currentWaypointIndex].transform.position);
            sensor.AddObservation(Mathf.Clamp(localTarget.x / 20f, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(localTarget.z / 20f, -1f, 1f));
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {

        if (waitingForPath) return;

        float steerInput = actions.ContinuousActions[0];
        float accelerationInput = actions.ContinuousActions[1];
        float brakeInput = actions.ContinuousActions[2];

        HandleMotor(accelerationInput, brakeInput);
        HandleSteering(steerInput);
        UpdateWheels();


        float speed = Vector3.Dot(rb.velocity, transform.forward);
        float laneCenter = LaneDetail.LaneCenter(raySensor);
        var (hitObject, dist, trafficLight) = rayDetail.GetRayInfo();
        float distance = dist * 40f;

        // if (distance < 10f) Debug.Log(distance);

        bool cautionZone = hitObject != RayDetail.HitGameObject.None && distance < 24f;

        // ── Survival Reward ───────────────────────────────────────────────────
        AddReward(0.005f);

        // ── FLIP CHECK ─────────────────────────────────────────────────────────
        if (transform.up.y < 0.5f)
        {
            AddReward(-1.0f);
            EndEpisode();
            return;
        }

        // ── Red Light Violation ────────────────────────────────────────────────
        if (redLightViolated)
        {
            redLightViolated = false;
            AddReward(-2.0f);
            EndEpisode();
            return;
        }

        // ── WAYPOINT NAVIGATION ────────────────────────────────────
        if (path != null && currentWaypointIndex < path.Count)
        {
            Vector3 target = path[currentWaypointIndex].transform.position;
            float distanceToWaypoint = Vector3.Distance(transform.position, target);

            // Abort if agent wanders too far from the waypoint
            if (distanceToWaypoint > 36f)
            {
                AddReward(-1.0f);
                EndEpisode();
                return;
            }

            // Progress reward
            if (!cautionZone)
            {
                float delta = previousDistanceToWaypoint - distanceToWaypoint;
                AddReward(0.15f * delta);
            }

            // Heading aligngment
            Vector3 toTarget = (target - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, toTarget);
            float expectedAngle = Mathf.Atan2(2.6f, distanceToWaypoint) * Mathf.Rad2Deg;
            float angleError = angle - expectedAngle;
            AddReward(-0.02f * (angleError / 180f));

            // Speed control for sharp turns
            if (angle > 30f && distanceToWaypoint < 10f)
            {
                float t = Mathf.Clamp01((angle - 30f) / 60f);
                float expectedSpeed = Mathf.Lerp(5f, 1f, t);
                if (speed > expectedSpeed)
                    AddReward(-0.03f * (speed - expectedSpeed));
            }

            // Lane-centering during navigation
            AddReward(-0.05f * Mathf.Abs(laneCenter));

            // Waypoint reached
            if (distanceToWaypoint < 6f)
            {
                AddReward(0.2f);
                currentWaypointIndex++;

                if (currentWaypointIndex >= path.Count)
                {
                    successfulEpisodes++;
                    windowSuccessCount++;

                    if (totalEpisodes % 100 == 0)
                    {
                        Debug.Log($"[Episode {totalEpisodes}]: {windowSuccessCount}/100 | Total: {successfulEpisodes}/{totalEpisodes} ({(float)successfulEpisodes / totalEpisodes:P1})");
                        windowSuccessCount = 0;
                    }

                    AddReward(5.0f);
                    EndEpisode();
                    return;
                }
            }

            previousDistanceToWaypoint = distanceToWaypoint;
        }

        // ── SPEED REWARDS ──────────────────────────────────────────
        if (!cautionZone)
        {
            AddReward(Mathf.Clamp(speed / 20f, 0f, 1f) * 0.02f);
            if (speed > 2f)
                AddReward(0.001f);
        }

        if (speed > 18f)
            AddReward(-0.004f * (speed - 18f));

        // ── STEERING SMOOTHNESS ────────────────────────────────────
        float steerChange = Mathf.Abs(currentSteerAngle - previousSteerAngle);
        previousSteerAngle = currentSteerAngle;
        float steerPenalty = steerChange / maxSteerAngle * (1f - Mathf.Exp(-5f * Mathf.Abs(laneCenter)));
        AddReward(-0.004f * steerPenalty);

        // ── LANE CENTERING ─────────────────────────────────────────
        AddReward(0.012f * Mathf.Exp(-10f * laneCenter * laneCenter));

        // ── OBSTACLE BRAKING ────────────────────────────────────────
        if (hitObject != RayDetail.HitGameObject.None && trafficLight != TrafficLight.Green)
        {
            float stopDistance = 7f;
            float driftDistance = 35f;

            float t = Mathf.InverseLerp(stopDistance, driftDistance, distance);
            float expectedSpeed = Mathf.Lerp(0f, 16f, t);
            float speedError = speed - expectedSpeed;

            if (speedError > 0f)
                AddReward(-0.15f * Mathf.Clamp(speedError / 10f, 0f, 1f));
            else
                AddReward(0.05f * Mathf.Exp(-Mathf.Abs(speedError)));

            if (distance < stopDistance)
            {
                AddReward(speed > 0.3f ? -0.15f : 0.08f);//0.10
            }

            float deceleration = previousSpeed - speed;
            if (deceleration > 0f && deceleration < 2f && speed > 1f && distance < driftDistance)
            {
                float urgency = 1f - t;
                AddReward(0.02f * urgency);
            }

            if (deceleration > 2f)
                AddReward(-0.05f * Mathf.Clamp((deceleration - 2f) / 3f, 0f, 1f));

            if (distance > 16f)
            {
                if (speed < 1f)
                    AddReward(-0.10f);
                else if (speed > 3f)
                    AddReward(0.03f);
            }
        }
        previousSpeed = speed;

        // ── NPC DETECTION ──────────────────────────────────────────
        float safeDistance = 8f;

        if (hitObject == RayDetail.HitGameObject.Human || hitObject == RayDetail.HitGameObject.Car)
        {
            if (distance >= safeDistance)
                AddReward(0.40f);
            else
            {
                float penalty = Mathf.Clamp(safeDistance - distance, 0, safeDistance) / safeDistance;
                AddReward(-0.6f * penalty);
            }
        }

        // ── TRAFFIC LIGHT ──────────────────────────────────────────
        switch (trafficLight)
        {
            case TrafficLight.Red:
                {
                    if (speed < 0.3f)
                    {
                        if (distance >= 6f && distance <= 8f)
                            AddReward(0.24f);               // optimal stop zone
                        else if (distance < 6f)
                            AddReward(-0.01f * (6f - distance));
                    }
                    else if (speed >= 0.3f && distance < 6f)
                    {
                        // moving and too close — graduated penalty
                        AddReward(-0.01f * (6f - distance));
                    }
                    break;
                }

            case TrafficLight.Yellow:
                if (distance < 20f)
                {
                    if (speed > 6f)
                        AddReward(-0.08f);
                    else
                        AddReward(0.05f);
                }
                break;

            case TrafficLight.Green:
                if (speed < 1f)
                    AddReward(-0.10f);
                else
                    AddReward(0.05f);
                break;

            default:
                if (speed < 0.1f && hitObject == RayDetail.HitGameObject.None)
                    AddReward(-0.05f);
                break;
        }
    }

    private void OnRedLightViolation()
    {
        redLightViolated = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("sideway"))
        {
            AddReward(-1.0f);
            EndEpisode();

        }

        else if (collision.gameObject.CompareTag("HumanNPC"))
        {
            AddReward(-28.0f);
            EndEpisode();
        }

        else if (collision.gameObject.CompareTag("CarNPC"))
        {
            AddReward(-27.0f);
            EndEpisode();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
    }

    private void HandleMotor(float acceleration, float brake)
    {
        frontLeftWheelCollider.motorTorque = acceleration * motorForce;
        frontRightWheelCollider.motorTorque = acceleration * motorForce;

        ApplyBraking(brake > 0f ? brakeForce * brake : 0f);
    }

    private void ApplyBraking(float brake)
    {
        frontRightWheelCollider.brakeTorque = brake;
        frontLeftWheelCollider.brakeTorque = brake;
        rearLeftWheelCollider.brakeTorque = brake;
        rearRightWheelCollider.brakeTorque = brake;
    }

    private void HandleSteering(float steerInput)
    {
        currentSteerAngle = maxSteerAngle * steerInput;
        frontLeftWheelCollider.steerAngle = currentSteerAngle;
        frontRightWheelCollider.steerAngle = currentSteerAngle;
    }

    private void UpdateWheels()
    {
        UpdateSingleWheel(frontLeftWheelCollider, frontLeftWheelTransform);
        UpdateSingleWheel(frontRightWheelCollider, frontRightWheelTransform);
        UpdateSingleWheel(rearRightWheelCollider, rearRightWheelTransform);
        UpdateSingleWheel(rearLeftWheelCollider, rearLeftWheelTransform);
    }

    private void UpdateSingleWheel(WheelCollider wheelCollider, Transform wheelTransform)
    {
        Vector3 pos;
        Quaternion rot;
        wheelCollider.GetWorldPose(out pos, out rot);
        wheelTransform.rotation = rot;
        wheelTransform.position = pos;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetAxis("Horizontal");
        continuousActions[1] = Input.GetAxis("Vertical");
        continuousActions[2] = Input.GetKey(KeyCode.Space) ? 1.0f : 0.0f;
    }


}