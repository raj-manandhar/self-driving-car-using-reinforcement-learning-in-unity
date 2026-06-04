using UnityEngine;
using Unity.MLAgents.Sensors;

public static class LaneDetail
{
    static float MinLeftEdgeDistance(RayPerceptionSensorComponent3D raySensor)
    {
        if (raySensor == null) return 0f;

        RayPerceptionInput rayInput = raySensor.GetRayPerceptionInput();
        RayPerceptionOutput rayOutput = RayPerceptionSensor.Perceive(rayInput, false);

        float minDistance = float.MaxValue;
        int totalRays = rayOutput.RayOutputs.Length;

        for (int i = 2; i < totalRays; i += 2)
        {
            var ray = rayOutput.RayOutputs[i];
            if (ray.HasHit && ray.HitGameObject.CompareTag("sideway"))
            {
                float hitDistance = ray.HitFraction * rayInput.RayLength;
                if (hitDistance < minDistance)
                    minDistance = hitDistance;
            }
        }
        return minDistance;
    }

    public static float LaneCenter(RayPerceptionSensorComponent3D raySensor)
    {
        float distance = MinLeftEdgeDistance(raySensor);
        return Mathf.Clamp((distance - 2.2f) / 8f, -1f, 1f);
    }

}
