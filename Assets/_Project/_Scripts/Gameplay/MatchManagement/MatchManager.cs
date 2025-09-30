using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance;

    public enum MatchState : byte { WaitingForPlayers, Playing, Result }

    public NetworkVariable<MatchState> currentState = new NetworkVariable<MatchState>(MatchState.WaitingForPlayers, writePerm: NetworkVariableWritePermission.Server);
    private readonly Dictionary<ulong, int> playersAndScores = new();

    void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (!NetworkManager.Singleton.IsHost) return;
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
        if (!NetworkManager.Singleton.IsHost) return;

        playersAndScores[clientId] = 0;
    }

    public void OnBeginMatch()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        if (currentState.Value == MatchState.Playing)
            return;

        currentState.Value = MatchState.Playing;
        RoundManager.Instance?.StartGame();
        OnBeginMatchClientRpc();
    }

    [ClientRpc]
    private void OnBeginMatchClientRpc()
    {
        if (UIGameplayManager.Instance != null && UIGameplayManager.Instance.waitingUI != null)
        {
            UIGameplayManager.Instance.waitingUI.SetActive(false);
        }
    }

    public void ShowWaitingUI()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        ShowWaitingUIClientRpc();
    }

    [ClientRpc]
    private void ShowWaitingUIClientRpc()
    {
        if (UIGameplayManager.Instance != null && UIGameplayManager.Instance.waitingUI != null)
        {
            UIGameplayManager.Instance.waitingUI.SetActive(true);
        }
    }
}
