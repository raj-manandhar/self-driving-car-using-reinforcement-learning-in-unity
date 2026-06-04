using UnityEngine;
using System.Collections;

public class RandomCube : MonoBehaviour
{
    public float slideDistance = 15f;
    public float speed = 2f;

    public float minWait = 4f;
    public float maxWait = 8f;

    public float minStartDelay = 0f;
    public float maxStartDelay = 6f;

    private Vector3 closedPosition;
    private Vector3 openPosition;

    void Start()
    {
        closedPosition = transform.position;
        openPosition = closedPosition + new Vector3(0, 0, slideDistance);

        StartCoroutine(StartRandom());
    }

    IEnumerator StartRandom()
    {
        // random delay so cubes don't start together
        yield return new WaitForSeconds(Random.Range(minStartDelay, maxStartDelay));

        StartCoroutine(DoorLoop());
    }

    IEnumerator DoorLoop()
    {
        while (true)
        {
            yield return MoveDoor(openPosition);

            yield return new WaitForSeconds(Random.Range(minWait, maxWait));

            yield return MoveDoor(closedPosition);

            yield return new WaitForSeconds(Random.Range(minWait, maxWait));
        }
    }

    IEnumerator MoveDoor(Vector3 target)
    {
        while (Vector3.Distance(transform.position, target) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                target,
                speed * Time.deltaTime
            );
            yield return null;
        }
    }
}