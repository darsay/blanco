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
        
        // Sistema de heartbeat para detectar desconexiones
        private float lastHeartbeatTime = 0f;
        private float heartbeatInterval = 5f; // Verificar cada 5 segundos
        private float hostTimeout = 10f; // Host ausente por más de 10 segundos = desconectado
        
        // NetworkVariables para sincronizar datos del lobby
        private NetworkVariable<LobbyState> lobbyState = new NetworkVariable<LobbyState>(LobbyState.Waiting);
        private NetworkList<PlayerInfo> players = new NetworkList<PlayerInfo>();
        
        // Diccionario para mapear clientIds a nombres de jugadores
        private Dictionary<ulong, string> clientIdToPlayerName = new Dictionary<ulong, string>();
        
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
                
                // Iniciar heartbeat del host
                StartCoroutine(HostHeartbeat());
            }
        }
        
        private System.Collections.IEnumerator HostHeartbeat()
        {
            while (CurrentLobby != null && NetworkManager.Singleton.IsServer)
            {
                try
                {
                    // Actualizar heartbeat del host
                    var updateOptions = new UpdateLobbyOptions
                    {
                        Data = new Dictionary<string, DataObject>
                        {
                            { "LastHeartbeat", new DataObject(DataObject.VisibilityOptions.Public, DateTime.UtcNow.ToString()) }
                        }
                    };
                    
                    // Usar Task.Run para evitar await en corrutina
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, updateOptions);
                            
                            if (showDebugLogs)
                                Debug.Log("💓 Heartbeat del host actualizado");
                        }
                        catch (Exception e)
                        {
                            if (showDebugLogs)
                                Debug.LogWarning($"⚠️ Error al actualizar heartbeat del host: {e.Message}");
                        }
                    });
                }
                catch (Exception e)
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ Error al preparar heartbeat del host: {e.Message}");
                }
                
                yield return new WaitForSeconds(heartbeatInterval);
            }
        }
        
        private void Update()
        {
            // Sistema de heartbeat para detectar desconexiones del host
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && CurrentLobby != null)
            {
                CheckHostConnection();
            }
        }
        
        private async void CheckHostConnection()
        {
            if (Time.time - lastHeartbeatTime > heartbeatInterval)
            {
                lastHeartbeatTime = Time.time;
                
                try
                {
                    // Verificar si el lobby sigue activo
                    var updatedLobby = await LobbyService.Instance.GetLobbyAsync(CurrentLobby.Id);
                    
                    if (updatedLobby == null)
                    {
                        if (showDebugLogs)
                            Debug.Log("🚪 Lobby ya no existe - Host probablemente desconectado");
                        
                        HandleHostDisconnection();
                        return;
                    }
                    
                    // Verificar si el host sigue en el lobby
                    bool hostFound = false;
                    string lastHeartbeat = "";
                    
                    foreach (var player in updatedLobby.Players)
                    {
                        if (player.Data != null && player.Data.ContainsKey("IsHost"))
                        {
                            if (player.Data["IsHost"].Value == "true")
                            {
                                hostFound = true;
                                break;
                            }
                        }
                    }
                    
                    // Verificar heartbeat del host
                    if (updatedLobby.Data != null && updatedLobby.Data.ContainsKey("LastHeartbeat"))
                    {
                        lastHeartbeat = updatedLobby.Data["LastHeartbeat"].Value;
                        
                        if (DateTime.TryParse(lastHeartbeat, out DateTime heartbeatTime))
                        {
                            var timeSinceHeartbeat = DateTime.UtcNow - heartbeatTime;
                            
                            if (timeSinceHeartbeat.TotalSeconds > hostTimeout)
                            {
                                if (showDebugLogs)
                                    Debug.Log($"🚪 Host inactivo por {timeSinceHeartbeat.TotalSeconds:F1} segundos - Desconectado");
                                
                                HandleHostDisconnection();
                                return;
                            }
                        }
                    }
                    
                    if (!hostFound)
                    {
                        if (showDebugLogs)
                            Debug.Log("🚪 Host no encontrado en el lobby - Desconectado");
                        
                        HandleHostDisconnection();
                    }
                    else if (showDebugLogs)
                    {
                        Debug.Log($"💓 Host activo - Último heartbeat: {lastHeartbeat}");
                    }
                }
                catch (Exception e)
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ Error al verificar estado del lobby: {e.Message}");
                    
                    // Si no podemos contactar el lobby, asumir que el host se desconectó
                    HandleHostDisconnection();
                }
            }
        }
        
        private void HandleHostDisconnection()
        {
            if (showDebugLogs)
                Debug.Log("🚪 Host desconectado - Saliendo del lobby");
            
            // Salir del canal de voz de Vivox
            _ = LeaveLobbyVoiceChannel();
            
            // Limpiar estado del lobby
            CleanupLobbyState();
            
            // Desconectar cliente
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
            
            // Disparar evento
            OnLobbyLeft?.Invoke();
            
            // Volver al menú principal
            StartCoroutine(LoadMenuAfterDelay());
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
                // Obtener el nombre del jugador guardado
                string playerName = PlayerPrefs.GetString("PlayerName", "");
                if (string.IsNullOrEmpty(playerName))
                {
                    playerName = $"Player_{AuthenticationService.Instance.PlayerId}";
                }
                
                CreateLobbyOptions lobbyOptions = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        { "JoinCode", new DataObject(DataObject.VisibilityOptions.Public, joinCode) },
                        { "HostId", new DataObject(DataObject.VisibilityOptions.Public, AuthenticationService.Instance.PlayerId) }
                    },
                    Player = new Player
                    {
                        Data = new Dictionary<string, PlayerDataObject>
                        {
                            { "IsHost", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "true") },
                            { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName) }
                        }
                    }
                };
                
                Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, lobbyOptions);
                
                if (showDebugLogs)
                    Debug.Log($"✅ Lobby creado: {lobby.Name} (ID: {lobby.Id})");
                
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
                if (showDebugLogs)
                    Debug.Log($"🔍 Buscando lobby con código: {joinCode}");
                
                // Verificar si ya estamos en un lobby
                if (CurrentLobby != null)
                {
                    if (showDebugLogs)
                        Debug.Log("⚠️ Ya estás en un lobby, saliendo primero...");
                    
                    // Salir del lobby actual
                    try
                    {
                        await LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, AuthenticationService.Instance.PlayerId);
                        CurrentLobby = null;
                    }
                    catch (Exception e)
                    {
                        if (showDebugLogs)
                            Debug.LogWarning($"⚠️ Error al salir del lobby anterior: {e.Message}");
                    }
                }
                
                // Obtener el nombre del jugador guardado
                string playerName = PlayerPrefs.GetString("PlayerName", "");
                if (string.IsNullOrEmpty(playerName))
                {
                    playerName = $"Player_{AuthenticationService.Instance.PlayerId}";
                }
                
                // Configurar opciones de unión con el nombre del jugador
                JoinLobbyByCodeOptions options = new JoinLobbyByCodeOptions
                {
                    Player = new Player
                    {
                        Data = new Dictionary<string, PlayerDataObject>
                        {
                            { "IsHost", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "false") },
                            { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName) }
                        }
                    }
                };
                
                // Unirse al nuevo lobby
                var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode, options);
                
                if (showDebugLogs)
                    Debug.Log($"✅ Lobby encontrado: {lobby.Name} (ID: {lobby.Id})");
                
                return lobby;
            }
            catch (Exception e)
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
                Debug.Log($"🔗 Cliente conectado: {clientId}");
            
            // Solo el servidor debe sincronizar los jugadores desde Unity Services
            if (NetworkManager.Singleton.IsServer)
            {
                if (showDebugLogs)
                    Debug.Log($"🔍 Servidor sincronizando jugadores desde Unity Services");
                
                await SyncPlayersFromUnityServices();
            }
            
            // Unirse al canal de voz solo si es el cliente local
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                await JoinLobbyVoiceChannel();
            }
        }
        
        private async void OnClientDisconnected(ulong clientId)
        {
            if (showDebugLogs)
                Debug.Log($"🔴 Cliente desconectado: {clientId}");
            
            // Remover jugador del diccionario
            if (clientIdToPlayerName.ContainsKey(clientId))
            {
                clientIdToPlayerName.Remove(clientId);
            }
            
            // Remover jugador de la lista si es servidor
            if (NetworkManager.Singleton.IsServer)
            {
                RemovePlayer(clientId);
            }
            
            // Salir del canal de voz si es el jugador local
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                await LeaveLobbyVoiceChannel();
                Debug.Log("🚪 Has salido del lobby");
            }
            else
            {
                Debug.Log($"🚪 Jugador {clientId} ha salido del lobby");
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
        
        private void CleanupLobbyState()
        {
            // Limpiar PlayerPrefs
            PlayerPrefs.DeleteKey("LobbyCode");
            PlayerPrefs.DeleteKey("LobbyId");
            PlayerPrefs.Save();
            
            // Limpiar lobby actual
            CurrentLobby = null;
            
            // Limpiar diccionario de nombres
            clientIdToPlayerName.Clear();
            
            // Solo limpiar la lista de jugadores si es el servidor
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && players != null)
            {
                players.Clear();
            }
            
            if (showDebugLogs)
                Debug.Log("🧹 Estado del lobby limpiado");
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
            
            if (showDebugLogs)
                Debug.Log($"🔧 AddPlayer llamado - ClientID: {clientId}, Nombre: '{playerName}', EsHost: {isHost}");
            
            var playerInfo = new PlayerInfo
            {
                clientId = clientId,
                playerName = new FixedString32Bytes(playerName),
                isReady = true,
                isHost = isHost
            };
            
            players.Add(playerInfo);
            
            if (showDebugLogs)
                Debug.Log($"➕ Jugador agregado: {playerName} (ID: {clientId}, Host: {isHost})");
            
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
        
        private async Task SyncPlayersFromUnityServices()
        {
            if (!NetworkManager.Singleton.IsServer || CurrentLobby == null)
                return;
            
            try
            {
                // Obtener la información actualizada del lobby
                var updatedLobby = await LobbyService.Instance.GetLobbyAsync(CurrentLobby.Id);
                if (updatedLobby == null)
                    return;
                
                if (showDebugLogs)
                    Debug.Log($"🔄 Sincronizando {updatedLobby.Players.Count} jugadores desde Unity Services");
                
                // Limpiar la lista actual
                players.Clear();
                
                // Agregar cada jugador desde Unity Services
                ulong clientIdCounter = 0;
                foreach (var player in updatedLobby.Players)
                {
                    string playerName = $"Player_{clientIdCounter}";
                    bool isHost = false;
                    
                    if (player.Data != null)
                    {
                        if (player.Data.ContainsKey("PlayerName"))
                        {
                            playerName = player.Data["PlayerName"].Value;
                        }
                        
                        if (player.Data.ContainsKey("IsHost"))
                        {
                            isHost = player.Data["IsHost"].Value == "true";
                        }
                    }
                    
                    // Crear PlayerInfo
                    var playerInfo = new PlayerInfo
                    {
                        clientId = clientIdCounter,
                        playerName = new FixedString32Bytes(playerName),
                        isReady = true,
                        isHost = isHost
                    };
                    
                    players.Add(playerInfo);
                    
                    if (showDebugLogs)
                        Debug.Log($"➕ Jugador sincronizado: {playerName} (ID: {clientIdCounter}, Host: {isHost})");
                    
                    clientIdCounter++;
                }
                
                // Notificar cambio en la lista de jugadores
                NotifyPlayersUpdated();
                
                // Actualizar el lobby actual
                CurrentLobby = updatedLobby;
            }
            catch (Exception e)
            {
                if (showDebugLogs)
                    Debug.LogError($"❌ Error al sincronizar jugadores: {e.Message}");
            }
        }
        
        #endregion
        
        #region Public Methods
        
        public async void LeaveLobby()
        {
            if (CurrentLobby != null)
            {
                try
                {
                    // Salir del lobby de Unity Services
                    await LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, AuthenticationService.Instance.PlayerId);
                    
                    if (showDebugLogs)
                        Debug.Log("🚪 Jugador removido del lobby de Unity Services");
                }
                catch (Exception e)
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ Error al salir del lobby de Unity Services: {e.Message}");
                }
                
                // Salir del canal de voz de Vivox
                await LeaveLobbyVoiceChannel();
                
                // Limpiar estado del lobby
                CleanupLobbyState();
                
                // Desconectar
                if (NetworkManager.Singleton != null)
                {
                    NetworkManager.Singleton.Shutdown();
                }
                
                if (showDebugLogs)
                    Debug.Log("🚪 Lobby abandonado voluntariamente");
                
                // Disparar evento
                OnLobbyLeft?.Invoke();
                
                // Forzar la carga de la escena del menú después de un breve delay
                StartCoroutine(LoadMenuAfterDelay());
            }
        }
        
        public async void CloseLobby()
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("⚠️ Solo el host puede cerrar el lobby");
                return;
            }
            
            if (showDebugLogs)
                Debug.Log("🚪 Host cerrando lobby para todos...");
            
            // Notificar a todos los clientes que el lobby se está cerrando
            CloseLobbyClientRpc();
            
            // Esperar un momento para que los clientes reciban el mensaje
            await Task.Delay(1000);
            
            try
            {
                // Eliminar el lobby de Unity Services
                if (CurrentLobby != null)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(CurrentLobby.Id);
                    
                    if (showDebugLogs)
                        Debug.Log("🚪 Lobby eliminado de Unity Services");
                }
            }
            catch (Exception e)
            {
                if (showDebugLogs)
                    Debug.LogWarning($"⚠️ Error al eliminar lobby de Unity Services: {e.Message}");
            }
            
            // Salir del canal de voz de Vivox
            await LeaveLobbyVoiceChannel();
            
            // Limpiar estado del lobby
            CleanupLobbyState();
            
            // Desconectar servidor
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
            
            if (showDebugLogs)
                Debug.Log("🚪 Lobby cerrado por el host");
            
            // Disparar evento
            OnLobbyLeft?.Invoke();
            
            // Forzar la carga de la escena del menú después de un breve delay
            StartCoroutine(LoadMenuAfterDelay());
        }
        
        [ClientRpc]
        private void CloseLobbyClientRpc()
        {
            if (showDebugLogs)
                Debug.Log("🚪 El host ha cerrado el lobby - Desconectando...");
            
            // Salir del canal de voz de Vivox
            _ = LeaveLobbyVoiceChannel();
            
            // Limpiar estado del lobby (sin tocar NetworkVariables)
            CleanupLobbyState();
            
            // Desconectar cliente inmediatamente
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
            
            // Disparar evento
            OnLobbyLeft?.Invoke();
            
            // Forzar la carga de la escena del menú después de un breve delay
            StartCoroutine(LoadMenuAfterDelay());
        }
        
        private System.Collections.IEnumerator LoadMenuAfterDelay()
        {
            yield return new WaitForSeconds(0.5f);
            UnityEngine.SceneManagement.SceneManager.LoadScene("Menu");
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
            
            if (showDebugLogs)
                Debug.Log($"📢 NotifyPlayersUpdated - {playerList.Count} jugadores en la lista");
            
            OnPlayersUpdated?.Invoke(playerList);
        }
        
        #endregion
    }
} 