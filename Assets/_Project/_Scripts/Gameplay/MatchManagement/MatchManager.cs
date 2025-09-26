using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor.PackageManager;
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

    private void Start()
    {
        if(!NetworkManager.Singleton.IsHost) return;
        playersAndScores[0] = 0;
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDisable()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        NetworkManager.OnClientConnectedCallback -= OnClientConnected;
    }


    private void OnClientConnected(ulong clientId)
    {
        if(!NetworkManager.Singleton.IsHost) return;

        playersAndScores[clientId] = 0;
    }

    public void OnBeginMatch()
    {
        if(!NetworkManager.Singleton.IsHost) return;
        if (currentState.Value == MatchState.WaitingForPlayers)
        {
            currentState.Value = MatchState.Playing;
            RoundManager.Instance.StartGame();
            OnBeginMatchClientRpc();
        }
    }

    [ClientRpc]
    private void OnBeginMatchClientRpc()
    {
        UIGameplayManager.Instance.waitingUI.SetActive(false);
    }
}
