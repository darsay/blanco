using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
using Unity.Collections;

namespace Blanco.Networking
{
    public class LobbyManager : NetworkBehaviour
    {
        [Header("Lobby Settings")]
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private string sceneToLoad = "Lobby";
        
        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = true;
        
        // Lobby actual
        public static Lobby CurrentLobby;
        private static UnityTransport _transport;
        
        // NetworkVariables para sincronizar datos del lobby
        private NetworkVariable<LobbyState> lobbyState = new NetworkVariable<LobbyState>(LobbyState.Waiting);
        private NetworkList<PlayerInfo> players = new NetworkList<PlayerInfo>();
        
        // Eventos para la UI
        public static event Action OnLobbyLeft;
        public event Action<LobbyState> OnLobbyStateChanged;
        public event Action OnGameStarting;
        public event Action<List<PlayerInfo>> OnPlayersUpdated;
        
        public enum LobbyState
        {
            Waiting,
            Starting,
            InGame
        }
        
        [System.Serializable]
        public struct PlayerInfo : INetworkSerializable, IEquatable<PlayerInfo>
        {
            public ulong clientId;
            public FixedString32Bytes playerName;
            public bool isReady;
            public bool isHost;
            
            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref clientId);
                serializer.SerializeValue(ref playerName);
                serializer.SerializeValue(ref isReady);
                serializer.SerializeValue(ref isHost);
            }
            
            public bool Equals(PlayerInfo other)
            {
                return clientId == other.clientId &&
                       playerName.Equals(other.playerName) &&
                       isReady == other.isReady &&
                       isHost == other.isHost;
            }
            
            public override bool Equals(object obj)
            {
                return obj is PlayerInfo other && Equals(other);
            }
            
