using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Blanco.Networking;

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance;

    [Header("References")]
    [SerializeField] private WordListSO wordList;
    [SerializeField] private VivoxManager vivoxManager;

    [Header("Durations")]
    [SerializeField] private float cardRevealDuration = 3f;
    [SerializeField] private float betweenRevealsDelay = 1f;
    [SerializeField] private float sayWordDurationPerPlayer = 5f;
    [SerializeField] private float betweenSpeakersDelay = 1f;
    [SerializeField] private float talkingDuration = 10f;
    [SerializeField] private float votingDuration = 30f;
    [SerializeField] private float resultDelay = 5f;
    [SerializeField] private float tieDelay = 4f;

    public enum RoundState : byte { Inactive, ShowingCards, SayWord, Talking, Voting, Result }
    public enum WinConditionType { Rounds, RemainingPlayers }

    [Header("Win Condition")]
    [SerializeField] private WinConditionType winCondition = WinConditionType.Rounds;
    [SerializeField, Min(1)] private int roundsToWin = 5;
    [SerializeField, Min(1)] private int remainingPlayersToWin = 2;

    [Header("Victory Feedback")]
    [SerializeField] private string victoryMessage = "El Blanco ha ganado la partida!";
    [SerializeField] private Animator victoryAnimator;
    [SerializeField] private string victoryTrigger = "Victory";
    [SerializeField] private ParticleSystem victoryVfx;
    [SerializeField] private AudioSource victoryAudio;

    public NetworkVariable<RoundState> currentState = new NetworkVariable<RoundState>(RoundState.Inactive, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString32Bytes> chosenWord = new NetworkVariable<FixedString32Bytes>(default, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> blancoPlayerId = new NetworkVariable<ulong>(default, writePerm: NetworkVariableWritePermission.Server);

    private readonly Dictionary<ulong, ulong> playerVotes = new();
    public WinConditionType CurrentWinConditionType => winCondition;
    public int CurrentWinConditionThreshold => winCondition == WinConditionType.Rounds ? roundsToWin : remainingPlayersToWin;


    private readonly HashSet<ulong> eliminatedPlayers = new();

    private Coroutine roundFlowCoroutine;
    private int roundsCompleted;
    private bool isGameOver;
    private bool awaitingRestart;
    private bool victoryFeedbackValidated;
    private bool victoryAnimatorWarned;
    private bool victoryTriggerWarned;
    private bool victoryVfxWarned;
    private bool victoryAudioWarned;

    public bool IsGameOver => isGameOver;
    public bool IsAwaitingRestart => awaitingRestart;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (NetworkManager.Singleton.IsHost)
        {
            BroadcastCurrentWinCondition();
        }
    }

    void Awake()
    {
        Instance = this;
    }

    public void StartGame()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        RestartMatch();
    }

    public void RestartMatch()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        if (roundFlowCoroutine != null)
        {
            StopCoroutine(roundFlowCoroutine);
            roundFlowCoroutine = null;
        }

        ResetMatchState();
        BeginRound();
    }

    void ResetMatchState()
    {
        roundsCompleted = 0;
        eliminatedPlayers.Clear();
        isGameOver = false;
        awaitingRestart = false;
        playerVotes.Clear();
        chosenWord.Value = default;
        blancoPlayerId.Value = default;

        if (NetworkManager.Singleton != null)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerController>() : null;
                player?.ResetGhostState();
            }
        }

        UIGameplayManager.Instance?.SetWinConditionDisplay(winCondition, CurrentWinConditionThreshold);
        BroadcastCurrentWinCondition();
    }

    void BeginRound()
    {
        if (isGameOver || awaitingRestart)
            return;

        ResetRoundState();

        if (!PrepareRoundData())
        {
            Debug.LogWarning("[RoundManager] Unable to prepare round data. Aborting round start.");
            return;
        }

        currentState.Value = RoundState.ShowingCards;
        roundFlowCoroutine = StartCoroutine(ShowCardsCoroutine());
    }

    public void StartRound()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        BeginRound();
    }

    bool PrepareRoundData()
    {
        if (wordList == null || wordList.Words == null || wordList.Words.Length == 0)
        {
            Debug.LogWarning("[RoundManager] Word list is empty. Provide entries before starting a round.");
            return false;
        }

        if (GetActivePlayersCount() == 0)
        {
            Debug.LogWarning("[RoundManager] No active players available to start the round.");
            return false;
        }

        SetRandomWord();

        if (string.IsNullOrWhiteSpace(chosenWord.Value.ToString()))
        {
            Debug.LogWarning("[RoundManager] Chosen word is empty. Round cannot begin.");
            return false;
        }

        if (!TryPickBlancoPlayer())
        {
            Debug.LogWarning("[RoundManager] Failed to pick a Blanco player. Round cannot begin.");
            return false;
        }

        SetCardsValues();
        return true;
    }

    void ResetRoundState()
    {
        playerVotes.Clear();
        currentState.Value = RoundState.Inactive;
        ResetPlayerVotingState();
        ServerResetVoteOutcomes();
        ResetVotingStateClientRpc();

        if (UIGameplayManager.Instance != null)
        {
            UIGameplayManager.Instance.StopGameTimer();
            UIGameplayManager.Instance.HideInfoTextClientRpc();
            UIGameplayManager.Instance.ClearVoteSelectionClientRpc();
        }
    }

    void ResetPlayerVotingState()
    {
        if (NetworkManager.Singleton == null)
            return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerController>() : null;
            if (player != null)
            {
                player.ShowCardClientRpc(false);
                player.PointClientRpc(false);
                player.AimClientRpc(false);
            }
        }
    }

    void ServerResetVoteOutcomes()
    {
        if (NetworkManager.Singleton == null)
            return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var sync = client.PlayerObject != null
                ? client.PlayerObject.GetComponent<PlayerActionsSync>() ?? client.PlayerObject.GetComponentInChildren<PlayerActionsSync>()
                : null;

            if (sync != null)
            {
                sync.voteOutcome.Value = PlayerActionsSync.VoteOutcome.None;
            }
        }
    }

    IEnumerator ShowCardsCoroutine()
    {
        foreach (var client in GetActiveClients())
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player == null)
                continue;

            UIGameplayManager.Instance?.SetInfoTextClientRpc($"Revealing {GetDisplayName(client.ClientId)}'s card");
            player.ShowCardClientRpc(true);
            yield return new WaitForSeconds(cardRevealDuration);
            player.ShowCardClientRpc(false);
            yield return new WaitForSeconds(betweenRevealsDelay);
        }

        UIGameplayManager.Instance?.HideInfoTextClientRpc();
        roundFlowCoroutine = StartCoroutine(SayWordCoroutine());
    }

    IEnumerator SayWordCoroutine()
    {
        currentState.Value = RoundState.SayWord;
        var randomizedClients = GetActiveClients().OrderBy(_ => UnityEngine.Random.value).ToList();

        foreach (var client in randomizedClients)
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player == null)
                continue;

            UIGameplayManager.Instance?.SetInfoTextClientRpc($"{GetDisplayName(client.ClientId)} is speaking");
            vivoxManager?.MuteAllExcept(player.OwnerClientId);
            UIGameplayManager.Instance?.StartGameTimer(sayWordDurationPerPlayer);
            yield return new WaitForSeconds(sayWordDurationPerPlayer);
            UIGameplayManager.Instance?.HideInfoTextClientRpc();
            yield return new WaitForSeconds(betweenSpeakersDelay);
        }

        roundFlowCoroutine = StartCoroutine(TalkingCoroutine());
    }

    IEnumerator TalkingCoroutine()
    {
        vivoxManager?.UnmuteAll();
        currentState.Value = RoundState.Talking;
        UIGameplayManager.Instance?.SetInfoTextClientRpc("Free discussion");
        UIGameplayManager.Instance?.StartGameTimer(talkingDuration);

        yield return new WaitForSeconds(talkingDuration);

        UIGameplayManager.Instance?.HideInfoTextClientRpc();

        foreach (var client in GetActiveClients())
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player == null)
                continue;

            player.ShowCardClientRpc(false);
            player.PointClientRpc(false);
        }

        roundFlowCoroutine = StartCoroutine(VotingCoroutine());
    }

    IEnumerator VotingCoroutine()
    {
        currentState.Value = RoundState.Voting;
        playerVotes.Clear();

        foreach (var client in GetActiveClients())
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            player?.AimClientRpc(true);
        }

        UIGameplayManager.Instance?.ClearVoteSelectionClientRpc();
        UIGameplayManager.Instance?.SetInfoTextClientRpc("Aim and select your suspect");
        UIGameplayManager.Instance?.StartGameTimer(votingDuration);

        yield return new WaitForSeconds(votingDuration);

        UIGameplayManager.Instance?.StopGameTimer();
        ResolveVotingPhase();
    }

    void ResolveVotingPhase()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        currentState.Value = RoundState.Result;
        UIGameplayManager.Instance?.HideInfoTextClientRpc();
        HideVotingWeapons();

        if (playerVotes.Count == 0)
        {
            UIGameplayManager.Instance?.SetInfoTextClientRpc("No one voted. The round will restart.");
            roundsCompleted++;
            playerVotes.Clear();

            if (CheckVictoryConditions(out string reason))
            {
                TriggerVictory(reason);
                return;
            }

            roundFlowCoroutine = StartCoroutine(BeginNextRoundAfterDelay(tieDelay));
            return;
        }

        var validVotes = playerVotes
            .Where(kv => NetworkManager.Singleton.ConnectedClients.ContainsKey(kv.Value) && !eliminatedPlayers.Contains(kv.Value))
            .GroupBy(kv => kv.Value)
            .Select(group => new { Target = group.Key, Count = group.Count() })
            .ToList();

        if (validVotes.Count == 0)
        {
            UIGameplayManager.Instance?.SetInfoTextClientRpc("Votes were invalid. The round will restart.");
            roundsCompleted++;
            playerVotes.Clear();

            if (CheckVictoryConditions(out string reason))
            {
                TriggerVictory(reason);
                return;
            }

            roundFlowCoroutine = StartCoroutine(BeginNextRoundAfterDelay(tieDelay));
            return;
        }

        int maxVotes = validVotes.Max(v => v.Count);
        var topTargets = validVotes.Where(v => v.Count == maxVotes).Select(v => v.Target).ToList();

        if (topTargets.Count != 1)
        {
            ApplyVoteOutcomes(0, true);
            UIGameplayManager.Instance?.SetInfoTextClientRpc("Voting ended in a tie. No one is eliminated.");
            roundsCompleted++;
            playerVotes.Clear();

            if (CheckVictoryConditions(out string reason))
            {
                TriggerVictory(reason);
                return;
            }

            roundFlowCoroutine = StartCoroutine(BeginNextRoundAfterDelay(tieDelay));
            return;
        }

        ulong eliminatedId = topTargets[0];
        ApplyVoteOutcomes(eliminatedId, false);
        RegisterElimination(eliminatedId);

        string eliminatedName = GetDisplayName(eliminatedId);
        UIGameplayManager.Instance?.SetInfoTextClientRpc($"{eliminatedName} was eliminated with {maxVotes} votes.");

        roundsCompleted++;
        playerVotes.Clear();

        if (CheckVictoryConditions(out string victoryReason))
        {
            TriggerVictory(victoryReason);
            return;
        }

        roundFlowCoroutine = StartCoroutine(BeginNextRoundAfterDelay(resultDelay));
    }

    void RegisterElimination(ulong eliminatedId)
    {
        if (!eliminatedPlayers.Add(eliminatedId))
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(eliminatedId, out var client))
        {
            var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerController>() : null;
            if (player != null)
            {
                player.AimClientRpc(false);
                player.PointClientRpc(false);
                player.ShowCardClientRpc(false);
                player.SetGhostStateServer(true);
            }
        }
    }

    void HideVotingWeapons()
    {
        if (NetworkManager.Singleton == null)
            return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerController>() : null;
            if (player != null)
            {
                player.AimClientRpc(false);
                player.PointClientRpc(false);
            }
        }
    }

    void ApplyVoteOutcomes(ulong eliminatedId, bool isTie)
    {
        if (NetworkManager.Singleton == null)
            return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var sync = client.PlayerObject != null
                ? client.PlayerObject.GetComponent<PlayerActionsSync>() ?? client.PlayerObject.GetComponentInChildren<PlayerActionsSync>()
                : null;

            if (sync == null)
                continue;

            if (isTie)
            {
                sync.voteOutcome.Value = PlayerActionsSync.VoteOutcome.Tie;
                continue;
            }

            if (!playerVotes.TryGetValue(client.ClientId, out ulong votedTarget))
            {
                sync.voteOutcome.Value = PlayerActionsSync.VoteOutcome.NoVote;
            }
            else if (votedTarget == eliminatedId)
            {
                sync.voteOutcome.Value = PlayerActionsSync.VoteOutcome.Success;
            }
            else
            {
                sync.voteOutcome.Value = PlayerActionsSync.VoteOutcome.Fail;
            }
        }
    }

    IEnumerator BeginNextRoundAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (isGameOver || awaitingRestart)
            yield break;

        BeginRound();
    }

    bool CheckVictoryConditions(out string reason)
    {
        reason = null;

        switch (winCondition)
        {
            case WinConditionType.Rounds:
                if (roundsCompleted >= Mathf.Max(1, roundsToWin))
                {
                    reason = $"Se completaron {roundsCompleted} rondas.";
                    return true;
                }
                break;
            case WinConditionType.RemainingPlayers:
                int activePlayers = GetActivePlayersCount();
                if (activePlayers <= Mathf.Max(1, remainingPlayersToWin))
                {
                    reason = activePlayers == 1
                        ? "Solo queda un jugador activo en la mesa."
                        : $"Solo quedan {activePlayers} jugadores activos.";
                    return true;
                }
                break;
        }

        return false;
    }

    void TriggerVictory(string reason, bool playersWin)
    {
        if (isGameOver)
            return;

        isGameOver = true;
        awaitingRestart = true;
        playerVotes.Clear();
        currentState.Value = RoundState.Result;

        if (roundFlowCoroutine != null)
        {
            StopCoroutine(roundFlowCoroutine);
            roundFlowCoroutine = null;
        }

        StopAllCoroutines();

        vivoxManager?.UnmuteAll();
        UIGameplayManager.Instance?.StopGameTimer();

        string header = playersWin ? "Players win!" : "The Blanco wins!";
        if (UIGameplayManager.Instance != null)
        {
            string composedMessage = header;
            if (!string.IsNullOrEmpty(reason))
            {
                composedMessage += $"\n{reason}";
            }

            composedMessage += "\n\nHost: press SPACE to start a new match.";
            UIGameplayManager.Instance.SetInfoTextClientRpc(composedMessage);
        }

        MatchManager.Instance?.ShowWaitingUI();
        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.currentState.Value = MatchManager.MatchState.Result;
        }

        ValidateVictoryFeedback();
        PlayVictoryFeedback(playersWin);

        ResetVotingStateClientRpc();
    }

    void ValidateVictoryFeedback()
    {
        if (victoryFeedbackValidated)
            return;

        victoryFeedbackValidated = true;

        if (victoryAnimator == null && !victoryAnimatorWarned)
        {
            Debug.LogWarning($"[{nameof(RoundManager)}] Falta asignar el Animator de victoria.", this);
            victoryAnimatorWarned = true;
        }

        if (victoryAnimator != null && string.IsNullOrEmpty(victoryTrigger) && !victoryTriggerWarned)
        {
            Debug.LogWarning($"[{nameof(RoundManager)}] El trigger de victoria esta vacio.", this);
            victoryTriggerWarned = true;
        }

        if (victoryVfx == null && !victoryVfxWarned)
        {
            Debug.LogWarning($"[{nameof(RoundManager)}] Falta asignar el VFX de victoria.", this);
            victoryVfxWarned = true;
        }

        if (victoryAudio == null && !victoryAudioWarned)
        {
            Debug.LogWarning($"[{nameof(RoundManager)}] Falta asignar el AudioSource de victoria.", this);
            victoryAudioWarned = true;
        }
    }

    void PlayVictoryFeedback()
    {
        if (victoryAnimator != null && !string.IsNullOrEmpty(victoryTrigger))
        {
            victoryAnimator.SetTrigger(victoryTrigger);
        }

        if (victoryVfx != null)
        {
            victoryVfx.Play();
        }

        if (victoryAudio != null)
        {
            victoryAudio.Play();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitVoteServerRpc(ulong targetClientId, ServerRpcParams rpcParams = default)
    {
        if (currentState.Value != RoundState.Voting)
            return;

        ulong voterId = rpcParams.Receive.SenderClientId;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.ContainsKey(voterId))
            return;

        if (eliminatedPlayers.Contains(voterId))
            return;

        if (targetClientId == PlayerActionsSync.NoTarget || eliminatedPlayers.Contains(targetClientId) || !NetworkManager.Singleton.ConnectedClients.ContainsKey(targetClientId))
        {
            playerVotes.Remove(voterId);
        }
        else
        {
            playerVotes[voterId] = targetClientId;
        }
    }

    bool TryPickBlancoPlayer()
    {
        var availableClients = GetActiveClients().ToList();
        if (availableClients.Count == 0)
        {
            Debug.LogWarning("[RoundManager] No active players available to pick a Blanco.");
            return false;
        }

        var selectedClient = availableClients[UnityEngine.Random.Range(0, availableClients.Count)];
        blancoPlayerId.Value = selectedClient.ClientId;
        Debug.Log($"Blanco player chosen: {blancoPlayerId.Value}");
        return true;
    }

    void SetRandomWord()
    {
        if (wordList == null || wordList.Words == null || wordList.Words.Length == 0)
        {
            Debug.LogError("Word list is empty or not assigned.");
            return;
        }

        int randomIndex = UnityEngine.Random.Range(0, wordList.Words.Length);
        chosenWord.Value = wordList.Words[randomIndex];
    }

    void SetCardsValues()
    {
        foreach (var client in GetActiveClients())
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player != null)
            {
                player.SetCardValuesClientRpc(chosenWord.Value, blancoPlayerId.Value);
            }
        }
    }

    public void ConfigureWinCondition(WinConditionType type, int threshold)
    {
        if (NetworkManager.Singleton.IsHost)
        {
            ApplyWinCondition(type, threshold);
            BroadcastWinConditionClientRpc(type, Mathf.Max(1, threshold));
        }
        else
        {
            ConfigureWinConditionServerRpc(type, threshold);
        }
    }

    void ApplyWinCondition(WinConditionType type, int threshold)
    {
        winCondition = type;

        if (type == WinConditionType.Rounds)
        {
            roundsToWin = Mathf.Max(1, threshold);
        }
        else
        {
            remainingPlayersToWin = Mathf.Max(1, threshold);
        }
    }

    public void BroadcastCurrentWinCondition()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        BroadcastWinConditionClientRpc(winCondition, CurrentWinConditionThreshold);
    }

    [ServerRpc(RequireOwnership = false)]
    void ConfigureWinConditionServerRpc(WinConditionType type, int threshold)
    {
        ApplyWinCondition(type, threshold);
        BroadcastWinConditionClientRpc(type, Mathf.Max(1, threshold));
    }

    [ClientRpc]
    void BroadcastWinConditionClientRpc(WinConditionType type, int threshold)
    {
        threshold = Mathf.Max(1, threshold);

        if (!NetworkManager.Singleton.IsHost)
        {
            ApplyWinCondition(type, threshold);
        }

        UIGameplayManager.Instance?.SetWinConditionDisplay(type, threshold);
    }

    int GetActivePlayersCount()
    {
        return GetActiveClients().Count();
    }

    IEnumerable<NetworkClient> GetActiveClients()
    {
        if (NetworkManager.Singleton == null)
            return Enumerable.Empty<NetworkClient>();

        return NetworkManager.Singleton.ConnectedClientsList
            .Where(client => client.PlayerObject != null && !eliminatedPlayers.Contains(client.ClientId));
    }

    string GetDisplayName(ulong clientId)
    {
        if (LobbyManager.Instance != null)
        {
            try
            {
                var playerInfo = LobbyManager.Instance.GetPlayerInfo(clientId);
                if (!playerInfo.playerName.IsEmpty)
                {
                    return playerInfo.playerName.ToString();
                }
            }
            catch
            {
                // Ignorar lookup fallido
            }
        }

        return $"Jugador {clientId}";
    }

    [ClientRpc]
    void ResetVotingStateClientRpc()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return;

        var playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObject == null)
            return;

        var sync = playerObject.GetComponent<PlayerActionsSync>() ?? playerObject.GetComponentInChildren<PlayerActionsSync>();
        sync?.ResetVotingState();

        UIGameplayManager.Instance?.SetLocalVoteSelection("Sin objetivo seleccionado");
    }
}





