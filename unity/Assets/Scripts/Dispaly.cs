using UnityEngine;
using TMPro;

public class Display : MonoBehaviour
{
    [SerializeField] private Rigidbody carRigidbody;
    [SerializeField] private TextMeshProUGUI speedText;

    private void Update()
    {
        float speed = Vector3.Dot(carRigidbody.velocity,
                                  carRigidbody.transform.forward);

        // Convert to km/h (multiply by 3.6)
        float kmh = speed * 3.6f;

        speedText.text = $"Speed: {Mathf.Abs(kmh):0} km/h";
    }
}
