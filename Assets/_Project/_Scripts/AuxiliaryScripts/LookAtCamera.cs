using UnityEngine;

public class LookAtCamera : MonoBehaviour
{
    private Transform mainCamera;

    void Start()
    {
        mainCamera = Camera.main.transform;
    }

    void Update()
    {
        if (mainCamera == null) return;

        Vector3 targetPosition = mainCamera.position;

        targetPosition.y = transform.position.y;

        transform.LookAt(targetPosition);
    }
}
