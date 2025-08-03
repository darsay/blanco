using System.Collections.Generic;
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

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            var names = playerNames.Value;

            foreach (var name in names)
            {
                AddNewPanel(name.ToString());
            }
        }
    }

    [ClientRpc]
    public void AddNewPlayerClientRpc(string name)
    {
        AddNewPanel(name);

        if(IsServer)
        {
            playerNames.Value.Add(new FixedString32Bytes(name));
        }
    }

    void AddNewPanel(string name)
    {
        var namePanel = Instantiate(PlayerNamePanelPrefab, playerNamesContainer);
        namePanel.SetName(name);
    }
}
