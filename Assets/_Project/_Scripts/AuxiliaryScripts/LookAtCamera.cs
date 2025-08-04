using UnityEngine;

public class LookAtCamera : MonoBehaviour
{
    private Camera mainCamera = null;

    public void SetCamera(Camera camera)
    {
        mainCamera = camera;
    }

    void Update()
    {
        if (mainCamera == null) return;

        Vector3 targetPosition = mainCamera.transform.position;

        targetPosition.y = transform.position.y;

        transform.LookAt(targetPosition);
    }
}