            public override int GetHashCode()
            {
                return HashCode.Combine(clientId, playerName, isReady, isHost);
            }
        }
        
        private static UnityTransport Transport
        {
            get => _transport != null ? _transport : _transport = FindObjectOfType<UnityTransport>();
            set => _transport = value;
        }
        
        private void Awake()
        {
            DontDestroyOnLoad(this);
        }
        
        private void Start()
        {
            // Suscribirse a eventos de red
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            Debug.Log("🔧 LobbyManager NetworkSpawn");
            
            if (NetworkManager.Singleton.IsServer)
            {
                lobbyState.Value = LobbyState.Waiting;
                Debug.Log("🟢 Host iniciado - Lobby en espera");
            }
            else
            {
                Debug.Log("🔵 Cliente conectado al lobby");
            }
        }
        
        public override void OnDestroy()
        {
            base.OnDestroy();
            
            // Desuscribirse de eventos
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }
        
        #region Host - Crear Lobby
        
        public async Task<bool> CreateLobby(string lobbyName)
        {
            try
            {
                if (showDebugLogs)
                    Debug.Log($"🔧 Creando lobby: {lobbyName}");
                
                // Autenticarse si no está autenticado
                if (!Authentication.IsSignedIn())
                {
                    bool authSuccess = await Authentication.Login();
                    if (!authSuccess)
                    {
                        Debug.LogError("❌ No se pudo autenticar para crear lobby");
                        return false;
                    }
                }
                
                // Crear relay allocation
                string joinCode = await CreateRelayAllocation();
                if (string.IsNullOrEmpty(joinCode))
                {
                    Debug.LogError("❌ No se pudo crear relay allocation");
                    return false;
                }
                
                // Crear lobby
                CurrentLobby = await CreateLobbyWithRelay(lobbyName, joinCode);
                if (CurrentLobby == null)
                {
                    Debug.LogError("❌ No se pudo crear lobby");
                    return false;
                }
                
                // Guardar código del lobby
                PlayerPrefs.SetString("LobbyCode", CurrentLobby.LobbyCode);
                PlayerPrefs.Save();
                
                if (showDebugLogs)
                    Debug.Log($"✅ Lobby creado exitosamente: {CurrentLobby.LobbyCode}");
                
                // Iniciar host
                NetworkManager.Singleton.StartHost();
                
                // El host ya está en la escena del lobby, no necesita cambiar
                Debug.Log("✅ Host iniciado en la escena actual");
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error al crear lobby: {e.Message}");
                return false;
            }
        }
        
        private async Task<string> CreateRelayAllocation()
        {
            try
            {
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
                Transport.SetHostRelayData(
                    allocation.RelayServer.IpV4, 
                    (ushort)allocation.RelayServer.Port, 
                    allocation.AllocationIdBytes, 
                    allocation.Key, 
                    allocation.ConnectionData
                );
                
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                
                if (showDebugLogs)
                    Debug.Log($"✅ Relay allocation creado: {joinCode}");
                
                return joinCode;
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error al crear relay allocation: {e.Message}");
                return null;
            }
        }
        
        private async Task<Lobby> CreateLobbyWithRelay(string lobbyName, string joinCode)
        {
            try
            {
                CreateLobbyOptions lobbyOptions = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        { "JoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
                    },
                    Player = new Player(id: Authentication.GetPlayerId())
                };
                
                Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, lobbyOptions);
                
                if (showDebugLogs)
                    Debug.Log($"✅ Lobby creado: {lobby.Name} - Código: {lobby.LobbyCode}");
                
                return lobby;
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error al crear lobby: {e.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region Client - Unirse a Lobby
        
        public async Task<bool> JoinLobby(string joinCode)
        {
            try
            {
                if (showDebugLogs)
                    Debug.Log($"🔧 Uniéndose a lobby: {joinCode}");
                
                // Autenticarse si no está autenticado
                if (!Authentication.IsSignedIn())
                {
                    bool authSuccess = await Authentication.Login();
                    if (!authSuccess)
                    {
                        Debug.LogError("❌ No se pudo autenticar para unirse al lobby");
                        return false;
                    }
                }
                
                // Unirse al lobby
                CurrentLobby = await JoinLobbyByCode(joinCode);
                if (CurrentLobby == null)
                {
                    Debug.LogError("❌ No se pudo unirse al lobby");
                    return false;
                }
                
                // Unirse al relay
                bool relaySuccess = await JoinRelayAllocation(CurrentLobby);
                if (!relaySuccess)
                {
                    Debug.LogError("❌ No se pudo unirse al relay");
                    return false;
                }
                
                // Guardar información del lobby
                PlayerPrefs.SetString("LobbyCode", CurrentLobby.LobbyCode);
                PlayerPrefs.SetString("LobbyId", CurrentLobby.Id);
                PlayerPrefs.Save();
                
                if (showDebugLogs)
                    Debug.Log($"✅ Unido exitosamente al lobby: {CurrentLobby.LobbyCode}");
                
                // Conectar como cliente
                NetworkManager.Singleton.StartClient();
                
                // El cliente se unirá a la escena del host automáticamente
                Debug.Log("✅ Cliente conectado, uniéndose a la escena del host");
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error al unirse al lobby: {e.Message}");
                return false;
            }
        }
        
        private async Task<Lobby> JoinLobbyByCode(string joinCode)
        {
            try
            {
                JoinLobbyByCodeOptions options = new JoinLobbyByCodeOptions
                {
                    Player = new Player(id: Authentication.GetPlayerId())
                };
                
                Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode, options);
                
                if (showDebugLogs)
                    Debug.Log($"✅ Unido al lobby: {lobby.Name}");
                
                return lobby;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"❌ Error al unirse al lobby: {e.Message}");
                return null;
            }
        }
        
        private async Task<bool> JoinRelayAllocation(Lobby lobby)
        {
            try
            {
                string joinCode = lobby.Data["JoinCode"].Value;
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                
                Transport.SetClientRelayData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData,
                    allocation.HostConnectionData
                );
                
                if (showDebugLogs)
                    Debug.Log("✅ Relay allocation unido exitosamente");
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error al unirse al relay: {e.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Network Events
        
        private void OnClientConnected(ulong clientId)
        {
            if (showDebugLogs)
                Debug.Log($"🟢 Cliente conectado: {clientId}");
            
            // Agregar jugador a la lista si es servidor
            if (NetworkManager.Singleton.IsServer)
            {
                AddPlayer(clientId, $"Player_{clientId}", clientId == NetworkManager.Singleton.LocalClientId);
            }
        }
        
        private void OnClientDisconnected(ulong clientId)
        {
            if (showDebugLogs)
                Debug.Log($"🔴 Cliente desconectado: {clientId}");
            
            // Remover jugador de la lista si es servidor
            if (NetworkManager.Singleton.IsServer)
            {
                RemovePlayer(clientId);
            }
        }
        
        #endregion
        
        #region Player Management
        
        private void AddPlayer(ulong clientId, string playerName, bool isHost)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            
            var playerInfo = new PlayerInfo
            {
                clientId = clientId,
                playerName = new FixedString32Bytes(playerName),
                isReady = true,
                isHost = isHost
            };
            
            players.Add(playerInfo);
            
            if (showDebugLogs)
                Debug.Log($"➕ Jugador agregado: {playerName} (ID: {clientId})");
            
            // Notificar cambio en la lista de jugadores
            NotifyPlayersUpdated();
        }
        
        private void RemovePlayer(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].clientId == clientId)
                {
                    string playerName = players[i].playerName.ToString();
                    players.RemoveAt(i);
                    
                    if (showDebugLogs)
                        Debug.Log($"➖ Jugador removido: {playerName} (ID: {clientId})");
                    
                    // Notificar cambio en la lista de jugadores
                    NotifyPlayersUpdated();
                    break;
                }
            }
        }
        
        #endregion
        
        #region Public Methods
        
        public void LeaveLobby()
        {
            if (CurrentLobby != null)
            {
                // Limpiar PlayerPrefs
                PlayerPrefs.DeleteKey("LobbyCode");
                PlayerPrefs.DeleteKey("LobbyId");
                PlayerPrefs.Save();
                
                // Desconectar
                if (NetworkManager.Singleton != null)
                {
                    NetworkManager.Singleton.Shutdown();
                }
                
                // Notificar evento
                OnLobbyLeft?.Invoke();
                
                if (showDebugLogs)
                    Debug.Log("🚪 Lobby abandonado");
            }
        }
        
        public List<PlayerInfo> GetPlayers()
        {
            var playerList = new List<PlayerInfo>();
            foreach (var player in players)
            {
                playerList.Add(player);
            }
            return playerList;
        }
        
        public int GetPlayerCount()
        {
            return players.Count;
        }
        
        public int GetMaxPlayers()
        {
            return maxPlayers;
        }
        
        public LobbyState GetLobbyState()
        {
            return lobbyState.Value;
        }
        
        public void SetLobbyState(LobbyState newState)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                lobbyState.Value = newState;
                OnLobbyStateChanged?.Invoke(newState);
            }
        }
        
        public bool IsHostPlayer()
        {
            return NetworkManager.Singleton.IsHost;
        }
        
        private void NotifyPlayersUpdated()
        {
            var playerList = new List<PlayerInfo>();
            foreach (var player in players)
            {
                playerList.Add(player);
            }
            OnPlayersUpdated?.Invoke(playerList);
        }
        
        #endregion
        
        #region Game Methods
        
        [ServerRpc(RequireOwnership = false)]
        public void StartGameServerRpc()
        {
            if (!NetworkManager.Singleton.IsServer) return;
            
            Debug.Log("🎮 Iniciando juego...");
            lobbyState.Value = LobbyState.Starting;
            OnGameStarting?.Invoke();
        }
        
        [ClientRpc]
        private void StartGameClientRpc()
        {
            Debug.Log("🎮 Cambiando a escena de gameplay...");
            UnityEngine.SceneManagement.SceneManager.LoadScene("Gameplay");
        }
        
        public void StartGame()
        {
            if (NetworkManager.Singleton.IsHost && lobbyState.Value == LobbyState.Waiting)
            {
                StartGameServerRpc();
            }
        }
        
        #endregion
        
        #region Debug Methods
        
        [ContextMenu("Debug Lobby Info")]
        public void DebugLobbyInfo()
        {
            Debug.Log("🔍 === INFO DEL LOBBY ===");
            Debug.Log($"🔍 Lobby actual: {(CurrentLobby != null ? CurrentLobby.Name : "Ninguno")}");
            Debug.Log($"🔍 Código: {(CurrentLobby != null ? CurrentLobby.LobbyCode : "Ninguno")}");
            Debug.Log($"🔍 Jugadores: {players.Count}/{maxPlayers}");
            Debug.Log($"🔍 Estado: {lobbyState.Value}");
            Debug.Log($"🔍 IsServer: {NetworkManager.Singleton.IsServer}");
            Debug.Log($"🔍 IsClient: {NetworkManager.Singleton.IsClient}");
            Debug.Log("🔍 === FIN INFO ===");
        }
        
        #endregion
    }
} 