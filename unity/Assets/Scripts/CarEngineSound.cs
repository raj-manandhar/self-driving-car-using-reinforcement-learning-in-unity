using UnityEngine;

public class CarEngineSound : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource engineAudioSource;

    [Header("Pitch Settings")]
    [SerializeField] private float minPitch = 0.6f;      // idle pitch
    [SerializeField] private float maxPitch = 2.2f;      // max speed pitch

    [Header("Volume Settings")]
    [SerializeField] private float minVolume = 0.3f;     // idle volume
    [SerializeField] private float maxVolume = 1.0f;     // max speed volume

    [Header("Speed Reference")]
    [SerializeField] private float maxSpeed = 18f;       // matches your CarAgent max speed

    [SerializeField] private float smoothSpeed = 5f;     // how fast pitch transitions

    private Rigidbody rb;
    private float targetPitch;
    private float targetVolume;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (engineAudioSource == null)
            engineAudioSource = GetComponent<AudioSource>();

        // Make sure audio loops
        engineAudioSource.loop = true;
        engineAudioSource.Play();
    }

    private void Update()
    {
        // Get forward speed (same method as your CarAgent)
        float speed = Vector3.Dot(rb.velocity, transform.forward);
        float speedRatio = Mathf.Clamp01(Mathf.Abs(speed) / maxSpeed);

        // Map speed to pitch and volume
        targetPitch = Mathf.Lerp(minPitch, maxPitch, speedRatio);
        targetVolume = Mathf.Lerp(minVolume, maxVolume, speedRatio);

        // Smooth the transition
        engineAudioSource.pitch = Mathf.Lerp(engineAudioSource.pitch, targetPitch, Time.deltaTime * smoothSpeed);
        engineAudioSource.volume = Mathf.Lerp(engineAudioSource.volume, targetVolume, Time.deltaTime * smoothSpeed);
    }
}