using UnityEngine;
using HealthbarGames;

public enum TrafficLight
{
    None,
    Red,
    Yellow,
    Green
}

public class IntersectionDetail : MonoBehaviour
{
    [Header("Intersection ID and corresponding TrafficLight")]
    public int intersectionID;
    public TrafficLightBase trafficLight;

    public TrafficLight GetTrafficLightState()
    {
        if (trafficLight == null)
            return TrafficLight.None;

        switch (trafficLight.GetState())
        {
            case TrafficLightBase.State.Stop:
                return TrafficLight.Red;

            case TrafficLightBase.State.PrepareToGo:
            case TrafficLightBase.State.PrepareToStop:
                return TrafficLight.Yellow;

            case TrafficLightBase.State.Go:
                return TrafficLight.Green;

            default:
                return TrafficLight.None;
        }
    }
}