using DG.Tweening;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using static System.TimeZoneInfo;

public class LocalPlayerController : NetworkBehaviour
{
    [Header("References")]
    public PlayerActionsSync playerActionsSync;
    public CinemachineCamera playerCamera;
    public Rig RightHandRig;

    [Header("Sensitivity")]
    public float sensitivity = 100f;

    [Header("Rotation Limits")]
    public float minVerticalAngle = -45f;
    public float maxVerticalAngle = 45f;
    public float minHorizontalAngle = -90f;
    public float maxHorizontalAngle = 90f;

    private float verticalRotation = 0f;
    private float horizontalRotation = 0f;

    private void Start()
    {
        RightHandRig.weight = 0f;
    }

    void Update()
    {
        if (!IsOwner) return;

        HandlePointing();

        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;

        horizontalRotation += mouseX;
        verticalRotation -= mouseY;

        horizontalRotation = Mathf.Clamp(horizontalRotation, minHorizontalAngle, maxHorizontalAngle);
        verticalRotation = Mathf.Clamp(verticalRotation, minVerticalAngle, maxVerticalAngle);

        playerCamera.transform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
    }

    void HandlePointing()
    {
        if (Input.GetMouseButtonDown(1))
        {
            playerActionsSync.isPlayerPointing.Value = true;
            DOTween.To(() => RightHandRig.weight, x => RightHandRig.weight = x, 1f, 0.3f);
        }
        else if(Input.GetMouseButtonUp(1))
        {
            playerActionsSync.isPlayerPointing.Value = false;
            DOTween.To(() => RightHandRig.weight, x => RightHandRig.weight = x, 0f, 0.3f);
        }
    }
}
