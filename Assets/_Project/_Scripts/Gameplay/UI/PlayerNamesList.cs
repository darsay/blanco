using Blanco.UI;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerNamesList : NetworkBehaviour
{
    [SerializeField]
    PlayerNamePanel PlayerNamePanelPrefab;

    [SerializeField]
    Transform playerNamesContainer;

    public NetworkVariable<List<FixedString32Bytes>> playerNames = new NetworkVariable<List <FixedString32Bytes>> (default, writePerm: NetworkVariableWritePermission.Server);

    public void AddNewPanel(Blanco.Networking.LobbyManager.PlayerInfo player)
    {
        var namePanel = Instantiate(PlayerNamePanelPrefab, playerNamesContainer);
        namePanel.SetName(player);
    }

    public void ClearPlayerList()
    {
        if (playerNamesContainer != null)
        {
            foreach (Transform child in playerNamesContainer)
            {
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }
}
