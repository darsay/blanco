using TMPro;
using Unity.Netcode;
using UnityEngine;

public class UIManager : NetworkBehaviour
{
    public static UIManager Instance;

    public GameObject pressToBegin;

    public GameObject waitingUI;

    [SerializeField]
    PlayerNamesList playerNamesList;

    [SerializeField]
    TextMeshProUGUI infoText;

    [SerializeField]
    GameTimer gameTimer;

    void Awake()
    {
        Instance = this;
        infoText.gameObject.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        pressToBegin.SetActive(IsServer);
    }

    public void AddNewPlayerToPlayerList(ulong id)
    {
        playerNamesList.AddNewPlayerClientRpc($"Player {id}");
    }

    [ClientRpc]
    public void SetInfoTextClientRpc(string text)
    {
        infoText.gameObject.SetActive(true);
        infoText.text = text;
    }

    [ClientRpc]
    public void HideInfoTextClientRpc()
    {
        infoText.gameObject.SetActive(false);
    }

    public void StartGameTimer(float duration)
    {
        gameTimer.SetVisibility(true);
        gameTimer.StartTimerServerRpc(duration);
    }
}
