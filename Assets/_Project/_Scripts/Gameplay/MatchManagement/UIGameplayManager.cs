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

    private Blanco.Networking.LobbyManager lobbyManager;
    private float lastUpdateTime;

    private void Start()
    {
        // Usar el singleton LobbyManager
        lobbyManager = Blanco.Networking.LobbyManager.Instance;
        if (lobbyManager == null)
        {
            Debug.LogWarning("⚠️ LobbyManager.Instance es null, creando uno automáticamente...");

            // Crear un GameObject con LobbyManager
            GameObject lobbyManagerGO = new GameObject("LobbyManager");
            lobbyManager = lobbyManagerGO.AddComponent<Blanco.Networking.LobbyManager>();

            Debug.Log("✅ LobbyManager creado automáticamente");
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
    }

    private void OnDestroy()
    {
        if (lobbyManager != null)
        {
            lobbyManager.OnPlayersUpdated -= OnPlayersUpdated;
            lobbyManager.OnLobbyStateChanged -= OnLobbyStateChanged;
        }
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
            Debug.Log($"✅ Código copiado: {lobbyCode}");
        }
    }

    private void OnLeaveLobbyClicked()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        if (isHost)
        {
            Debug.Log("🚪 Host cerrando lobby...");
            // El host cierra el lobby para todos
            if (lobbyManager != null)
            {
                lobbyManager.CloseLobby();
            }
        }
        else
        {
            Debug.Log("🚪 Cliente abandonando lobby...");
            // El cliente solo se va
            if (lobbyManager != null)
            {
                lobbyManager.LeaveLobby();
            }
        }

        // Limpiar datos del lobby
        PlayerPrefs.DeleteKey("LobbyCode");

        // Volver al menú principal
        UnityEngine.SceneManagement.SceneManager.LoadScene("Menu");
    }

    // Eventos del LobbyManager
    private void OnPlayersUpdated(List<Blanco.Networking.LobbyManager.PlayerInfo> players)
    {
        Debug.Log($"🔄 Lista de jugadores actualizada: {players.Count} jugadores");

        // Actualizar lista de jugadores específicamente
        UpdatePlayersList();

        // También actualizar el resto de la UI
        UpdateUI();
    }

    private void OnLobbyStateChanged(Blanco.Networking.LobbyManager.LobbyState newState)
    {
        Debug.Log($"🔄 Estado del lobby cambiado: {newState}");
        UpdateUI();
    }

    void Awake()
    {
        Instance = this;
        infoText.gameObject.SetActive(false);
    }

    [ClientRpc]
    public void SetInfoTextClientRpc(string text)
    {
        infoText.gameObject.SetActive(true);
        infoText.text = text;
    }

    [ClientRpc]
    public void HideInfoTextClientRpc()
    {
        infoText.gameObject.SetActive(false);
    }

    public void StartGameTimer(float duration)
    {
        gameTimer.SetVisibility(true);
        gameTimer.StartTimerServerRpc(duration);
    }
}
