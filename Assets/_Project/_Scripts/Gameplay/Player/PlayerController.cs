using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [Header("References")]
    public CinemachineCamera playerCamera;

    [Header("Player Prefabs")]
    public GameObject owningPlayer;
    public GameObject nonOwningPlayer;



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
        }
        else
        {
            owningPlayer.SetActive(false);
            nonOwningPlayer.SetActive(true);
            playerCamera.enabled = false;
        }
    }

}
