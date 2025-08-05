using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Vivox;
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
        
        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = true;
        
        // Singleton
        public static LobbyManager Instance { get; private set; }
        
        public static Lobby CurrentLobby;
        private static UnityTransport _transport;

        // NetworkVariables para sincronizar datos del lobby
        private NetworkVariable<LobbyState> lobbyState = new NetworkVariable<LobbyState>(LobbyState.Waiting);
        private NetworkList<PlayerInfo> players = new NetworkList<PlayerInfo>();
        
        // Eventos del lobby
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
            get
            {
                if (_transport == null)
                    _transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                return _transport;
            }
        }
        
        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Configurar eventos de red
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }
        
        private void Start()
        {
            // Configurar eventos de Vivox
            if (VivoxService.Instance != null)
            {
                VivoxService.Instance.LoggedIn += OnVivoxLoggedIn;
                VivoxService.Instance.LoggedOut += OnVivoxLoggedOut;
                VivoxService.Instance.ChannelJoined += OnVivoxChannelJoined;
                VivoxService.Instance.ChannelLeft += OnVivoxChannelLeft;
                VivoxService.Instance.ParticipantAddedToChannel += OnVivoxParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel += OnVivoxParticipantRemoved;
            }
        }
        
        public override void OnNetworkSpawn()
        {
            if (showDebugLogs)
                Debug.Log("🌐 LobbyManager iniciado en red");
            
            // Configurar el estado inicial del lobby
            if (NetworkManager.Singleton.IsServer)
            {
                lobbyState.Value = LobbyState.Waiting;
            }
        }
        
        public override void OnDestroy()
        {
            // Limpiar eventos de red
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            
            // Limpiar eventos de Vivox
            if (VivoxService.Instance != null)
            {
                VivoxService.Instance.LoggedIn -= OnVivoxLoggedIn;
                VivoxService.Instance.LoggedOut -= OnVivoxLoggedOut;
                VivoxService.Instance.ChannelJoined -= OnVivoxChannelJoined;
                VivoxService.Instance.ChannelLeft -= OnVivoxChannelLeft;
                VivoxService.Instance.ParticipantAddedToChannel -= OnVivoxParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel -= OnVivoxParticipantRemoved;
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

        #region Vivox

        private async void OnClientConnected(ulong clientId)
        {
            if (showDebugLogs)
                Debug.Log($"🟢 Cliente conectado: {clientId}");
            
            // Agregar jugador a la lista si es servidor
            if (NetworkManager.Singleton.IsServer)
            {
                AddPlayer(clientId, $"Player_{clientId}", clientId == NetworkManager.Singleton.LocalClientId);
            }
            
            // Unirse al canal de voz SOLO si es el jugador local
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                await JoinLobbyVoiceChannel();
            }
        }
        
        private async void OnClientDisconnected(ulong clientId)
        {
            if (showDebugLogs)
                Debug.Log($"🔴 Cliente desconectado: {clientId}");
            
            // Remover jugador de la lista si es servidor
            if (NetworkManager.Singleton.IsServer)
            {
                RemovePlayer(clientId);
            }
            
            // Salir del canal de voz si es el jugador local
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                await LeaveLobbyVoiceChannel();
            }
        }
        
        private async Task JoinLobbyVoiceChannel()
        {
            try
            {
                if (showDebugLogs)
                    Debug.Log("🎤 Uniéndose al canal de voz del lobby...");
                
                // Obtener el código del lobby
                string lobbyCode = GetCurrentLobbyCode();
                if (string.IsNullOrEmpty(lobbyCode))
                {
                    Debug.LogWarning("⚠️ No hay lobby activo, usando canal por defecto");
                    lobbyCode = "DefaultLobby";
                }
                
                // Hacer login a Vivox si no está logueado
                if (!VivoxService.Instance.IsLoggedIn)
                {
                    if (showDebugLogs)
                        Debug.Log("🎤 Haciendo login a Vivox...");
                    
                    var loginOptions = new LoginOptions();
                    loginOptions.DisplayName = $"Player_{AuthenticationService.Instance.PlayerId}";
                    await VivoxService.Instance.LoginAsync(loginOptions);
                }
                
                // Unirse al canal usando las APIs v16
                await VivoxService.Instance.JoinGroupChannelAsync(lobbyCode, ChatCapability.AudioOnly);
                
                if (showDebugLogs)
                    Debug.Log($"✅ Conectado al canal de voz: {lobbyCode}");
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error al unirse al canal de voz: {e.Message}");
            }
        }
        
        private async Task LeaveLobbyVoiceChannel()
        {
            try
            {
                if (showDebugLogs)
                    Debug.Log("🎤 Saliendo del canal de voz...");
                
                await VivoxService.Instance.LeaveAllChannelsAsync();
                
                if (showDebugLogs)
                    Debug.Log("✅ Desconectado del canal de voz");
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error al salir del canal de voz: {e.Message}");
            }
        }
        
        private string GetCurrentLobbyCode()
        {
            string lobbyCode = PlayerPrefs.GetString("LobbyCode", "");
            
            if (string.IsNullOrEmpty(lobbyCode) && CurrentLobby != null)
            {
                lobbyCode = CurrentLobby.LobbyCode;
            }
            
            return lobbyCode;
        }
        
        // Eventos de Vivox
        private void OnVivoxLoggedIn()
        {
            if (showDebugLogs)
                Debug.Log("🎤 Usuario logueado en Vivox");
        }
        
        private void OnVivoxLoggedOut()
        {
            if (showDebugLogs)
                Debug.Log("🎤 Usuario deslogueado de Vivox");
        }
        
        private void OnVivoxChannelJoined(string channelName)
        {
            if (showDebugLogs)
                Debug.Log($"🎤 Canal unido: {channelName}");
        }
        
        private void OnVivoxChannelLeft(string channelName)
        {
            if (showDebugLogs)
                Debug.Log($"🎤 Canal abandonado: {channelName}");
        }
        
        private void OnVivoxParticipantAdded(VivoxParticipant participant)
        {
            if (showDebugLogs)
                Debug.Log($"🎤 Participante agregado: {participant.DisplayName}");
        }
        
        private void OnVivoxParticipantRemoved(VivoxParticipant participant)
        {
            if (showDebugLogs)
                Debug.Log($"🎤 Participante removido: {participant.DisplayName}");
        }

        #endregion

        #region Network Events

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
        
        public async void LeaveLobby()
        {
            if (CurrentLobby != null)
            {
                // Salir del canal de voz de Vivox
                await LeaveLobbyVoiceChannel();
                
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
            Debug.Log("🎮 Juego iniciado");
        }
        
        public void StartGame()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                StartGameServerRpc();
            }
        }
        
        #endregion
        
        [ContextMenu("Debug Lobby Info")]
        public void DebugLobbyInfo()
        {
            string info = "🎮 === LOBBY INFO ===\n";
            info += $"Estado: {lobbyState.Value}\n";
            info += $"Jugadores: {players.Count}/{maxPlayers}\n";
            info += $"Es Host: {IsHostPlayer()}\n";
            info += $"Vivox Logueado: {VivoxService.Instance.IsLoggedIn}\n";
            info += $"Canales Activos: {VivoxService.Instance.ActiveChannels.Count}\n";
            info += "🎮 === FIN INFO ===";
            Debug.Log(info);
        }
    }
} 