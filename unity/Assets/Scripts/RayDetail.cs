using UnityEngine;

[ExecuteAlways]
public class RayDetail : MonoBehaviour
{
    [Header("Raycast Settings")]
    public float sensorLength = 40f;
    public LayerMask obstacleMask;

    [Header("Side Ray Detection")]
    public LayerMask sideMask;
    public float sideLength = 20f;
    public float sideOffset = 1.2f;
    public float forwardOffset = 2f;
    public enum HitGameObject
    {
        None,
        TrafficLight,
        Human,
        Car,
        Obstacle
    }


    private TrafficLight trafficLight = TrafficLight.None;
    private float rayDistance = 1f;
    private HitGameObject hitGameObject = HitGameObject.None;
    private int currentIntersectionID = -1;
    private int previousIntersectionID = -1;
    private string entryFaceName = "";

    public void ResetState()
    {
        currentIntersectionID = -1;
        previousIntersectionID = -1;
        entryFaceName = "";
        trafficLight = TrafficLight.None;
        hitGameObject = HitGameObject.None;
        rayDistance = 1f;
    }

    public (HitGameObject gameObject, float distance, TrafficLight trafficLight) GetRayInfo()
    {
        Vector3 origin = transform.position + Vector3.up * 1f;

        RaycastHit hit;

        if (Physics.Raycast(origin, transform.forward, out hit, sensorLength, obstacleMask))
        {
            rayDistance = Mathf.Clamp01(hit.distance / sensorLength);

            if (hit.collider.CompareTag("HumanNPC"))
            {
                hitGameObject = HitGameObject.Human;
                trafficLight = TrafficLight.None;
            }
            else if (hit.collider.CompareTag("CarNPC"))
            {
                hitGameObject = HitGameObject.Car;
                trafficLight = TrafficLight.None;
            }
            else if (hit.collider.CompareTag("entrypoint"))
            {
                IntersectionDetail intersection = hit.collider.GetComponent<IntersectionDetail>();
                if (intersection != null && currentIntersectionID != intersection.intersectionID)
                {
                    trafficLight = intersection.GetTrafficLightState();
                    hitGameObject = HitGameObject.TrafficLight;
                }
                else
                {
                    trafficLight = TrafficLight.None;
                    hitGameObject = HitGameObject.None;
                    rayDistance = 1f;
                }
            }
            else if (hit.collider.CompareTag("Obstacle"))
            {
                hitGameObject = HitGameObject.Obstacle;
                trafficLight = TrafficLight.None;
            }
        }
        else
        {
            rayDistance = 1f;
            hitGameObject = HitGameObject.None;
            trafficLight = TrafficLight.None;

        }
        // Debug.Log($"{hitGameObject}->{currentIntersectionID}->{trafficLight}->{entryFaceName}");


        Vector3[] sideOrigins =
        {
        origin + transform.right * sideOffset,
        origin - transform.right * sideOffset,
        origin + transform.right * sideOffset * 2,
        origin - transform.right * sideOffset * 2
        };

        foreach (var sideOrigin in sideOrigins)
        {
            Vector3 rayStart = sideOrigin + transform.forward * forwardOffset;

            if (Physics.Raycast(rayStart, transform.forward, out hit, sideLength, sideMask))
            {
                float sideDistance = Mathf.Clamp01(hit.distance / sensorLength);

                if (hit.collider.CompareTag("CarNPC"))
                {
                    if (sideDistance < rayDistance)
                    {
                        rayDistance = sideDistance;
                        hitGameObject = HitGameObject.Car;
                    }
                }
                else if (hit.collider.CompareTag("HumanNPC"))
                {
                    if (sideDistance < rayDistance)
                    {
                        rayDistance = sideDistance;
                        hitGameObject = HitGameObject.Human;

                    }
                }
            }
        }

        // Debug.Log($"{hitGameObject}->{rayDistance}");
        return (hitGameObject, rayDistance, trafficLight);
    }

    void OnDrawGizmos()
    {
        Vector3 origin = transform.position + Vector3.up * 1f;
        Vector3 direction = transform.forward;

        RaycastHit hit;

        if (Physics.Raycast(origin, direction, out hit, sensorLength, obstacleMask))
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, hit.point);
            Gizmos.DrawSphere(hit.point, 0.03f);
        }
        else
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + direction * sensorLength);
        }

        Vector3[] sideOrigins =
        {
        origin + transform.right * sideOffset,
        origin - transform.right * sideOffset,
        origin + transform.right * sideOffset * 2,
        origin - transform.right * sideOffset * 2
        };

        foreach (var sideOrigin in sideOrigins)
        {
            Vector3 rayStart = sideOrigin + transform.forward * forwardOffset;

            if (Physics.Raycast(rayStart, transform.forward, out hit, sideLength, sideMask))
            {
                Gizmos.color = Color.magenta; // color when HIT
                Gizmos.DrawLine(rayStart, hit.point);
            }
            else
            {
                Gizmos.color = Color.green; // color when NO HIT
                Gizmos.DrawLine(rayStart, rayStart + transform.forward * sideLength);
            }
        }

    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("entrypoint"))
        {
            IntersectionDetail ic = other.GetComponent<IntersectionDetail>();
            if (ic != null)
            {
                if (ic.intersectionID != currentIntersectionID && ic.intersectionID != previousIntersectionID)
                {
                    currentIntersectionID = ic.intersectionID;
                    entryFaceName = other.gameObject.name;
                    if (ic.GetTrafficLightState() == TrafficLight.Red)
                    {
                        SendMessage("OnRedLightViolation");
                    }

                    // Debug.Log($"Entered intersection {ic.intersectionID} from {entryFaceName}");
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("entrypoint"))
        {
            IntersectionDetail ic = other.GetComponent<IntersectionDetail>();
            if (ic != null)
            {
                if (ic.intersectionID == currentIntersectionID
                                         && other.gameObject.name != entryFaceName)
                {
                    previousIntersectionID = currentIntersectionID;
                    currentIntersectionID = -1;
                    entryFaceName = "";
                    // Debug.Log($"Truly exited intersection {ic.intersectionID}");
                }
            }
        }
    }
}

