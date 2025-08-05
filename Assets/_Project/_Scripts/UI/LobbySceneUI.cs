using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Unity.Netcode;

namespace Blanco.UI
{
    public class LobbySceneUI : MonoBehaviour
    {
        [Header("Lobby Panel")]
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private TextMeshProUGUI lobbyCodeText;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private TextMeshProUGUI playerCountText;
        
        [Header("Leave Lobby")]
        [SerializeField] private Button leaveLobbyButton;
        [SerializeField] private TextMeshProUGUI leaveButtonText;
        
        [Header("Players List")]
        [SerializeField] private Transform playersListContainer;
        [SerializeField] private GameObject playerItemPrefab;
        [SerializeField] private TextMeshProUGUI playersListText;
        
        [Header("Player Name Edit")]
        [SerializeField] private TMP_InputField playerNameInput;
        [SerializeField] private Button updateNameButton;
        [SerializeField] private TextMeshProUGUI currentPlayerNameText;
        
        [Header("Settings")]
        [SerializeField] private float updateInterval = 1f;
        
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
            lobbyManager.OnGameStarting += OnGameStarting;
            
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
                lobbyManager.OnGameStarting -= OnGameStarting;
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
            ClearPlayersList();
            
            // Obtener lista de jugadores
            var players = lobbyManager.GetPlayers();
            if (players == null || players.Count == 0)
            {
                if (playersListText != null)
                {
                    playersListText.text = "No hay jugadores conectados";
                }
                return;
            }
            
            // Crear elementos para cada jugador
            foreach (var player in players)
            {
                CreatePlayerItem(player);
            }
            
            // Actualizar texto de resumen
            if (playersListText != null)
            {
                playersListText.text = $"Jugadores ({players.Count}):";
            }
        }

        private void ClearPlayersList()
        {
            if (playersListContainer != null)
            {
                foreach (Transform child in playersListContainer)
                {
                    if (child != null)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
        }
        
        private void CreatePlayerItem(Blanco.Networking.LobbyManager.PlayerInfo player)
        {
            if (playersListContainer == null) return;
            
            GameObject playerItem;
            TextMeshProUGUI textComponent = null;
            
            // Usar prefab si está disponible, sino crear un texto simple
            if (playerItemPrefab != null)
            {
                playerItem = Instantiate(playerItemPrefab, playersListContainer);
                
                // Configurar usando el componente PlayerListItem si está disponible
                var playerListItem = playerItem.GetComponent<PlayerListItem>();
                if (playerListItem != null)
                {
                    playerListItem.SetPlayerInfo(player);
                    return; // Ya está configurado, salir
                }
                
                // Si no hay PlayerListItem, obtener el TextMeshProUGUI del prefab
                textComponent = playerItem.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                // Crear un GameObject simple con texto
                playerItem = new GameObject($"Player_{player.clientId}");
                playerItem.transform.SetParent(playersListContainer);
                
                // Añadir componente de texto
                textComponent = playerItem.AddComponent<TextMeshProUGUI>();
                textComponent.fontSize = 14;
                textComponent.color = Color.white;
                
                // Configurar layout
                var layoutElement = playerItem.AddComponent<UnityEngine.UI.LayoutElement>();
                layoutElement.minHeight = 20;
            }
            
            // Configurar texto del jugador (fallback si no hay PlayerListItem)
            if (textComponent != null)
            {
                string playerName = player.playerName.ToString();
                string hostIndicator = player.isHost ? " (Host)" : "";
                string status = player.isReady ? "✅" : "⏳";
                
                textComponent.text = $"{status} {playerName} (ID: {player.clientId}){hostIndicator}";
                
                // Color diferente para el host
                if (player.isHost)
                {
                    textComponent.color = Color.yellow;
                }
                else
                {
                    textComponent.color = Color.white;
                }
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
        
        private void OnGameStarting()
        {
            Debug.Log("🎮 El juego está iniciando...");
            // Aquí puedes agregar lógica adicional cuando el juego inicia
        }
        
        [ContextMenu("Debug UI Info")]
        public void DebugUIInfo()
        {
            Debug.Log("🔍 === INFO DE LOBBY UI ===");
            Debug.Log($"🔍 LobbyManager: {(lobbyManager != null ? "Encontrado" : "No encontrado")}");
            Debug.Log($"🔍 Jugadores: {lobbyManager?.GetPlayerCount() ?? 0}");
            Debug.Log($"🔍 IsHost: {(NetworkManager.Singleton != null ? NetworkManager.Singleton.IsHost : false)}");
            Debug.Log($"🔍 IsClient: {(NetworkManager.Singleton != null ? NetworkManager.Singleton.IsClient : false)}");
            Debug.Log($"🔍 Código: {PlayerPrefs.GetString("LobbyCode", "No disponible")}");
            Debug.Log("🔍 === FIN INFO ===");
        }
    }
} 