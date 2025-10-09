using Blanco.Networking;
using Blanco.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class UIGameplayManager : NetworkBehaviour
{
    public static UIGameplayManager Instance;

    public GameObject pressToBegin;

    public GameObject waitingUI;

    [SerializeField]
    PlayerNamesList playerNamesList;

    [SerializeField]
    TextMeshProUGUI infoText;

    [SerializeField]
    GameTimer gameTimer;

    [Header("Scoreboard UI")]
    [SerializeField] private GameObject gameScorePanel;
    [SerializeField] private TextMeshProUGUI gameScoreTitleText;
    [SerializeField] private TextMeshProUGUI gameScoreRoundsText;
    [SerializeField] private TextMeshProUGUI gameScoreGameTotalsText;
    [SerializeField] private TextMeshProUGUI gameScoreCumulativeTotalsText;
    [SerializeField] private TextMeshProUGUI gameScoreWinnerText;

    private Coroutine scoreDisplayCoroutine;

    [Header("Settings")]
    [SerializeField] private float updateInterval = 1f;

    [Header("Lobby Panel")]
    [SerializeField] private TextMeshProUGUI lobbyCodeText;
    [SerializeField] private Button copyCodeButton;
    [SerializeField] private TextMeshProUGUI playerCountText;

    [Header("Leave Lobby")]
    [SerializeField] private Button leaveLobbyButton;
    [SerializeField] private TextMeshProUGUI leaveButtonText;

    [Header("Voting UI")]
    [SerializeField] private TextMeshProUGUI voteSelectionText;

    [Header("Win Condition UI")]
    [SerializeField] private TMP_Dropdown winConditionDropdown;
    [SerializeField] private TMP_InputField winConditionValueInput;
    [SerializeField] private Button applyWinConditionButton;
    [SerializeField] private TextMeshProUGUI winConditionSummaryText;

    private LobbyManager lobbyManager;
    private float lastUpdateTime;
    private bool suppressWinConditionCallbacks;

    void Awake()
    {
        Instance = this;
        if (infoText != null)
        {
            infoText.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        // Usar el singleton LobbyManager
        lobbyManager = LobbyManager.Instance;
        if (lobbyManager == null)
        {
            Debug.LogWarning("?? LobbyManager.Instance es null, creando uno automáticamente...");

            // Crear un GameObject con LobbyManager
            GameObject lobbyManagerGO = new GameObject("LobbyManager");
            lobbyManager = lobbyManagerGO.AddComponent<LobbyManager>();

            Debug.Log("? LobbyManager creado automáticamente");
        }

        // Suscribirse a eventos
        lobbyManager.OnPlayersUpdated += OnPlayersUpdated;
        lobbyManager.OnLobbyStateChanged += OnLobbyStateChanged;

        // Configurar botones
        if (copyCodeButton != null)
            copyCodeButton.onClick.AddListener(OnCopyCodeClicked);

        if (leaveLobbyButton != null)
            leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);

        // Mostrar código del lobby
        UpdateLobbyCode();

        // Configurar UI inicial
        UpdateUI();
        InitializeWinConditionUI();
        HideGameScorePanel();
    }

    private void OnDisable()
    {
        if (lobbyManager != null)
        {
            lobbyManager.OnPlayersUpdated -= OnPlayersUpdated;
            lobbyManager.OnLobbyStateChanged -= OnLobbyStateChanged;
        }

        if (winConditionDropdown != null)
        {
            winConditionDropdown.onValueChanged.RemoveListener(OnWinConditionDropdownChanged);
        }

        if (winConditionValueInput != null)
        {
            winConditionValueInput.onEndEdit.RemoveListener(OnWinConditionValueEdited);
        }

        if (applyWinConditionButton != null)
        {
            applyWinConditionButton.onClick.RemoveListener(OnApplyWinConditionClicked);
        }

        CancelInvoke(nameof(RequestWinConditionSync));
    }

    private void Update()
    {
        // Actualizar UI periódicamente
        if (Time.time - lastUpdateTime > updateInterval)
        {
            lastUpdateTime = Time.time;
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        if (lobbyManager == null) return;

        // Actualizar información del lobby
        UpdateLobbyInfo();

        // Actualizar texto del botón de salir
        UpdateLeaveButtonText();
        UpdateWinConditionInteractableState(IsLocalHost());
    }

    private void UpdateLobbyInfo()
    {
        // Mostrar código del lobby
        UpdateLobbyCode();

        // Mostrar contador de jugadores
        if (playerCountText != null)
        {
            int currentPlayers = lobbyManager.GetPlayerCount();
            int maxPlayers = lobbyManager.GetMaxPlayers();
            playerCountText.text = $"Jugadores: {currentPlayers}/{maxPlayers}";
        }

        // Actualizar lista de jugadores
        UpdatePlayersList();
    }

    private void UpdatePlayersList()
    {
        if (lobbyManager == null) return;

        // Limpiar lista actual
        playerNamesList.ClearPlayerList();

        // Obtener lista de jugadores
        var players = lobbyManager.GetPlayers();
        if (players == null || players.Count == 0)
        {
            return;
        }

        // Crear elementos para cada jugador
        foreach (var player in players)
        {
            playerNamesList.AddNewPanel(player);
        }
    }

    private void UpdateLeaveButtonText()
    {
        if (leaveButtonText != null)
        {
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            leaveButtonText.text = isHost ? "Cerrar Lobby" : "Abandonar Lobby";
        }
    }

    private void UpdateLobbyCode()
    {
        if (lobbyCodeText != null)
        {
            string lobbyCode = PlayerPrefs.GetString("LobbyCode", "");
            if (!string.IsNullOrEmpty(lobbyCode))
            {
                lobbyCodeText.text = $"Código: {lobbyCode}";
            }
            else
            {
                lobbyCodeText.text = "Código: No disponible";
            }
        }
    }

    private void OnCopyCodeClicked()
    {
        string lobbyCode = PlayerPrefs.GetString("LobbyCode", "");
        if (!string.IsNullOrEmpty(lobbyCode))
        {
            GUIUtility.systemCopyBuffer = lobbyCode;
            Debug.Log($"? Código copiado: {lobbyCode}");
        }
    }

    private void OnLeaveLobbyClicked()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        if (isHost)
        {
            Debug.Log("?? Host cerrando lobby...");
            // El host cierra el lobby para todos
            lobbyManager?.CloseLobby();
        }
        else
        {
            Debug.Log("?? Cliente abandonando lobby...");
            // El cliente solo se va
            lobbyManager?.LeaveLobby();
        }

        // Limpiar datos del lobby
        PlayerPrefs.DeleteKey("LobbyCode");

        // Volver al menú principal
        UnityEngine.SceneManagement.SceneManager.LoadScene("Menu");
    }

    // Eventos del LobbyManager
    private void OnPlayersUpdated(List<LobbyManager.PlayerInfo> players)
    {
        Debug.Log($"?? Lista de jugadores actualizada: {players.Count} jugadores");

        // Actualizar lista de jugadores específicamente
        UpdatePlayersList();

        // También actualizar el resto de la UI
        UpdateUI();
    }

    private void OnLobbyStateChanged(LobbyManager.LobbyState newState)
    {
        Debug.Log($"?? Estado del lobby cambiado: {newState}");
        UpdateUI();
    }

    [ClientRpc]
    public void SetInfoTextClientRpc(string text)
    {
        if (infoText == null) return;
        infoText.gameObject.SetActive(true);
        infoText.text = text;
    }

    [ClientRpc]
    public void HideInfoTextClientRpc()
    {
        if (infoText == null) return;
        infoText.gameObject.SetActive(false);
    }

    public void SetLocalVoteSelection(string text)
    {
        if (voteSelectionText != null)
        {
            voteSelectionText.text = text;
        }
    }

    void InitializeWinConditionUI()
    {
        bool isHost = IsLocalHost();

        if (winConditionDropdown != null)
        {
            winConditionDropdown.onValueChanged.AddListener(OnWinConditionDropdownChanged);
            winConditionDropdown.interactable = isHost;
        }

        if (winConditionValueInput != null)
        {
            winConditionValueInput.onEndEdit.AddListener(OnWinConditionValueEdited);
            winConditionValueInput.interactable = isHost;
        }

        if (applyWinConditionButton != null)
        {
            applyWinConditionButton.onClick.AddListener(OnApplyWinConditionClicked);
            applyWinConditionButton.interactable = isHost;
        }

        UpdateWinConditionInteractableState(isHost);
        SyncWinConditionUI();

        if (isHost)
        {
            Invoke(nameof(RequestWinConditionSync), 0.5f);
        }
    }

    void UpdateWinConditionInteractableState(bool isHost)
    {
        if (winConditionDropdown != null)
        {
            winConditionDropdown.interactable = isHost;
        }

        if (winConditionValueInput != null)
        {
            winConditionValueInput.interactable = isHost;
        }

        if (applyWinConditionButton != null)
        {
            applyWinConditionButton.interactable = isHost;
        }
    }

    void SyncWinConditionUI()
    {
        if (RoundManager.Instance == null)
            return;

        SetWinConditionDisplay(RoundManager.Instance.CurrentWinConditionType, RoundManager.Instance.CurrentWinConditionThreshold);
    }

    bool IsLocalHost()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    }

    void OnWinConditionDropdownChanged(int index)
    {
        if (suppressWinConditionCallbacks)
            return;

        if (!IsLocalHost())
            return;

        ApplyWinConditionFromUI();
    }

    void OnWinConditionValueEdited(string _)
    {
        if (suppressWinConditionCallbacks)
            return;

        if (!IsLocalHost())
            return;

        ApplyWinConditionFromUI();
    }

    void OnApplyWinConditionClicked()
    {
        if (!IsLocalHost())
            return;

        ApplyWinConditionFromUI();
    }

    void ApplyWinConditionFromUI()
    {
        if (RoundManager.Instance == null)
            return;

        var type = GetSelectedWinConditionType();
        int threshold = Mathf.Max(1, ParseWinConditionInput());

        SetWinConditionDisplay(type, threshold);
        RoundManager.Instance.ConfigureWinCondition(type, threshold);
    }

    RoundManager.WinConditionType GetSelectedWinConditionType()
    {
        if (winConditionDropdown == null)
            return RoundManager.WinConditionType.Rounds;

        return winConditionDropdown.value == 0 ? RoundManager.WinConditionType.Rounds : RoundManager.WinConditionType.RemainingPlayers;
    }

    int ParseWinConditionInput()
    {
        if (winConditionValueInput == null)
        {
            return RoundManager.Instance != null ? RoundManager.Instance.CurrentWinConditionThreshold : 1;
        }

        return int.TryParse(winConditionValueInput.text, out var parsed) ? parsed : 1;
    }

    void RequestWinConditionSync()
    {
        if (!IsLocalHost())
            return;

        if (RoundManager.Instance == null)
        {
            Invoke(nameof(RequestWinConditionSync), 0.5f);
            return;
        }

        RoundManager.Instance.BroadcastCurrentWinCondition();
    }

    public void SetWinConditionDisplay(RoundManager.WinConditionType type, int threshold)
    {
        suppressWinConditionCallbacks = true;

        if (winConditionDropdown != null)
        {
            int index = type == RoundManager.WinConditionType.Rounds ? 0 : 1;
            if (winConditionDropdown.value != index)
            {
                winConditionDropdown.value = index;
            }
        }

        int clampedThreshold = Mathf.Max(1, threshold);

        if (winConditionValueInput != null)
        {
            string textValue = clampedThreshold.ToString();
            if (winConditionValueInput.text != textValue)
            {
                winConditionValueInput.text = textValue;
            }
        }

        if (winConditionSummaryText != null)
        {
            winConditionSummaryText.text = type == RoundManager.WinConditionType.Rounds
                ? $"Victory after {clampedThreshold} rounds"
                : $"Victory when {clampedThreshold} players remain";
        }

        suppressWinConditionCallbacks = false;
    }

    public void DisplayGameScore(MatchManager.GameScoreBroadcast payload)
    {
        HideGameScorePanel();

        if (gameScoreTitleText != null)
        {
            gameScoreTitleText.text = BuildGameScoreTitle(payload);
        }

        scoreDisplayCoroutine = StartCoroutine(PlayGameScoreSequence(payload));
    }

    IEnumerator PlayGameScoreSequence(MatchManager.GameScoreBroadcast payload)
    {
        float stepDuration = Mathf.Max(0.1f, payload.StepDuration);

        var roundEntries = BuildRoundEntries(payload, out var gameTotals);
        string totalsText = BuildGameTotalsText(gameTotals);
        string cumulativeText = BuildCumulativeTotalsText();
        string winnerText = payload.IsFinalGame ? BuildWinnerText(payload.WinnerIds) : string.Empty;

        if (gameScorePanel != null)
        {
            gameScorePanel.SetActive(true);
        }

        ClearGameScoreStageTexts();

        if (roundEntries.Count == 0)
        {
            roundEntries.Add("Sin eventos de puntuacion en este juego.");
        }

        foreach (var entry in roundEntries)
        {
            SetRoundText(entry);
            SetGameTotalsText(string.Empty);
            SetCumulativeText(string.Empty);
            SetWinnerText(string.Empty, false);

            yield return new WaitForSeconds(stepDuration);
        }

        SetRoundText(string.Empty);
        SetGameTotalsText(totalsText);
        SetCumulativeText(string.Empty);
        SetWinnerText(string.Empty, false);

        yield return new WaitForSeconds(stepDuration);

        SetRoundText(string.Empty);
        SetGameTotalsText(string.Empty);
        SetCumulativeText(cumulativeText);
        SetWinnerText(string.Empty, false);

        yield return new WaitForSeconds(stepDuration);

        if (payload.IsFinalGame)
        {
            bool hasWinner = !string.IsNullOrEmpty(winnerText);

            SetRoundText(string.Empty);
            SetGameTotalsText(string.Empty);
            SetCumulativeText(string.Empty);
            SetWinnerText(winnerText, hasWinner);

            yield return new WaitForSeconds(stepDuration);
        }

        ClearGameScoreTexts();

        if (gameScorePanel != null)
        {
            gameScorePanel.SetActive(false);
        }

        scoreDisplayCoroutine = null;
    }

    public void HideGameScorePanel()
    {
        if (scoreDisplayCoroutine != null)
        {
            StopCoroutine(scoreDisplayCoroutine);
            scoreDisplayCoroutine = null;
        }

        ClearGameScoreTexts();

        if (gameScorePanel != null)
        {
            gameScorePanel.SetActive(false);
        }
    }

    void ClearGameScoreTexts()
    {
        if (gameScoreTitleText != null)
        {
            gameScoreTitleText.text = string.Empty;
        }

        ClearGameScoreStageTexts();
    }

    void ClearGameScoreStageTexts()
    {
        SetRoundText(string.Empty);
        SetGameTotalsText(string.Empty);
        SetCumulativeText(string.Empty);
        SetWinnerText(string.Empty, false);
    }

    void SetRoundText(string value)
    {
        if (gameScoreRoundsText != null)
        {
            gameScoreRoundsText.text = value;
        }
    }

    void SetGameTotalsText(string value)
    {
        if (gameScoreGameTotalsText != null)
        {
            gameScoreGameTotalsText.text = value;
        }
    }

    void SetCumulativeText(string value)
    {
        if (gameScoreCumulativeTotalsText != null)
        {
            gameScoreCumulativeTotalsText.text = value;
        }
    }

    void SetWinnerText(string value, bool active)
    {
        if (gameScoreWinnerText == null)
            return;

        gameScoreWinnerText.text = value;
        gameScoreWinnerText.gameObject.SetActive(active && !string.IsNullOrEmpty(value));
    }

    string BuildGameScoreTitle(MatchManager.GameScoreBroadcast payload)
    {
        var builder = new StringBuilder();
        builder.Append("Juego ");
        builder.Append(payload.GameIndex + 1);
        builder.Append(payload.PlayersWon ? " - Ganaron los jugadores" : " - Gano el Blanco");

        string reason = payload.VictoryReason.ToString();
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.Append(" - ");
            builder.Append(reason);
        }

        builder.Append(" (x");
        builder.Append(payload.MultiplierApplied.ToString("0.##"));
        builder.Append(')');
        return builder.ToString();
    }

    List<string> BuildRoundEntries(MatchManager.GameScoreBroadcast payload, out Dictionary<ulong, int> gameTotals)
    {
        gameTotals = new Dictionary<ulong, int>();
        var entries = new List<string>();

        if (payload.Rounds == null)
        {
            return entries;
        }

        foreach (var round in payload.Rounds)
        {
            var builder = new StringBuilder();
            builder.Append("Ronda ");
            builder.Append(round.RoundNumber);
            builder.Append(" (");
            builder.Append(TranslateScorePhase(round.Phase));
            builder.Append(") x");
            builder.Append(round.MultiplierApplied.ToString("0.##"));
            builder.AppendLine();

            string summary = FormatRoundSummary(round);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                builder.Append("  ");
                builder.AppendLine(summary);
            }

            if (round.Events != null && round.Events.Count > 0)
            {
                foreach (var evt in round.Events)
                {
                    int points = evt.FinalPoints;
                    if (!gameTotals.ContainsKey(evt.PlayerId))
                    {
                        gameTotals[evt.PlayerId] = 0;
                    }
                    gameTotals[evt.PlayerId] += points;

                    string playerName = GetDisplayName(evt.PlayerId);
                    string reason = FormatScoreEventReason(round, evt);
                    string categoryLabel = TranslateScoreCategory(evt.Category);

                    builder.Append("  - ");
                    builder.Append(playerName);
                    builder.Append(": ");
                    if (points > 0)
                    {
                        builder.Append('+');
                    }
                    builder.Append(points);
                    builder.Append(" (");
                    builder.Append(categoryLabel);
                    builder.Append(')');
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        builder.Append(" - ");
                        builder.Append(reason);
                    }
                    builder.AppendLine();
                }
            }
            else
            {
                builder.AppendLine("  - Sin variaciones de puntuacion");
            }

            entries.Add(builder.ToString().TrimEnd());
        }

        return entries;
    }

    string FormatRoundSummary(MatchManager.RoundScoreDto round)
    {
        string summary = round.Summary.ToString();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        return round.Phase switch
        {
            RoundManager.ScorePhase.RoundCompleted => FormatRoundCompletedSummary(round),
            RoundManager.ScorePhase.GameResult => FormatGameResultSummary(round),
            _ => string.Empty
        };
    }

    string FormatRoundCompletedSummary(MatchManager.RoundScoreDto round)
    {
        var summary = round.RoundSummary;
        switch (summary.ConclusionType)
        {
            case RoundManager.RoundConclusionType.Elimination:
                if (summary.HasEliminatedPlayer)
                {
                    string eliminatedName = GetDisplayName(summary.EliminatedPlayerId);
                    return $"Ronda {round.RoundNumber}: {eliminatedName} fue eliminado";
                }
                return $"Ronda {round.RoundNumber}: un jugador fue eliminado";
            case RoundManager.RoundConclusionType.Tie:
                return $"Ronda {round.RoundNumber}: Empate en la votacion";
            case RoundManager.RoundConclusionType.InvalidVotes:
                return $"Ronda {round.RoundNumber}: Los votos fueron invalidos";
            case RoundManager.RoundConclusionType.NoVotes:
                return $"Ronda {round.RoundNumber}: Nadie voto";
            default:
                return $"Ronda {round.RoundNumber}";
        }
    }

    string FormatGameResultSummary(MatchManager.RoundScoreDto round)
    {
        var summary = round.GameSummary;
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
        {
            return headline;
        }

        return $"{headline} - {reason}";
    }

    string FormatScoreEventReason(MatchManager.RoundScoreDto round, MatchManager.RoundScoreEventDto evt)
    {
        string reason = evt.Reason.ToString();
        if (!string.IsNullOrWhiteSpace(reason))
        {
            return reason;
        }

        switch (evt.Category)
        {
            case RoundManager.ScoreCategory.Survival:
                return $"Sobrevivio la ronda {round.RoundNumber}";
            case RoundManager.ScoreCategory.CorrectVote:
                return $"Voto acertado en la ronda {round.RoundNumber}";
            case RoundManager.ScoreCategory.GameWin:
                return evt.IsBlanco ? "Gano la partida como Blanco" : "Gano la partida como jugador";
            default:
                return string.Empty;
        }
    }

    string BuildGameTotalsText(Dictionary<ulong, int> gameTotals)
    {
        var matchManager = MatchManager.Instance;
        if (matchManager != null)
        {
            foreach (var entry in matchManager.SyncedScores)
            {
                if (!gameTotals.ContainsKey(entry.PlayerId))
                {
                    gameTotals[entry.PlayerId] = 0;
                }
            }
        }

        if (gameTotals.Count == 0)
        {
            return "Puntos del juego:\n  Sin variaciones.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Puntos del juego:");
        foreach (var kv in gameTotals.OrderByDescending(pair => pair.Value))
        {
            string playerName = GetDisplayName(kv.Key);
            builder.Append("  ");
            builder.Append(playerName);
            builder.Append(": ");
            if (kv.Value > 0)
            {
                builder.Append('+');
            }
            builder.Append(kv.Value);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    string BuildCumulativeTotalsText()
    {
        var matchManager = MatchManager.Instance;
        if (matchManager == null || matchManager.SyncedScores.Count == 0)
        {
            return "Totales acumulados:\n  Sin datos.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Totales acumulados:");

        // NetworkList<T> may not expose LINQ operators directly in all Netcode versions,
        // so copy to a List and sort it explicitly before iterating.
        var orderedScores = new List<MatchManager.PlayerScoreState>();
        foreach (var score in matchManager.SyncedScores)
        {
            orderedScores.Add(score);
        }
        orderedScores.Sort((a, b) => b.Score.CompareTo(a.Score));

        foreach (var entry in orderedScores)
        {
            string playerName = GetDisplayName(entry.PlayerId);
            builder.Append("  ");
            builder.Append(playerName);
            builder.Append(": ");
            builder.Append(entry.Score);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    string BuildWinnerText(List<ulong> winnerIds)
    {
        if (winnerIds == null || winnerIds.Count == 0)
        {
            return string.Empty;
        }

        var names = new List<string>(winnerIds.Count);
        foreach (var id in winnerIds)
        {
            names.Add(GetDisplayName(id));
        }

        if (names.Count == 1)
        {
            return $"Ganador de la partida: {names[0]}";
        }

        return $"Ganadores de la partida: {string.Join(", ", names)}";
    }

    string TranslateScoreCategory(RoundManager.ScoreCategory category)
    {
        switch (category)
        {
            case RoundManager.ScoreCategory.Survival:
                return "Supervivencia";
            case RoundManager.ScoreCategory.CorrectVote:
                return "Voto acertado";
            case RoundManager.ScoreCategory.GameWin:
                return "Victoria";
            default:
                return category.ToString();
        }
    }

    string TranslateScorePhase(RoundManager.ScorePhase phase)
    {
        switch (phase)
        {
            case RoundManager.ScorePhase.GameResult:
                return "Resultado del juego";
            case RoundManager.ScorePhase.RoundCompleted:
            default:
                return "Ronda completada";
        }
    }

    public void StartGameTimer(float duration)
    {
        if (gameTimer == null)
            return;

        gameTimer.SetVisibility(true);
        gameTimer.StartTimerServerRpc(duration);
    }

    public void StopGameTimer()
    {
        if (gameTimer == null)
            return;

        if (IsServer)
        {
            gameTimer.StopTimerImmediate();
        }
        else
        {
            gameTimer.SetVisibility(false);
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
}
