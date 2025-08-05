using System;
using TMPro;
using Unity.Cinemachine;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [Header("References")]
    public CinemachineCamera playerCamera;

    [Header("Card")]
    [SerializeField] CardController owningCard;
    [SerializeField] CardController nonOwningCard;

    [Header("Player Prefabs")]
    [SerializeField] GameObject owningPlayer;
    [SerializeField] GameObject nonOwningPlayer;

    [Header("Player Controllers")]
    [SerializeField] LocalPlayerController localPlayerController;

    [SerializeField]
    PlayerTag playerTag;


    public CardController Card;
    

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            owningPlayer.SetActive(true);
            nonOwningPlayer.SetActive(false);
            playerCamera.enabled = true;
            Card = owningCard;
        }
        else
        {
            owningPlayer.SetActive(false);
            nonOwningPlayer.SetActive(true);
            playerCamera.enabled = false;
            Card = nonOwningCard;
        }

        SetPlayerName($"Player {OwnerClientId}");
    }

    [ClientRpc]
    public void SetCardValuesClientRpc(FixedString32Bytes word, ulong blancoId)
    {
        if(!IsOwner) return;

        if (OwnerClientId == blancoId)
        {
            Card.SetWord("Blanco");
        }
        else
        {
            Card.SetWord(word.ToString());
        }
    }

    public void SetPlayerName(string name)
    {
        playerTag.SetName(name);
    }

    [ClientRpc]
    public void ShowCardClientRpc(bool show)
    {
        if (IsOwner)
        {
            localPlayerController.SeeCard(show);
        }
    }

    [ClientRpc]
    public void PointClientRpc(bool active)
    {
        if (IsOwner)
        {
            localPlayerController.Point(active);
        }
    }

    [ClientRpc]
    public void AimClientRpc(bool active)
    {
        if (IsOwner)
        {
            localPlayerController.Aim(active);
        }
    }


}