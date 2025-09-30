using Blanco.Networking;
using Blanco.UI;
using System.Collections.Generic;
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
        SetLocalVoteSelection("No target selected");
        InitializeWinConditionUI();
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

    [ClientRpc]
    public void ClearVoteSelectionClientRpc()
    {
        SetLocalVoteSelection("No target selected");
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
}
