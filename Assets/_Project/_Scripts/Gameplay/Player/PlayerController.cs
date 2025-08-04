using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    public Transform torsoIkTarget;
    public Transform handIkTarget;

    [Header("Sensitivity")]
    public float sensitivity = 100f;

    [Header("Rotation Limits")]
    public float minVerticalAngle = -45f;
    public float maxVerticalAngle = 45f;
    public float minHorizontalAngle = -90f;
    public float maxHorizontalAngle = 90f;

    [Header("IK Target")]
    public float ikTargetDistance = 2f;

    private float verticalRotation = 0f;
    private float horizontalRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;

        horizontalRotation += mouseX;
        verticalRotation -= mouseY;

        horizontalRotation = Mathf.Clamp(horizontalRotation, minHorizontalAngle, maxHorizontalAngle);
        verticalRotation = Mathf.Clamp(verticalRotation, minVerticalAngle, maxVerticalAngle);

        cameraTransform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);

        // Posiciona el IK target frente a la cámara
        if (torsoIkTarget != null)
        {
            torsoIkTarget.position = cameraTransform.position + cameraTransform.forward * ikTargetDistance;
            handIkTarget.position = cameraTransform.position + cameraTransform.forward * 0.5f;
        }
    }
}
