using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance;

    public WordListSO wordList;

    public enum RoundState : byte { Inactive, ShowingCards, SayWord, Talking, Voting, Result }
    public NetworkVariable<RoundState> currentState = new NetworkVariable<RoundState>(RoundState.Inactive, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString32Bytes> chosenWord = new NetworkVariable<FixedString32Bytes>(default, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> blancoPlayerId = new NetworkVariable<ulong>(default, writePerm: NetworkVariableWritePermission.Server);

    void Awake()
    {
        Instance = this;
    }

    public void StartGame()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        StartRound();
    }

    public void StartRound()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        SetRandomWord();
        PickBlancoPlayer();
        SetCardsValues();
        currentState.Value = RoundState.ShowingCards;

        StartCoroutine(ShowCardsCoroutine());
    }

    IEnumerator ShowCardsCoroutine()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();

            if (player != null)
            {
                UIGameplayManager.Instance.SetInfoTextClientRpc($"Player {client.ClientId}'s card has been revealed!");
                player.ShowCardClientRpc(true);
                yield return new WaitForSeconds(3f);
                player.ShowCardClientRpc(false);
                yield return new WaitForSeconds(1f);
            }
        }

        UIGameplayManager.Instance.HideInfoTextClientRpc();
        StartCoroutine(SayWordCoroutine());
    }

    IEnumerator SayWordCoroutine()
    {
        currentState.Value = RoundState.SayWord;
        var randomizedClients = NetworkManager.Singleton.ConnectedClientsList.OrderBy(c => UnityEngine.Random.value).ToList();

        foreach (var client in randomizedClients)
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();

            if (player != null)
            {
                UIGameplayManager.Instance.SetInfoTextClientRpc($"Player {client.ClientId}'s is speaking!");
                UIGameplayManager.Instance.StartGameTimer(5f);
                yield return new WaitForSeconds(5f);
                UIGameplayManager.Instance.HideInfoTextClientRpc();
                yield return new WaitForSeconds(1f);
            }
        }

        StartCoroutine(TalkingCoroutine());
    }

    IEnumerator TalkingCoroutine()
    {
        currentState.Value = RoundState.Talking;
        UIGameplayManager.Instance.StartGameTimer(10f);
        yield return new WaitForSeconds(10f);

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player != null)
            {
                player.ShowCardClientRpc(false);
                player.PointClientRpc(false);
            }
        }

        StartCoroutine(VotingCoroutine());
    }

    IEnumerator VotingCoroutine()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player != null)
            {
                player.AimClientRpc(true);
            }
        }
        currentState.Value = RoundState.Voting;
        yield return new WaitForSeconds(30f);
        UIGameplayManager.Instance.StartGameTimer(30f);
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
