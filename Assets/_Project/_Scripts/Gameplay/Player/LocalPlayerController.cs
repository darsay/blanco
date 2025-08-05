using DG.Tweening;
using System;
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
    public Rig LeftHandRig;
    public GameObject revolver;

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
        LeftHandRig.weight = 0f;
    }

    void Update()
    {
        if (!IsOwner) return;

        if(IsServer && MatchManager.Instance.currentState.Value == MatchManager.MatchState.WaitingForPlayers)
        {
            HandleBeginingMatch();
        }

        HandlePointing();
        HandleSeeCard();

        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;

        horizontalRotation += mouseX;
        verticalRotation -= mouseY;

        horizontalRotation = Mathf.Clamp(horizontalRotation, minHorizontalAngle, maxHorizontalAngle);
        verticalRotation = Mathf.Clamp(verticalRotation, minVerticalAngle, maxVerticalAngle);

        playerCamera.transform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
    }

    private void HandleBeginingMatch()
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            MatchManager.Instance.OnBeginMatch();
        }
    }

    void HandlePointing()
    {
        if (RoundManager.Instance.currentState.Value == RoundManager.RoundState.Voting)
            return;

        if (Input.GetMouseButtonDown(1))
        {
            Point(true);
        }
        else if(Input.GetMouseButtonUp(1))
        {
            Point(false);
        }
    }

    void HandleSeeCard()
    {
        if(RoundManager.Instance.currentState.Value != RoundManager.RoundState.Talking)
            return;

        if (Input.GetMouseButtonDown(0))
        {
           SeeCard(true);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            SeeCard(false);
        }
    }

    public void SeeCard(bool show)
    {
        var targetValue = show ? 1f : 0f;

        playerActionsSync.isPlayerCheckingCard.Value = show;
        DOTween.To(() => LeftHandRig.weight, x => LeftHandRig.weight = x, targetValue, 0.3f);
    }

    public void Point(bool active)
    {
        var targetValue = active ? 1f : 0f;

        playerActionsSync.isPlayerPointing.Value = active;
        DOTween.To(() => RightHandRig.weight, x => RightHandRig.weight = x, targetValue, 0.3f);
    }

    public void Aim(bool active)
    {
        revolver.SetActive(active);

        var targetValue = active ? 1f : 0f;

        playerActionsSync.isPlayerAiming.Value = active;
        revolver.SetActive(active);
        DOTween.To(() => RightHandRig.weight, x => RightHandRig.weight = x, targetValue, 0.3f);
    }
}
