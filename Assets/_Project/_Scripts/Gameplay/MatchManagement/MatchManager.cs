using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance;

    public enum MatchState : byte { WaitingForPlayers, Playing, Result }

    public NetworkVariable<MatchState> currentState = new NetworkVariable<MatchState>(MatchState.WaitingForPlayers, writePerm:NetworkVariableWritePermission.Server);
    private Dictionary<ulong, int> playersAndScores = new();

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if(!IsServer) return;
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
    }


    private void OnClientConnected(ulong clientId)
    {
        if(!IsServer) return;

        playersAndScores[clientId] = 0;
        UIManager.Instance.AddNewPlayerToPlayerList(clientId);
    }

    public void OnBeginMatch()
    {
        if(!IsServer) return;
        if (currentState.Value == MatchState.WaitingForPlayers)
        {
            currentState.Value = MatchState.Playing;
            RoundManager.Instance.StartGame();
            OnBeginMatchClientRpc();
        }
    }

    [ClientRpc]
    public void OnBeginMatchClientRpc()
    {
        UIManager.Instance.waitingUI.SetActive(false);
    }
}
