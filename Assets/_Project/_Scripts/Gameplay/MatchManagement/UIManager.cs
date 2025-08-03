using Unity.Netcode;
using UnityEngine;

public class UIManager : NetworkBehaviour
{
    public static UIManager Instance;

    public GameObject pressToBegin;

    public GameObject waitingUI;

    [SerializeField]
    PlayerNamesList playerNamesList;

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        pressToBegin.SetActive(IsServer);
    }

    public void AddNewPlayerToPlayerList(ulong id)
    {
        playerNamesList.AddNewPlayerClientRpc($"Player {id}");
    }
}
