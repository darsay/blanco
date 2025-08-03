using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance;

    public WordListSO wordList;

    public enum RoundState : byte { Inactive, ShowingCards, Talking, Voting, Result }
    public NetworkVariable<RoundState> currentState = new NetworkVariable<RoundState>(RoundState.Inactive, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString32Bytes> chosenWord = new NetworkVariable<FixedString32Bytes>(default, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> blancoPlayerId = new NetworkVariable<ulong>(default, writePerm: NetworkVariableWritePermission.Server);

    void Awake()
    {
        Instance = this;
    }

    public void StartGame()
    {
        if (!IsServer) return;
        StartRound();
    }

    public void StartRound()
    {
        if (!IsServer) return;
        SetRandomWord();
        PickBlancoPlayer();
        SetCardsValues();
        currentState.Value = RoundState.ShowingCards;
    }

    void PickBlancoPlayer()
    {
        if (NetworkManager.Singleton.ConnectedClients.Count == 0)
        {
            Debug.LogError("No players connected to pick a Blanco player.");
            return;
        }
        var idx = Random.Range(0, NetworkManager.Singleton.ConnectedClientsList.Count);

        blancoPlayerId.Value = NetworkManager.Singleton
            .ConnectedClientsList[idx].ClientId;

        Debug.Log($"Blanco player chosen: {blancoPlayerId.Value}");
    }

    void SetRandomWord()
    {
        if (wordList == null || wordList.Words.Length == 0)
        {
            Debug.LogError("Word list is empty or not assigned.");
            return;
        }
        int randomIndex = Random.Range(0, wordList.Words.Length);
        chosenWord.Value = wordList.Words[randomIndex];
    }

    void SetCardsValues()
    {
        if (NetworkManager.Singleton.ConnectedClients.Count == 0)
        {
            Debug.LogError("No players connected to set card values.");
            return;
        }
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player != null)
            {
                player.SetCardValuesClientRpc(chosenWord.Value, blancoPlayerId.Value);
            }
        }
    }
}
