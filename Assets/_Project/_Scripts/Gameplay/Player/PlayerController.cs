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


    public CardController Card;
    

    void Start()
    {
        //Cursor.lockState = CursorLockMode.Locked;
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
    }

    [ClientRpc]
    public void SetCardValuesClientRpc(FixedString32Bytes word, ulong blancoId)
    {
        if(!IsOwner) return;

        if (OwnerClientId.Equals(RoundManager.Instance.blancoPlayerId.Value))
        {
            Card.SetWord("Blanco");
        }
        else
        {
            Debug.Log($"Setting card word for player {OwnerClientId} to {word}. Blanco {blancoId}");
            Card.SetWord(word.ToString());
        }
    }

}
