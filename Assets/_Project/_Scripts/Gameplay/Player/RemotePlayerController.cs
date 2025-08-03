using DG.Tweening;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class RemotePlayerController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform torsoRotator;
    [SerializeField] private Transform torsoTarget;
    [SerializeField] private PlayerActionsSync actionsSync;
    [SerializeField] private Rig rightHandRig;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            actionsSync.isPlayerPointing.OnValueChanged += OnPointingChanged;
        }
    }


    void Update()
    {
        if (IsOwner) return; // solo para otros jugadores


        Vector3 direction = actionsSync.cameraForward.Value;
        if (direction == Vector3.zero) return;

        torsoRotator.forward = direction;
        torsoTarget.position = torsoRotator.position + direction * 2f;
    }

    private void OnPointingChanged(bool previousValue, bool newValue)
    {
        float targetWeight = newValue ? 1f : 0f;

        DOTween.To(() => rightHandRig.weight, x => rightHandRig.weight = x, targetWeight, 0.3f);
    }
}
