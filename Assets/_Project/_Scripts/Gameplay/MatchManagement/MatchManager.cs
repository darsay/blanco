using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Blanco.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance;

    public enum MatchState : byte { WaitingForPlayers, Playing, Result }

    [Header("Match Settings")]
    [SerializeField, Min(1)] private int gamesPerMatch = 3;
    [SerializeField] private float firstGameMultiplier = 1f;
    [SerializeField] private float gameMultiplierIncrement = 0.5f;

    [Header("Scoreboard")]
    [SerializeField, Min(0.1f)] private float scoreboardStepDuration = 3f;

    [Header("Debug")]
    [SerializeField] private bool logScoreEvents;

    public NetworkVariable<MatchState> currentState = new NetworkVariable<MatchState>(MatchState.WaitingForPlayers, writePerm: NetworkVariableWritePermission.Server);

    [Serializable]
    public struct PlayerScoreState : INetworkSerializable, IEquatable<PlayerScoreState>
    {
        public ulong PlayerId;
        public int Score;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Score);
        }

        public bool Equals(PlayerScoreState other) => PlayerId == other.PlayerId && Score == other.Score;
    }

    [Serializable]
    public class ScoreEventReport
    {
        public ulong PlayerId;
        public int BasePoints;
        public int FinalPoints;
        public bool IsBlanco;
        public string Reason;
        public RoundManager.ScoreCategory Category;
    }

    [Serializable]
    public class RoundScoreDetails
    {
        public int RoundNumber;
        public RoundManager.ScorePhase Phase;
        public float MultiplierApplied;
        public RoundManager.RoundSummaryData RoundSummary;
        public RoundManager.GameResultSummaryData GameSummary;
        public string Summary;
        public List<ScoreEventReport> Events = new();
    }

    [Serializable]
    public class GameScoreSummary
    {
        public int GameIndex;
        public bool PlayersWon;
        public string VictoryReason;
        public float MultiplierApplied;
        public List<RoundScoreDetails> Rounds = new();
    }

    public struct RoundScoreEventDto : INetworkSerializable
    {
        public ulong PlayerId;
        public int BasePoints;
        public int FinalPoints;
        public bool IsBlanco;
        public FixedString128Bytes Reason;
        public RoundManager.ScoreCategory Category;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref BasePoints);
            serializer.SerializeValue(ref FinalPoints);
            serializer.SerializeValue(ref IsBlanco);
            serializer.SerializeValue(ref Reason);
            serializer.SerializeValue(ref Category);
        }
    }

    public struct RoundScoreDto : INetworkSerializable
    {
        public int RoundNumber;
        public RoundManager.ScorePhase Phase;
        public float MultiplierApplied;
        public RoundManager.RoundSummaryData RoundSummary;
        public RoundManager.GameResultSummaryData GameSummary;
        public FixedString128Bytes Summary;
        public List<RoundScoreEventDto> Events;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref RoundNumber);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref MultiplierApplied);
            var conclusion = RoundSummary.ConclusionType;
            serializer.SerializeValue(ref conclusion);
            var hasEliminated = RoundSummary.HasEliminatedPlayer;
            serializer.SerializeValue(ref hasEliminated);
            ulong eliminatedId = RoundSummary.EliminatedPlayerId;
            serializer.SerializeValue(ref eliminatedId);

            var playersWin = GameSummary.PlayersWin;
            serializer.SerializeValue(ref playersWin);
            var winCondition = GameSummary.WinCondition;
            serializer.SerializeValue(ref winCondition);
            int roundsSnapshot = GameSummary.RoundsCompletedSnapshot;
            serializer.SerializeValue(ref roundsSnapshot);
            int activePlayersSnapshot = GameSummary.ActivePlayersSnapshot;
            serializer.SerializeValue(ref activePlayersSnapshot);
            var anyBlancoAliveSnapshot = GameSummary.AnyBlancoAliveSnapshot;
            serializer.SerializeValue(ref anyBlancoAliveSnapshot);
            serializer.SerializeValue(ref Summary);

            int count = Events != null ? Events.Count : 0;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                RoundSummary.ConclusionType = conclusion;
                RoundSummary.HasEliminatedPlayer = hasEliminated;
                RoundSummary.EliminatedPlayerId = eliminatedId;
                GameSummary.PlayersWin = playersWin;
                GameSummary.WinCondition = winCondition;
                GameSummary.RoundsCompletedSnapshot = roundsSnapshot;
                GameSummary.ActivePlayersSnapshot = activePlayersSnapshot;
                GameSummary.AnyBlancoAliveSnapshot = anyBlancoAliveSnapshot;
                Events ??= new List<RoundScoreEventDto>(count);
                Events.Clear();
                if (Events.Capacity < count)
                {
                    Events.Capacity = count;
                }

                for (int i = 0; i < count; i++)
                {
                    var evt = new RoundScoreEventDto();
                    serializer.SerializeValue(ref evt);
                    Events.Add(evt);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var evt = Events[i];
                    serializer.SerializeValue(ref evt);
                }
            }
        }
    }

    public struct GameScoreBroadcast : INetworkSerializable
    {
        public int GameIndex;
        public bool PlayersWon;
        public FixedString128Bytes VictoryReason;
        public float MultiplierApplied;
        public float StepDuration;
        public List<RoundScoreDto> Rounds;
        public bool IsFinalGame;
        public List<ulong> WinnerIds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref GameIndex);
            serializer.SerializeValue(ref PlayersWon);
            serializer.SerializeValue(ref VictoryReason);
            serializer.SerializeValue(ref MultiplierApplied);
            serializer.SerializeValue(ref StepDuration);

            int roundCount = Rounds != null ? Rounds.Count : 0;
            serializer.SerializeValue(ref roundCount);

            if (serializer.IsReader)
            {
                Rounds ??= new List<RoundScoreDto>(roundCount);
                Rounds.Clear();
                if (Rounds.Capacity < roundCount)
                {
                    Rounds.Capacity = roundCount;
                }

                for (int i = 0; i < roundCount; i++)
                {
                    var round = new RoundScoreDto();
                    serializer.SerializeValue(ref round);
                    Rounds.Add(round);
                }
            }
            else
            {
                for (int i = 0; i < roundCount; i++)
                {
                    var round = Rounds[i];
                    serializer.SerializeValue(ref round);
                }
            }

            serializer.SerializeValue(ref IsFinalGame);

            int winnerCount = WinnerIds != null ? WinnerIds.Count : 0;
            serializer.SerializeValue(ref winnerCount);

            if (serializer.IsReader)
            {
                WinnerIds ??= new List<ulong>(winnerCount);
                WinnerIds.Clear();
                if (WinnerIds.Capacity < winnerCount)
                {
                    WinnerIds.Capacity = winnerCount;
                }

                for (int i = 0; i < winnerCount; i++)
                {
                    ulong id = default;
                    serializer.SerializeValue(ref id);
                    WinnerIds.Add(id);
                }
            }
            else
            {
                for (int i = 0; i < winnerCount; i++)
                {
                    ulong id = WinnerIds[i];
                    serializer.SerializeValue(ref id);
                }
            }
        }
    }

    private readonly Dictionary<ulong, int> playersAndScores = new();
    private readonly NetworkList<PlayerScoreState> syncedScores = new();
    private readonly List<GameScoreSummary> completedGames = new();
    private readonly List<ulong> matchWinners = new();
    private List<RoundScoreDetails> currentGameScoreDetails;
    private int currentGameIndex;
    private Coroutine postGameRoutine;

    void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        RegisterConnectedPlayers();
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        NetworkManager.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        if (!playersAndScores.ContainsKey(clientId))
        {
            SetPlayerScore(clientId, 0);
        }
    }

    public void OnBeginMatch()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        if (currentState.Value == MatchState.Playing)
            return;

        ResetMatchState();
        currentState.Value = MatchState.Playing;

        HideGameScoreClientRpc();
        RoundManager.Instance?.StartGame();
        OnBeginMatchClientRpc();
    }

    void ResetMatchState()
    {
        playersAndScores.Clear();
        syncedScores.Clear();
        completedGames.Clear();
        matchWinners.Clear();
        currentGameScoreDetails = null;
        currentGameIndex = 0;

        RegisterConnectedPlayers();
    }

    void RegisterConnectedPlayers()
    {
        if (NetworkManager.Singleton == null)
            return;

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SetPlayerScore(clientId, 0);
        }

        if (NetworkManager.Singleton.IsListening)
        {
            ulong hostId = NetworkManager.Singleton.LocalClientId;
            int existing = playersAndScores.TryGetValue(hostId, out var storedScore) ? storedScore : 0;
            SetPlayerScore(hostId, existing);
        }
    }

    void SetPlayerScore(ulong playerId, int score)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        playersAndScores[playerId] = score;

        int index = -1;
        for (int i = 0; i < syncedScores.Count; i++)
        {
            if (syncedScores[i].PlayerId == playerId)
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            var entry = syncedScores[index];
            entry.Score = score;
            syncedScores[index] = entry;
        }
        else
        {
            syncedScores.Add(new PlayerScoreState { PlayerId = playerId, Score = score });
        }
    }

    float GetGameMultiplier(int gameIndex)
    {
        return Mathf.Max(0f, firstGameMultiplier + gameMultiplierIncrement * gameIndex);
    }

    float GetCurrentGameMultiplier()
    {
        return GetGameMultiplier(currentGameIndex);
    }

    public IReadOnlyDictionary<ulong, int> PlayerScores => playersAndScores;
    public IReadOnlyList<GameScoreSummary> CompletedGames => completedGames;
    public IReadOnlyList<RoundScoreDetails> CurrentGameScoreDetails => currentGameScoreDetails ?? (IReadOnlyList<RoundScoreDetails>)Array.Empty<RoundScoreDetails>();
    public IReadOnlyList<ulong> MatchWinners => matchWinners;
    public NetworkList<PlayerScoreState> SyncedScores => syncedScores;
    public float CurrentMultiplier => GetCurrentGameMultiplier();
    public int GamesPerMatch => gamesPerMatch;

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
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

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

    [ClientRpc]
    void ShowGameScoreClientRpc(GameScoreBroadcast payload)
    {
        UIGameplayManager.Instance?.DisplayGameScore(payload);
    }

    [ClientRpc]
    void HideGameScoreClientRpc()
    {
        UIGameplayManager.Instance?.HideGameScorePanel();
    }

    internal void OnRoundManagerGameStarted()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        if (postGameRoutine != null)
        {
            StopCoroutine(postGameRoutine);
            postGameRoutine = null;
        }

        currentGameScoreDetails = new List<RoundScoreDetails>();
        HideGameScoreClientRpc();
    }

    internal void ProcessRoundScore(RoundManager.RoundScoreReport report)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || report == null)
            return;

        if (currentGameScoreDetails == null)
        {
            currentGameScoreDetails = new List<RoundScoreDetails>();
        }

        float multiplier = GetCurrentGameMultiplier();
        string summaryText = BuildRoundSummaryText(report);
        var processed = new RoundScoreDetails
        {
            RoundNumber = report.RoundNumber,
            Phase = report.Phase,
            Summary = summaryText,
            MultiplierApplied = multiplier,
            RoundSummary = report.RoundSummary,
            GameSummary = report.GameSummary
        };

        foreach (var evt in report.Events)
        {
            int basePoints = evt.Points;
            int finalPoints = Mathf.RoundToInt(basePoints * multiplier);
            string reasonText = BuildScoreEventReasonText(report, evt);

            int existing = playersAndScores.TryGetValue(evt.PlayerId, out var stored) ? stored : 0;
            SetPlayerScore(evt.PlayerId, existing + finalPoints);

            processed.Events.Add(new ScoreEventReport
            {
                PlayerId = evt.PlayerId,
                BasePoints = basePoints,
                FinalPoints = finalPoints,
                IsBlanco = evt.IsBlanco,
                Reason = reasonText,
                Category = evt.Category
            });

            if (logScoreEvents)
            {
                Debug.Log($"[MatchManager] Game {currentGameIndex + 1} round {report.RoundNumber}: player {evt.PlayerId} +{finalPoints} points (base {basePoints}) for {reasonText}.");
            }
        }

        currentGameScoreDetails.Add(processed);
    }

    internal void OnRoundManagerGameEnded(bool playersWin, string victoryReason)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        int completedGameIndex = currentGameIndex;
        var summary = new GameScoreSummary
        {
            GameIndex = completedGameIndex,
            PlayersWon = playersWin,
            VictoryReason = victoryReason,
            MultiplierApplied = GetGameMultiplier(completedGameIndex),
            Rounds = CloneRoundScores(currentGameScoreDetails)
        };

        completedGames.Add(summary);
        currentGameScoreDetails = null;

        bool isFinalGame = completedGameIndex + 1 >= gamesPerMatch;
        List<ulong> winnersSnapshot = null;

        if (isFinalGame)
        {
            winnersSnapshot = CalculateCurrentWinners();
            matchWinners.Clear();
            matchWinners.AddRange(winnersSnapshot);
        }

        BroadcastGameScore(summary, isFinalGame, winnersSnapshot);

        currentGameIndex++;

        float waitDuration = GetScoreboardSequenceDuration(summary, isFinalGame);

        if (postGameRoutine != null)
        {
            StopCoroutine(postGameRoutine);
        }

        postGameRoutine = StartCoroutine(HandlePostGameSequence(waitDuration, isFinalGame));
    }

    void BroadcastGameScore(GameScoreSummary summary, bool isFinalGame, List<ulong> winnersSnapshot)
    {
        var payload = new GameScoreBroadcast
        {
            GameIndex = summary.GameIndex,
            PlayersWon = summary.PlayersWon,
            MultiplierApplied = summary.MultiplierApplied,
            Rounds = new List<RoundScoreDto>(summary.Rounds.Count),
            IsFinalGame = isFinalGame,
            WinnerIds = winnersSnapshot != null ? new List<ulong>(winnersSnapshot) : new List<ulong>()
        };

        payload.StepDuration = Mathf.Max(0.1f, scoreboardStepDuration);

        FixedString128Bytes victoryReason = summary.VictoryReason ?? string.Empty;
        payload.VictoryReason = victoryReason;

        foreach (var round in summary.Rounds)
        {
            var dto = new RoundScoreDto
            {
                RoundNumber = round.RoundNumber,
                Phase = round.Phase,
                MultiplierApplied = round.MultiplierApplied,
                RoundSummary = round.RoundSummary,
                GameSummary = round.GameSummary,
                Events = new List<RoundScoreEventDto>(round.Events.Count)
            };

            FixedString128Bytes summaryFs = round.Summary ?? string.Empty;
            dto.Summary = summaryFs;

            foreach (var evt in round.Events)
            {
                var eventDto = new RoundScoreEventDto
                {
                    PlayerId = evt.PlayerId,
                    BasePoints = evt.BasePoints,
                    FinalPoints = evt.FinalPoints,
                    Category = evt.Category,
                    IsBlanco = evt.IsBlanco
                };
                eventDto.Reason = evt.Reason ?? string.Empty;
                dto.Events.Add(eventDto);
            }

            payload.Rounds.Add(dto);
        }

        ShowGameScoreClientRpc(payload);
    }

    float GetScoreboardSequenceDuration(GameScoreSummary summary, bool isFinalGame)
    {
        int roundStageCount = summary.Rounds.Count > 0 ? summary.Rounds.Count : 1;
        int stageCount = roundStageCount + 2;
        if (isFinalGame)
        {
            stageCount += 1;
        }

        float stepDuration = Mathf.Max(0.1f, scoreboardStepDuration);
        return stageCount * stepDuration;
    }

    string BuildRoundSummaryText(RoundManager.RoundScoreReport report)
    {
        if (report == null)
            return string.Empty;

        return report.Phase switch
        {
            RoundManager.ScorePhase.RoundCompleted => BuildRoundCompletedSummary(report.RoundNumber, report.RoundSummary),
            RoundManager.ScorePhase.GameResult => BuildGameResultSummary(report.GameSummary),
            _ => string.Empty
        };
    }

    string BuildRoundCompletedSummary(int roundNumber, RoundManager.RoundSummaryData summary)
    {
        switch (summary.ConclusionType)
        {
            case RoundManager.RoundConclusionType.Elimination:
                if (summary.HasEliminatedPlayer)
                {
                    string name = GetDisplayName(summary.EliminatedPlayerId);
                    return $"Ronda {roundNumber}: {name} fue eliminado";
                }
                return $"Ronda {roundNumber}: un jugador fue eliminado";
            case RoundManager.RoundConclusionType.Tie:
                return $"Ronda {roundNumber}: Empate en la votacion";
            case RoundManager.RoundConclusionType.InvalidVotes:
                return $"Ronda {roundNumber}: Los votos fueron invalidos";
            case RoundManager.RoundConclusionType.NoVotes:
                return $"Ronda {roundNumber}: Nadie voto";
            default:
                return $"Ronda {roundNumber}";
        }
    }

    string BuildGameResultSummary(RoundManager.GameResultSummaryData summary)
    {
        string headline = summary.PlayersWin ? "Victoria para los jugadores" : "Victoria para el Blanco";
        string reason;
        switch (summary.WinCondition)
        {
            case RoundManager.WinConditionType.Rounds:
                reason = summary.AnyBlancoAliveSnapshot
                    ? $"Se completaron {summary.RoundsCompletedSnapshot} rondas y aun queda al menos un Blanco vivo."
                    : $"Se completaron {summary.RoundsCompletedSnapshot} rondas y no quedan Blancos con vida.";
                break;
            case RoundManager.WinConditionType.RemainingPlayers:
                reason = summary.ActivePlayersSnapshot == 1
                    ? "Solo queda un jugador activo en la mesa."
                    : $"Solo quedan {summary.ActivePlayersSnapshot} jugadores activos.";
                break;
            default:
                reason = string.Empty;
                break;
        }

        if (string.IsNullOrWhiteSpace(reason))
            return headline;
        return $"{headline} - {reason}";
    }

    string BuildScoreEventReasonText(RoundManager.RoundScoreReport report, RoundManager.ScoreEvent evt)
    {
        switch (evt.Category)
        {
            case RoundManager.ScoreCategory.Survival:
                return $"Sobrevivio la ronda {report.RoundNumber}";
            case RoundManager.ScoreCategory.CorrectVote:
                return $"Voto acertado en la ronda {report.RoundNumber}";
            case RoundManager.ScoreCategory.GameWin:
                return evt.IsBlanco ? "Gano la partida como Blanco" : "Gano la partida como jugador";
            default:
                return string.Empty;
        }
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

    IEnumerator HandlePostGameSequence(float waitDuration, bool isFinalGame)
    {
        if (waitDuration > 0f)
        {
            yield return new WaitForSeconds(waitDuration);
        }

        postGameRoutine = null;

        if (isFinalGame)
        {
            FinalizeMatch();
        }
        else
        {
            RoundManager.Instance?.StartGame();
        }
    }

    List<ulong> CalculateCurrentWinners()
    {
        if (playersAndScores.Count == 0)
            return new List<ulong>();

        int maxScore = playersAndScores.Values.Max();
        var winners = new List<ulong>();
        foreach (var kv in playersAndScores)
        {
            if (kv.Value == maxScore)
            {
                winners.Add(kv.Key);
            }
        }
        return winners;
    }

    List<RoundScoreDetails> CloneRoundScores(List<RoundScoreDetails> source)
    {
        if (source == null || source.Count == 0)
            return new List<RoundScoreDetails>();

        var clone = new List<RoundScoreDetails>(source.Count);
        foreach (var round in source)
        {
            var roundCopy = new RoundScoreDetails
            {
                RoundNumber = round.RoundNumber,
                Phase = round.Phase,
                MultiplierApplied = round.MultiplierApplied,
                Summary = round.Summary,
                RoundSummary = round.RoundSummary,
                GameSummary = round.GameSummary
            };

            foreach (var evt in round.Events)
            {
                roundCopy.Events.Add(new ScoreEventReport
                {
                    PlayerId = evt.PlayerId,
                    BasePoints = evt.BasePoints,
                    FinalPoints = evt.FinalPoints,
                    IsBlanco = evt.IsBlanco,
                    Reason = evt.Reason,
                    Category = evt.Category
                });
            }

            clone.Add(roundCopy);
        }

        return clone;
    }

    void FinalizeMatch()
    {
        currentState.Value = MatchState.Result;

        matchWinners.Clear();
        if (playersAndScores.Count > 0)
        {
            int maxScore = playersAndScores.Values.Max();
            foreach (var kv in playersAndScores)
            {
                if (kv.Value == maxScore)
                {
                    matchWinners.Add(kv.Key);
                }
            }
        }

        ShowWaitingUI();
    }
}
