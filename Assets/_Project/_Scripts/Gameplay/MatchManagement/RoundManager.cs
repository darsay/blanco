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

    public enum ScorePhase { RoundCompleted, GameResult }
    public enum ScoreCategory { Survival, CorrectVote, GameWin }
    public enum RoundConclusionType { Elimination, Tie, InvalidVotes, NoVotes }

    [Serializable]
    public class ScoreEvent
    {
        public ulong PlayerId;
        public int Points;
        public bool IsBlanco;
        public ScoreCategory Category;
    }

    [Serializable]
    public struct RoundSummaryData
    {
        public RoundConclusionType ConclusionType;
        public bool HasEliminatedPlayer;
        public ulong EliminatedPlayerId;
    }

    [Serializable]
    public struct GameResultSummaryData
    {
        public bool PlayersWin;
        public WinConditionType WinCondition;
        public int RoundsCompletedSnapshot;
        public int ActivePlayersSnapshot;
        public bool AnyBlancoAliveSnapshot;
    }

    [Serializable]
    public class RoundScoreReport
    {
        public int RoundNumber;
        public ScorePhase Phase;
        public RoundSummaryData RoundSummary;
        public GameResultSummaryData GameSummary;
        public List<ScoreEvent> Events = new();
    }

    struct VoteRevealEntry
    {
        public ulong TargetId;
        public int VoteCount;
        public List<ulong> Voters;
    }

    struct VoteTally
    {
        public ulong Target;
        public int Count;
    }

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
    [SerializeField] private float eliminationDuration = 4f;

    [Header("Vote Reveal Sequence")]
    [SerializeField] private float voteRevealStepDuration = 2.5f;
    [SerializeField] private float voteRevealBetweenStepsDelay = 0.75f;
    [SerializeField] private float voteRevealCameraLockDuration = 1.5f;
    [SerializeField] private float voteRevealFocusBlendDuration = 0.35f;
    [SerializeField] private float voteRevealFocusHeightOffset = 1.6f;
    [SerializeField] private bool lockInputDuringVoteReveal = true;

    public enum RoundState : byte { Inactive, ShowingCards, SayWord, Talking, Voting, Result }
    public enum WinConditionType { Rounds, RemainingPlayers }

    [Header("Win Condition")]
    [SerializeField] private WinConditionType winCondition = WinConditionType.Rounds;
    [SerializeField, Min(1)] private int roundsToWin = 5;
    [SerializeField, Min(1)] private int remainingPlayersToWin = 2;

    [Header("Blanco Settings")]
    [SerializeField, Min(1)] private int blancosPerMatch = 1;

    [Header("Victory Feedback")]
    [SerializeField] private string victoryMessage = "El Blanco ha ganado la partida!";
    [SerializeField] private Animator victoryAnimator;
    [SerializeField] private string victoryTrigger = "Victory";
    [SerializeField] private ParticleSystem victoryVfx;
    [SerializeField] private AudioSource victoryAudio;

    [Header("Scoring")]
    [SerializeField] private int pointsPerRoundSurvivedBlanco = 5;
    [SerializeField] private int pointsPerRoundSurvivedPlayer = 3;
    [SerializeField] private int pointsPerGameWinBlanco = 20;
    [SerializeField] private int pointsPerGameWinPlayer = 20;
    [SerializeField] private int pointsPerCorrectVote = 4;

    public NetworkVariable<RoundState> currentState = new NetworkVariable<RoundState>(RoundState.Inactive, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString32Bytes> chosenWord = new NetworkVariable<FixedString32Bytes>(default, writePerm: NetworkVariableWritePermission.Server);
    public NetworkList<ulong> blancoPlayerIds = new();
    private readonly HashSet<ulong> blancoPlayersCache = new();
    private bool hasShownCardsThisMatch;

    private readonly Dictionary<ulong, ulong> playerVotes = new();
    public WinConditionType CurrentWinConditionType => winCondition;
    public int CurrentWinConditionThreshold => winCondition == WinConditionType.Rounds ? roundsToWin : remainingPlayersToWin;
    public int BlancosPerMatch => Mathf.Max(1, blancosPerMatch);


    private readonly HashSet<ulong> eliminatedPlayers = new();
    private int currentStartingOrderSlot = -1;
    private static readonly int[] SeatTurnOrder = { 0, 4, 3, 6, 1, 5, 2, 7 };
    private readonly NetworkList<ulong> eliminatedPlayersSync = new();
    private readonly HashSet<ulong> eliminatedPlayersCache = new();
    private bool blancosAssignedThisMatch;

    private Coroutine roundFlowCoroutine;
    private int roundsCompleted;
    private bool isGameOver;
    private bool awaitingRestart;
    private readonly List<RoundScoreReport> currentGameScoreReports = new();
    private bool victoryFeedbackValidated;
    private bool victoryAnimatorWarned;
    private bool victoryTriggerWarned;
    private bool victoryVfxWarned;
    private bool victoryAudioWarned;

    public bool IsGameOver => isGameOver;
    public bool IsAwaitingRestart => awaitingRestart;
    public IReadOnlyList<ulong> EliminatedPlayers
    {
        get
        {
            var list = new List<ulong>(eliminatedPlayersSync.Count);
            foreach (var id in eliminatedPlayersSync)
                list.Add(id);
            return list;
        }
    }
    public IReadOnlyList<ulong> BlancoPlayers
    {
        get
        {
            var list = new List<ulong>(blancoPlayerIds.Count);
            foreach (var id in blancoPlayerIds)
                list.Add(id);
            return list;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Mantener sincronizada la lista de eliminados y blancos en todos los clientes.
        eliminatedPlayersSync.OnListChanged += HandleEliminatedPlayersListChanged;
        blancoPlayerIds.OnListChanged += HandleBlancoPlayersListChanged;

        if (NetworkManager.Singleton.IsHost)
        {
            BroadcastCurrentWinCondition();
        }
        else
        {
            RebuildEliminatedPlayersCache();
            RebuildBlancoPlayersCache();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        eliminatedPlayersSync.OnListChanged -= HandleEliminatedPlayersListChanged;
        blancoPlayerIds.OnListChanged -= HandleBlancoPlayersListChanged;

        eliminatedPlayersCache.Clear();
        blancoPlayersCache.Clear();
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
        MatchManager.Instance?.OnRoundManagerGameStarted();
        BeginRound();
    }

    void ResetMatchState()
    {
        roundsCompleted = 0;
        eliminatedPlayers.Clear();
        eliminatedPlayersSync.Clear();
        eliminatedPlayersCache.Clear();
        blancoPlayerIds.Clear();
        blancoPlayersCache.Clear();
        blancosAssignedThisMatch = false;
        hasShownCardsThisMatch = false;
        isGameOver = false;
        awaitingRestart = false;
        playerVotes.Clear();
        currentGameScoreReports.Clear();
        chosenWord.Value = default;
        currentStartingOrderSlot = -1;

        SpawnAllGhosts();

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

        if (hasShownCardsThisMatch)
        {
            // En rondas posteriores omitimos la animacion de cartas.
            roundFlowCoroutine = StartCoroutine(SayWordCoroutine());
            return;
        }

        hasShownCardsThisMatch = true;
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

        if (!TryAssignBlancoPlayers())
        {
            Debug.LogWarning("[RoundManager] Failed to assign a Blanco player. Round cannot begin.");
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
        var orderedClients = GetActiveClientsInSeatingOrder();
        foreach (var client in orderedClients)
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
        var orderedClients = GetActiveClientsInSeatingOrder();

        foreach (var client in orderedClients)
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
        vivoxManager?.UnmuteAllAlive();
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

        UIGameplayManager.Instance?.SetInfoTextClientRpc("Aim and select your suspect");
        UIGameplayManager.Instance?.StartGameTimer(votingDuration);

        yield return new WaitForSeconds(votingDuration);

        UIGameplayManager.Instance?.StopGameTimer();
        StartCoroutine(ResolveVotingPhase());
    }

    IEnumerator ResolveVotingPhase()
    {
        if (!NetworkManager.Singleton.IsHost)
            yield break;

        currentState.Value = RoundState.Result;
        roundsCompleted++;
        UIGameplayManager.Instance?.HideInfoTextClientRpc();

        bool weaponsHidden = false;
        void HideWeaponsOnce()
        {
            if (weaponsHidden)
                return;
            HideVotingWeapons();
            weaponsHidden = true;
        }

        if (playerVotes.Count == 0)
        {
            HideWeaponsOnce();
            UIGameplayManager.Instance?.SetInfoTextClientRpc("No one voted. The round will restart.");
            ReportRoundOutcome(roundsCompleted, RoundConclusionType.NoVotes, null);
            playerVotes.Clear();

            if (CheckVictoryConditions(out string reason, out bool playersWinResult))
            {
                yield return new WaitForSeconds(resultDelay);
                TriggerVictory(reason, playersWinResult);
                yield break;
            }

            roundFlowCoroutine = StartCoroutine(BeginNextRoundAfterDelay(tieDelay));
            yield break;
        }

        var validVotes = playerVotes
            .Where(kv => NetworkManager.Singleton.ConnectedClients.ContainsKey(kv.Value) && !eliminatedPlayers.Contains(kv.Value))
            .GroupBy(kv => kv.Value)
            .Select(group => new VoteTally { Target = group.Key, Count = group.Count() })
            .ToList();

        if (validVotes.Count == 0)
        {
            HideWeaponsOnce();
            UIGameplayManager.Instance?.SetInfoTextClientRpc("Votes were invalid. The round will restart.");
            ReportRoundOutcome(roundsCompleted, RoundConclusionType.InvalidVotes, null);
            playerVotes.Clear();

            if (CheckVictoryConditions(out string reason, out bool playersWinResult))
            {
                yield return new WaitForSeconds(resultDelay);
                TriggerVictory(reason, playersWinResult);
                yield break;
            }

            roundFlowCoroutine = StartCoroutine(BeginNextRoundAfterDelay(tieDelay));
            yield break;
        }

        var voteLookup = BuildVoteLookup();
        var revealEntries = BuildVoteRevealEntries(validVotes, voteLookup);
        int maxVotes = validVotes.Max(v => v.Count);
        var topTargets = validVotes.Where(v => v.Count == maxVotes).Select(v => v.Target).ToList();
        ulong? eliminatedId = topTargets.Count == 1 ? topTargets[0] : (ulong?)null;

        if (revealEntries.Count > 0)
        {
            yield return PlayVoteRevealSequence(revealEntries, eliminatedId, topTargets);
        }

        if (topTargets.Count != 1)
        {
            HideWeaponsOnce();
            ApplyVoteOutcomes(0, true);
            UIGameplayManager.Instance?.SetInfoTextClientRpc("Voting ended in a tie. No one is eliminated.");
            ReportRoundOutcome(roundsCompleted, RoundConclusionType.Tie, null);
            playerVotes.Clear();

            if (CheckVictoryConditions(out string reason, out bool playersWinResult))
            {
                yield return new WaitForSeconds(resultDelay);
                TriggerVictory(reason, playersWinResult);
                yield break;
            }

            roundFlowCoroutine = StartCoroutine(BeginNextRoundAfterDelay(tieDelay));
            yield break;
        }

        ulong eliminated = eliminatedId.Value;
        ApplyVoteOutcomes(eliminated, false);

        string eliminatedName = GetDisplayName(eliminated);
        UIGameplayManager.Instance?.SetInfoTextClientRpc($"{eliminatedName} was eliminated with {maxVotes} votes.");

        // TODO: Integrate elimination-specific animation once assets are ready.

        ReportRoundOutcome(roundsCompleted, RoundConclusionType.Elimination, eliminated);
        RegisterElimination(eliminated);

        yield return new WaitForSeconds(eliminationDuration);

        if (IsPlayerBlanco(eliminated) && AreAllBlancosEliminated())
        {
            UIGameplayManager.Instance?.SetInfoTextClientRpc($"{eliminatedName} era Blanko. Todos los Blancos han sido eliminados!");
            yield return new WaitForSeconds(resultDelay);
            TriggerVictory("Todos los Blancos han sido eliminados!", true);
            HideWeaponsOnce();
            yield break;
        }
        else if (IsPlayerBlanco(eliminated) && !AreAllBlancosEliminated())
        {
            UIGameplayManager.Instance?.SetInfoTextClientRpc($"{eliminatedName} era Blanko. Pero quedan mas en la partida!");
        }
        else if (!IsPlayerBlanco(eliminated))
        {
            UIGameplayManager.Instance?.SetInfoTextClientRpc($"{eliminatedName} no era Blanko.");
        }

        yield return new WaitForSeconds(eliminationDuration);

        HideWeaponsOnce();
        playerVotes.Clear();

        if (CheckVictoryConditions(out string victoryReason, out bool playersWin))
        {
            TriggerVictory(victoryReason, playersWin);
            yield break;
        }

        roundFlowCoroutine = StartCoroutine(BeginNextRoundAfterDelay(resultDelay));
    }

    Dictionary<ulong, List<ulong>> BuildVoteLookup()
    {
        var lookup = new Dictionary<ulong, List<ulong>>();
        foreach (var kv in playerVotes)
        {
            if (!lookup.TryGetValue(kv.Value, out var voters))
            {
                voters = new List<ulong>();
                lookup[kv.Value] = voters;
            }

            voters.Add(kv.Key);
        }

        return lookup;
    }

    List<VoteRevealEntry> BuildVoteRevealEntries(IEnumerable<VoteTally> tallies, Dictionary<ulong, List<ulong>> voteLookup)
    {
        var orderedTallies = tallies
            .OrderBy(t => t.Count)
            .ThenBy(t => t.Target)
            .ToList();

        var entries = new List<VoteRevealEntry>(orderedTallies.Count);
        foreach (var tally in orderedTallies)
        {
            if (tally.Count <= 0)
                continue;

            if (!voteLookup.TryGetValue(tally.Target, out var voters))
                continue;

            entries.Add(new VoteRevealEntry
            {
                TargetId = tally.Target,
                VoteCount = tally.Count,
                Voters = new List<ulong>(voters)
            });
        }

        return entries;
    }

    IEnumerator PlayVoteRevealSequence(List<VoteRevealEntry> entries, ulong? eliminatedId, List<ulong> topTargets)
    {
        if (entries == null || entries.Count == 0)
            yield break;

        var topTargetSet = new HashSet<ulong>(topTargets);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            bool isFinalShot = eliminatedId.HasValue && entry.TargetId == eliminatedId.Value;
            bool isTieLeader = !eliminatedId.HasValue && topTargetSet.Contains(entry.TargetId);

            BroadcastVoteRevealStepClientRpc(entry.TargetId, entry.VoteCount, entry.Voters.ToArray(), isFinalShot, isTieLeader);
            ApplyVoteRevealFocus(entry.TargetId, entry.Voters, lockInputDuringVoteReveal);

            if (voteRevealStepDuration > 0f)
            {
                yield return new WaitForSeconds(voteRevealStepDuration);
            }

            if (!isFinalShot)
            {
                // TODO: Trigger failed shot animation for this reveal step voters.
            }
            else
            {
                // TODO: Trigger successful elimination shot animation for this reveal step voters.
            }

            if (i < entries.Count - 1 && voteRevealBetweenStepsDelay > 0f)
            {
                yield return new WaitForSeconds(voteRevealBetweenStepsDelay);
            }
        }
    }

    void ApplyVoteRevealFocus(ulong focusTargetId, IReadOnlyList<ulong> voters, bool lockCamera)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (!TryGetFocusPoint(focusTargetId, out var focusPoint))
            return;

        var voterSet = new HashSet<ulong>(voters);

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerController>() : null;
            if (player == null)
                continue;

            bool isVoter = voterSet.Contains(client.ClientId);
            bool isTarget = client.ClientId == focusTargetId;

            if (!isTarget)
            {
                player.ForceLookAtPointClientRpc(focusPoint, voteRevealFocusBlendDuration, voteRevealCameraLockDuration, lockCamera, false);
                player.PointClientRpc(isVoter);
                player.AimClientRpc(isVoter);
            }
            else
            {
                player.PointClientRpc(false);
                player.AimClientRpc(false);
            }
        }
    }

    bool TryGetFocusPoint(ulong clientId, out Vector3 focusPoint)
    {
        focusPoint = Vector3.zero;

        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return false;

        if (client.PlayerObject == null)
            return false;

        focusPoint = client.PlayerObject.transform.position + Vector3.up * voteRevealFocusHeightOffset;
        return true;
    }

    [ClientRpc]
    void BroadcastVoteRevealStepClientRpc(ulong focusTargetId, int voteCount, ulong[] voterIds, bool isFinalShot, bool isTieLeader)
    {
        UIGameplayManager.Instance?.ShowVoteRevealStep(focusTargetId, voteCount, voterIds, isFinalShot, isTieLeader);
    }

    void RegisterElimination(ulong eliminatedId)
    {
        if (!eliminatedPlayers.Add(eliminatedId))
            return;

        if (IsServer)
        {
            // Notificamos a los clientes que este jugador quedo fuera de la partida.
            eliminatedPlayersSync.Add(eliminatedId);
        }

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

        if (!IsServer)
            return;
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

    bool CheckVictoryConditions(out string reason, out bool playersWin)
    {
        reason = null;
        playersWin = false;

        switch (winCondition)
        {
            case WinConditionType.Rounds:
                if (roundsCompleted >= Mathf.Max(1, roundsToWin))
                {
                    bool anyBlancoAlive = BlancosAliveCount() > 0;
                    reason = anyBlancoAlive
                        ? $"Se completaron {roundsCompleted} rondas y aun queda al menos un Blanco vivo."
                        : $"Se completaron {roundsCompleted} rondas y no quedan Blancos con vida.";
                    playersWin = !anyBlancoAlive;
                    return true;
                }
                break;
            case WinConditionType.RemainingPlayers:
                int activePlayers = GetActivePlayersCount();
                if (activePlayers <= Mathf.Max(1, remainingPlayersToWin))
                {
                    bool anyBlancoAlive = BlancosAliveCount() > 0;
                    reason = activePlayers == 1
                        ? "Solo queda un jugador activo en la mesa."
                        : $"Solo quedan {activePlayers} jugadores activos.";
                    playersWin = !anyBlancoAlive;
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

        string header = playersWin ? "Los jugadores ganan!" : "El Blanco gana!";
        if (UIGameplayManager.Instance != null)
        {
            string composedMessage = header;
            if (!string.IsNullOrEmpty(reason))
            {
                composedMessage += $"\n{reason}";
            }
            UIGameplayManager.Instance.SetInfoTextClientRpc(composedMessage);
        }

        ReportGameWin(playersWin);
        
        SpawnAllGhosts();

        MatchManager.Instance?.OnRoundManagerGameEnded(playersWin, reason);
        currentGameScoreReports.Clear();

        ValidateVictoryFeedback();
        PlayVictoryFeedback();
    }

    void SpawnAllGhosts()
    {
        if (NetworkManager.Singleton != null)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerController>() : null;
                player?.ResetGhostState();
            }
        }
    }

    int BlancosAliveCount()
    {
        if (!blancosAssignedThisMatch)
            return 0;

        int BlancosAlive = 0;

        // Revisa la lista segun el rol (host o cliente) para determinar si queda algun Blanco activo.
        IEnumerable<ulong> source = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost
            ? BlancoPlayers
            : blancoPlayersCache;

        foreach (var blancoId in source)
        {
            if (!IsPlayerEliminated(blancoId))
            {
                if (NetworkManager.Singleton == null)
                    return 0;

                if (NetworkManager.Singleton.ConnectedClients.ContainsKey(blancoId))
                    BlancosAlive++;
            }
        }

        return BlancosAlive;
    }

    void HandleBlancoPlayersListChanged(NetworkListEvent<ulong> change)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            return;

        RebuildBlancoPlayersCache();
    }

    void HandleEliminatedPlayersListChanged(NetworkListEvent<ulong> change)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            return;

        RebuildEliminatedPlayersCache();
    }

    void RebuildEliminatedPlayersCache()
    {
        eliminatedPlayersCache.Clear();
        foreach (var id in eliminatedPlayersSync)
        {
            eliminatedPlayersCache.Add(id);
        }
    }

    void RebuildBlancoPlayersCache()
    {
        // En clientes no host replicamos la lista para facilitar las consultas locales.
        blancoPlayersCache.Clear();
        foreach (var id in blancoPlayerIds)
        {
            blancoPlayersCache.Add(id);
        }

        blancosAssignedThisMatch = blancoPlayersCache.Count > 0;
    }

    bool AreAllBlancosEliminated()
    {
        // Confirma si queda algun Blanco sin eliminar teniendo en cuenta el contexto de red.
        IEnumerable<ulong> source = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost
            ? BlancoPlayers
            : blancoPlayersCache;

        if (!source.Any())
            return false;

        foreach (var blancoId in source)
        {
            if (!IsPlayerEliminated(blancoId))
                return false;
        }

        return true;
    }

    public bool IsPlayerEliminated(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            return eliminatedPlayers.Contains(clientId);
        }

        return eliminatedPlayersCache.Contains(clientId);
    }

    public bool IsPlayerBlanco(ulong clientId)
    {
        // Permite a cualquier lado de la red saber si un jugador es Blanco usando la fuente de datos adecuada.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            return blancoPlayerIds.Contains(clientId);
        }

        return blancoPlayersCache.Contains(clientId);
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

    void ReportGameWin(bool playersWin)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        var report = new RoundScoreReport
        {
            RoundNumber = Mathf.Max(1, roundsCompleted),
            Phase = ScorePhase.GameResult,
            GameSummary = new GameResultSummaryData
            {
                PlayersWin = playersWin,
                WinCondition = winCondition,
                RoundsCompletedSnapshot = Mathf.Max(1, roundsCompleted),
                ActivePlayersSnapshot = GetActivePlayersCount(),
                AnyBlancoAliveSnapshot = BlancosAliveCount() > 0
            }
        };

        int winPoints = playersWin ? pointsPerGameWinPlayer : pointsPerGameWinBlanco;
        if (winPoints != 0)
        {
            foreach (var winnerId in EnumerateAlivePlayers())
            {
                bool isBlanco = IsPlayerBlanco(winnerId);
                if (playersWin && isBlanco)
                    continue;
                if (!playersWin && !isBlanco)
                    continue;

                report.Events.Add(new ScoreEvent
                {
                    PlayerId = winnerId,
                    Points = winPoints,
                    IsBlanco = isBlanco,
                    Category = ScoreCategory.GameWin
                });
            }
        }

        currentGameScoreReports.Add(report);
        MatchManager.Instance?.ProcessRoundScore(report);
    }

    void ReportRoundOutcome(int roundNumber, RoundConclusionType conclusionType, ulong? eliminatedPlayerId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        var report = new RoundScoreReport
        {
            RoundNumber = Mathf.Max(1, roundNumber),
            Phase = ScorePhase.RoundCompleted,
            RoundSummary = new RoundSummaryData
            {
                ConclusionType = conclusionType,
                HasEliminatedPlayer = eliminatedPlayerId.HasValue,
                EliminatedPlayerId = eliminatedPlayerId.GetValueOrDefault()
            }
        };

        foreach (var survivorId in EnumerateAlivePlayers(eliminatedPlayerId))
        {
            bool isBlanco = IsPlayerBlanco(survivorId);
            int points = isBlanco ? pointsPerRoundSurvivedBlanco : pointsPerRoundSurvivedPlayer;
            if (points == 0)
                continue;

            report.Events.Add(new ScoreEvent
            {
                PlayerId = survivorId,
                Points = points,
                IsBlanco = isBlanco,
                Category = ScoreCategory.Survival
            });
        }

        if (conclusionType == RoundConclusionType.Elimination && eliminatedPlayerId.HasValue && pointsPerCorrectVote != 0)
        {
            foreach (var vote in playerVotes)
            {
                if (vote.Value != eliminatedPlayerId.Value)
                    continue;

                report.Events.Add(new ScoreEvent
                {
                    PlayerId = vote.Key,
                    Points = pointsPerCorrectVote,
                    IsBlanco = IsPlayerBlanco(vote.Key),
                    Category = ScoreCategory.CorrectVote
                });
            }
        }

        currentGameScoreReports.Add(report);
        MatchManager.Instance?.ProcessRoundScore(report);
    }

    IEnumerable<ulong> EnumerateAlivePlayers(ulong? excludedPlayerId = null)
    {
        if (NetworkManager.Singleton == null)
            yield break;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            ulong clientId = client.ClientId;
            if (excludedPlayerId.HasValue && excludedPlayerId.Value == clientId)
                continue;

            if (eliminatedPlayers.Contains(clientId))
                continue;

            yield return clientId;
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

    bool TryAssignBlancoPlayers()
    {
        if (blancosAssignedThisMatch && blancoPlayerIds.Count > 0)
        {
            return true;
        }

        // Selecciona candidatos vivos y no eliminados para ocupar los roles de Blanco.
        var availableClients = GetActiveClients().Select(client => client.ClientId).ToList();
        if (availableClients.Count == 0)
        {
            Debug.LogWarning("[RoundManager] No active players available to pick a Blanco.");
            return false;
        }

        int blancosToSelect = Mathf.Clamp(blancosPerMatch, 1, availableClients.Count);
        availableClients = availableClients.OrderBy(_ => UnityEngine.Random.value).ToList();

        // Reiniciamos la lista sincronizada antes de aplicar la nueva asignacion.
        blancoPlayerIds.Clear();
        blancoPlayersCache.Clear();

        for (int i = 0; i < blancosToSelect; i++)
        {
            ulong candidate = availableClients[i];
            blancoPlayerIds.Add(candidate);
            blancoPlayersCache.Add(candidate);
        }

        blancosAssignedThisMatch = blancoPlayerIds.Count > 0;

        var blancoLabelsList = new List<string>(blancoPlayerIds.Count);
        foreach (var id in blancoPlayerIds)
        {
            blancoLabelsList.Add(id.ToString());
        }
        var blancoLabels = blancoLabelsList.ToArray();
        Debug.Log($"[RoundManager] Blanco players chosen: {string.Join(", ", blancoLabels)}");

        return blancosAssignedThisMatch;
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
                bool isBlanco = IsPlayerBlanco(client.ClientId);
                player.SetCardValuesClientRpc(chosenWord.Value, isBlanco);
            }
        }
    }

    public void ConfigureBlancosPerMatch(int count)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        // Mantiene la configuracion bajo control del host y evita valores invalidos.
        blancosPerMatch = Mathf.Max(1, count);
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

    int GetSeatTurnOrderSlot(int seatIndex)
    {
        if (seatIndex < 0)
            return SeatTurnOrder.Length + 500;

        for (int i = 0; i < SeatTurnOrder.Length; i++)
        {
            if (SeatTurnOrder[i] == seatIndex)
                return i;
        }

        return SeatTurnOrder.Length + seatIndex;
    }

    List<NetworkClient> GetActiveClientsInSeatingOrder()
    {
        var activeClients = GetActiveClients().ToList();
        if (activeClients.Count == 0)
            return activeClients;

        var playerSpawner = PlayerSpawner.Instance;
        var orderedEntries = new List<(NetworkClient client, int seatIndex, int orderSlot)>(activeClients.Count);
        int fallbackOrderBase = SeatTurnOrder.Length + 1000;
        int fallbackCounter = 0;

        foreach (var client in activeClients)
        {
            int seatIndex = -1;
            int orderSlot;

            if (playerSpawner != null && playerSpawner.TryGetSeatIndex(client.ClientId, out int assignedSeat))
            {
                seatIndex = assignedSeat;
                orderSlot = GetSeatTurnOrderSlot(seatIndex);
            }
            else
            {
                orderSlot = fallbackOrderBase + fallbackCounter;
                fallbackCounter++;
            }

            orderedEntries.Add((client, seatIndex, orderSlot));
        }

        orderedEntries.Sort((a, b) =>
        {
            int comparison = a.orderSlot.CompareTo(b.orderSlot);
            if (comparison != 0)
                return comparison;

            comparison = a.seatIndex.CompareTo(b.seatIndex);
            if (comparison != 0)
                return comparison;

            return a.client.ClientId.CompareTo(b.client.ClientId);
        });

        if (orderedEntries.Count == 0)
            return new List<NetworkClient>();

        if (currentStartingOrderSlot < 0)
        {
            var randomEntry = orderedEntries[UnityEngine.Random.Range(0, orderedEntries.Count)];
            currentStartingOrderSlot = randomEntry.orderSlot;
        }

        int startIndex = orderedEntries.FindIndex(entry => entry.orderSlot == currentStartingOrderSlot);
        if (startIndex < 0)
        {
            startIndex = orderedEntries.FindIndex(entry => entry.orderSlot > currentStartingOrderSlot);
            if (startIndex < 0)
            {
                startIndex = 0;
            }
        }

        currentStartingOrderSlot = orderedEntries[startIndex].orderSlot;

        var orderedClients = new List<NetworkClient>(orderedEntries.Count);
        for (int i = 0; i < orderedEntries.Count; i++)
        {
            var entry = orderedEntries[(startIndex + i) % orderedEntries.Count];
            orderedClients.Add(entry.client);
        }

        return orderedClients;
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
    }
}
