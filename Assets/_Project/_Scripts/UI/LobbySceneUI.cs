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
        
        // Métodos para iniciar juego y salir del lobby (puedes agregarlos después)
        private void OnStartGameClicked()
        {
            Debug.Log("🎮 Iniciando juego...");
            
            if (lobbyManager != null)
            {
                lobbyManager.StartGame();
            }
        }
        
        private void OnLeaveLobbyClicked()
        {
            Debug.Log("🚪 Abandonando lobby...");
            
            // Abandonar lobby
            if (lobbyManager != null)
            {
                lobbyManager.LeaveLobby();
            }
            
            // Volver al menú principal
            UnityEngine.SceneManagement.SceneManager.LoadScene("Menu");
        }
        
        // Eventos del LobbyManager
        private void OnPlayersUpdated(List<Blanco.Networking.LobbyManager.PlayerInfo> players)
        {
            Debug.Log($"🔄 Lista de jugadores actualizada: {players.Count} jugadores");
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