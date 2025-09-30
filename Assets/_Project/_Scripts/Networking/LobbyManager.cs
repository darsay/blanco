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
using static Blanco.Networking.LobbyManager;
using MoreMountains.Tools;

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
        private float heartbeatInterval = 10f; // Verificar cada 5 segundos
        private float hostTimeout = 20f; // Host ausente por más de 10 segundos = desconectado
        
        // NetworkVariables para sincronizar datos del lobby
        private NetworkVariable<LobbyState> lobbyState = new NetworkVariable<LobbyState>(LobbyState.Waiting);
        private NetworkList<PlayerInfo> players = new NetworkList<PlayerInfo>();
        
        // Diccionario para mapear clientIds a nombres de jugadores
        // Dictionary removed - using Unity Services data instead
        
        // Eventos del lobby
        public static event Action OnLobbyLeft;
        public event Action<LobbyState> OnLobbyStateChanged;
        public event Action OnGameStarting;
        public event Action<List<PlayerInfo>> OnPlayersUpdated;
        public event Action<PlayerInfo> OnPlayerJoin;
        public event Action<PlayerInfo> OnPlayerLeave;

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
            if (NetworkManager.Singleton.IsHost)
            {
                lobbyState.Value = LobbyState.Waiting;
                
                // Heartbeat del host DESHABILITADO temporalmente para evitar rate limit
                StartCoroutine(HostHeartbeat());
            }
        }
        
        private System.Collections.IEnumerator HostHeartbeat()
        {
            while (CurrentLobby != null && NetworkManager.Singleton.IsHost)
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
                
                // Verificar si ya estamos en un lobby y salir completamente
                if (CurrentLobby != null)
                {
                    if (showDebugLogs)
                        Debug.Log("⚠️ Ya estás en un lobby, saliendo primero...");
                    
                    try
                    {
                        await LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, AuthenticationService.Instance.PlayerId);
                        await Task.Delay(500); // Esperar un poco para que se procese
                        CurrentLobby = null;
                        if (showDebugLogs)
                            Debug.Log("✅ Salida del lobby anterior completada");
                    }
                    catch (Exception e)
                    {
                        if (showDebugLogs)
                            Debug.LogWarning($"⚠️ Error al salir del lobby anterior: {e.Message}");
                        // Continuar de todos modos
                    }
                }
                
                // Obtener el nombre del jugador guardado
                string playerName = PlayerPrefs.GetString("PlayerName", "");
                if (string.IsNullOrEmpty(playerName))
                {
                    playerName = $"Player_{AuthenticationService.Instance.PlayerId.Substring(0, 8)}";
                }
                
                if (showDebugLogs)
                    Debug.Log($"🎯 Intentando unirse con nombre: '{playerName}' y PlayerId: {AuthenticationService.Instance.PlayerId}");
                
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
                
                // Unirse al nuevo lobby con reintentos
                Lobby lobby = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode, options);
                        break; // Éxito, salir del bucle
                    }
                    catch (Exception e)
                    {
                        if (showDebugLogs)
                            Debug.LogWarning($"⚠️ Intento {attempt + 1} falló: {e.Message}");
                        
                        if (attempt < 2) // No es el último intento
                        {
                            await Task.Delay(1000 * (attempt + 1)); // Delay incremental
                        }
                        else
                        {
                            throw; // Re-lanzar excepción en el último intento
                        }
                    }
                }
                
                if (lobby != null && showDebugLogs)
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
            
            // Solo el servidor debe manejar los jugadores
            if (NetworkManager.Singleton.IsHost)
            {
                if (showDebugLogs)
                    Debug.Log($"🔍 Servidor añadiendo jugador con clientId: {clientId}");
                
                // Añadir jugador inmediatamente con nombre temporal
                string playerName = clientId == 0 ? "Host" : "Player joining...";
                bool isHost = clientId == 0; // El primer cliente conectado es el host
                AddPlayerDirectly(clientId, playerName, isHost);

                // SIEMPRE actualizar nombres de manera asíncrona para todos los jugadores
                // Esto es especialmente importante cuando re-entras al lobby
                if (showDebugLogs)
                    Debug.Log($"🔄 Iniciando actualización asíncrona de nombres para cliente {clientId}");
                
                // Actualizar nombres en segundo plano sin bloquear
                _ = UpdatePlayerNamesAsync();
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
                Debug.Log($"🔌 Cliente desconectado: {clientId}");
            
            // Solo el servidor debe manejar los jugadores
            if (NetworkManager.Singleton.IsHost)
            {
                // Remover jugador de la lista
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].clientId == clientId)
                    {
                        if (showDebugLogs)
                            Debug.Log($"➖ Removiendo jugador: {players[i].playerName} (ID: {clientId})");

                        OnPlayerLeave?.Invoke(players[i]);
                        players.RemoveAt(i);
                        NotifyPlayersUpdated();
                        break;
                    }
                }
                
                // Si era el host quien se desconectó y somos el servidor, notificar a todos los clientes
                if (clientId == 0)
                {
                    if (showDebugLogs)
                        Debug.Log("🚪 Host se desconectó - Cerrando lobby para todos");
                    
                    CloseLobbyClientRpc();
                }
            }
            
            // Salir del canal de voz si es el jugador local
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                await LeaveLobbyVoiceChannel();
                if (showDebugLogs)
                    Debug.Log("🚪 Has salido del lobby");
            }
            
            // Si se desconectó el host (clientId 0) y nosotros somos cliente, salir
            if (clientId == 0 && !NetworkManager.Singleton.IsHost)
            {
                if (showDebugLogs)
                    Debug.Log("🚪 Host desconectado - Saliendo del lobby");
                
                HandleHostDisconnection();
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

                var props3D = new Channel3DProperties();

                // Unirse al canal usando las APIs v16
                await VivoxService.Instance.JoinPositionalChannelAsync(
                    lobbyCode,
                    ChatCapability.AudioOnly,
                    props3D);

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
            
            // Limpiar lobby actual COMPLETAMENTE
            CurrentLobby = null;
            
            // Limpiar diccionario de nombres
            // Dictionary cleared - no longer needed
            
            // Solo limpiar la lista de jugadores si es el servidor
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && players != null)
            {
                players.Clear();
            }
            
            // Asegurar que Unity Services esté en estado limpio para la próxima conexión
            if (showDebugLogs)
                Debug.Log("🧹 Estado del lobby limpiado completamente - Listo para re-entrada");
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

        private void AddPlayerDirectly(ulong clientId, string playerName, bool isHost)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            
            // SIEMPRE usar el nombre temporal proporcionado inicialmente
            // NO intentar obtener el nombre de Unity Services aquí para evitar inconsistencias
            // El sistema asíncrono se encargará de actualizar el nombre correcto después
            
            if (showDebugLogs)
                Debug.Log($"🔧 AddPlayerDirectly - ClientID: {clientId}, Nombre temporal: '{playerName}', EsHost: {isHost}");
            
            var playerInfo = new PlayerInfo
            {
                clientId = clientId,
                playerName = new FixedString32Bytes(playerName),
                isReady = true,
                isHost = isHost
            };
            
            players.Add(playerInfo);
            OnPlayerJoin?.Invoke(playerInfo);

            if (showDebugLogs)
                Debug.Log($"➕ Jugador agregado con nombre temporal: {playerName} (ID: {clientId}, Host: {isHost})");
            
            // Notificar cambio en la lista de jugadores
            NotifyPlayersUpdated();
        }
        
        private string GetPlayerNameFromUnityServices(ulong clientId)
        {
            if (CurrentLobby == null || CurrentLobby.Players == null)
            {
                if (showDebugLogs)
                    Debug.LogWarning($"⚠️ CurrentLobby o Players es null para clientId {clientId}");
                return null;
            }
            
            // ALWAYS show this critical debugging info
            Debug.Log($"🔍 Buscando nombre para clientId {clientId}. Unity Services tiene {CurrentLobby.Players.Count} jugadores:");
            for (int i = 0; i < CurrentLobby.Players.Count; i++)
            {
                var p = CurrentLobby.Players[i];
                string name = p.Data?.ContainsKey("PlayerName") == true ? p.Data["PlayerName"].Value : "N/A";
                string isHost = p.Data?.ContainsKey("IsHost") == true ? p.Data["IsHost"].Value : "N/A";
                Debug.Log($"  [{i}] PlayerId: {p.Id}, Name: '{name}', IsHost: {isHost}");
            }
            
            // Para el host (clientId 0), buscar por IsHost=true
            if (clientId == 0)
            {
                foreach (var player in CurrentLobby.Players)
                {
                    if (player.Data != null && player.Data.ContainsKey("IsHost") && player.Data["IsHost"].Value == "true")
                    {
                        if (player.Data.ContainsKey("PlayerName"))
                        {
                            string hostName = player.Data["PlayerName"].Value;
                            if (showDebugLogs)
                                Debug.Log($"✅ Nombre del host obtenido: '{hostName}'");
                            return hostName;
                        }
                    }
                }
            }
            else
            {
                // Para clientes, primero intentar mapeo directo por índice
                int targetIndex = (int)clientId;
                if (targetIndex < CurrentLobby.Players.Count)
                {
                    var player = CurrentLobby.Players[targetIndex];
                    if (showDebugLogs)
                        Debug.Log($"🔍 Checking client {clientId} at index {targetIndex}: PlayerId={player.Id}");
                    
                    if (player.Data != null && player.Data.ContainsKey("PlayerName"))
                    {
                        string playerName = player.Data["PlayerName"].Value;
                        if (showDebugLogs)
                            Debug.Log($"✅ Nombre obtenido para cliente {clientId}: '{playerName}'");
                        return playerName;
                    }
                }
                
                // Si no funciona el mapeo directo, usar mapeo inteligente para re-entradas
                Debug.Log($"🔄 Mapeo directo falló para cliente {clientId}, usando mapeo inteligente...");
                
                // Para re-entradas, mapear el cliente a cualquier jugador no-host disponible
                // Si solo hay 1 cliente en Unity Services pero Netcode asigna clientId > 1, usar ese único cliente
                var nonHostPlayers = new List<Unity.Services.Lobbies.Models.Player>();
                foreach (var player in CurrentLobby.Players)
                {
                    bool isHost = player.Data != null && player.Data.ContainsKey("IsHost") && player.Data["IsHost"].Value == "true";
                    Debug.Log($"  Evaluando jugador: PlayerId={player.Id}, isHost={isHost}");
                    if (!isHost)
                    {
                        nonHostPlayers.Add(player);
                    }
                }
                
                Debug.Log($"  Encontrados {nonHostPlayers.Count} jugadores no-host para clientId {clientId}");
                
                if (nonHostPlayers.Count > 0)
                {
                    // Para re-entradas, usar el último jugador no-host disponible
                    // Esto funciona bien cuando solo hay 1 cliente real pero clientId es mayor
                    var targetPlayer = nonHostPlayers[nonHostPlayers.Count - 1];
                    
                    if (targetPlayer.Data != null && targetPlayer.Data.ContainsKey("PlayerName"))
                    {
                        string playerName = targetPlayer.Data["PlayerName"].Value;
                        Debug.Log($"✅ Nombre obtenido por mapeo inteligente para cliente {clientId}: '{playerName}'");
                        return playerName;
                    }
                    else
                    {
                        Debug.LogWarning($"⚠️ Jugador encontrado pero sin PlayerName data para clientId {clientId}");
                    }
                }
            }
            
            if (showDebugLogs)
                Debug.LogWarning($"⚠️ No se encontró PlayerName para clientId {clientId}");
            
            return null;
        }
        
        private bool GetIsHostFromUnityServices(ulong clientId)
        {
            if (CurrentLobby == null || CurrentLobby.Players == null)
                return clientId == 0; // Fallback: primer cliente es host
            
            // Para el host (clientId 0), buscar por IsHost=true
            if (clientId == 0)
            {
                foreach (var player in CurrentLobby.Players)
                {
                    if (player.Data != null && player.Data.ContainsKey("IsHost") && player.Data["IsHost"].Value == "true")
                    {
                        if (showDebugLogs)
                            Debug.Log($"✅ Host confirmado desde Unity Services");
                        return true;
                    }
                }
            }
            else
            {
                // Para clientes, verificar por índice
                int targetIndex = (int)clientId;
                if (targetIndex < CurrentLobby.Players.Count)
                {
                    var player = CurrentLobby.Players[targetIndex];
                    if (player.Data != null && player.Data.ContainsKey("IsHost"))
                    {
                        bool isHost = player.Data["IsHost"].Value == "true";
                        if (showDebugLogs)
                            Debug.Log($"✅ IsHost obtenido para cliente {clientId}: {isHost}");
                        return isHost;
                    }
                }
            }
            
            return clientId == 0; // Fallback: primer cliente es host
        }
        
        private async Task UpdatePlayerNamesAsync()
        {
            // Intentar varias veces con delays incrementales más largos
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    // Delay más largo para dar tiempo a Unity Services: 1s, 2s, 3s, 4s, 5s
                    await Task.Delay(1000 * (attempt + 1));
                    
                    if (showDebugLogs)
                        Debug.Log($"🔄 Intento {attempt + 1} de actualización de nombres");
                    
                    // Actualizar CurrentLobby
                    var updatedLobby = await LobbyService.Instance.GetLobbyAsync(CurrentLobby.Id);
                    if (updatedLobby != null)
                    {
                        CurrentLobby = updatedLobby;
                        
                        if (showDebugLogs)
                            Debug.Log($"📋 Lobby actualizado con {CurrentLobby.Players.Count} jugadores");
                        
                        // Actualizar nombres de todos los jugadores que tengan nombres temporales
                        bool namesUpdated = false;
                        for (int i = 0; i < players.Count; i++)
                        {
                            var currentPlayer = players[i];
                            string currentPlayerName = currentPlayer.playerName.ToString();
                            
                            // Solo actualizar si el jugador tiene un nombre temporal O si es el primer intento (para re-entradas)
                            bool shouldUpdate = currentPlayerName == "Player joining..." || 
                                              currentPlayerName.StartsWith("Player_") ||
                                              currentPlayerName == "Host"; // También actualizar "Host" en caso de re-entrada
                            
                            if (shouldUpdate)
                            {
                                string correctName = GetPlayerNameFromUnityServices(currentPlayer.clientId);
                                bool correctIsHost = GetIsHostFromUnityServices(currentPlayer.clientId);
                                
                                if (!string.IsNullOrEmpty(correctName) && correctName != currentPlayerName)
                                {
                                    var updatedPlayer = new PlayerInfo
                                    {
                                        clientId = currentPlayer.clientId,
                                        playerName = new FixedString32Bytes(correctName),
                                        isReady = currentPlayer.isReady,
                                        isHost = correctIsHost
                                    };
                                    
                                    players[i] = updatedPlayer;
                                    namesUpdated = true;
                                    
                                    if (showDebugLogs)
                                        Debug.Log($"✅ Nombre actualizado: '{correctName}' para cliente {currentPlayer.clientId} (antes: '{currentPlayerName}')");
                                }
                                else if (showDebugLogs && string.IsNullOrEmpty(correctName))
                                {
                                    Debug.LogWarning($"⚠️ No se pudo obtener nombre correcto para cliente {currentPlayer.clientId}");
                                }
                            }
                        }
                        
                        if (namesUpdated)
                        {
                            NotifyPlayersUpdated();
                            if (showDebugLogs)
                                Debug.Log($"🎯 Actualización de nombres completada en intento {attempt + 1}");
                            return; // Éxito, salir del bucle
                        }
                        else if (attempt >= 2) // Después del intento 3, verificar si todos tienen nombres correctos
                        {
                            bool allNamesCorrect = true;
                            int temporaryNames = 0;
                            foreach (var player in players)
                            {
                                string playerName = player.playerName.ToString();
                                if (playerName == "Player joining..." || playerName.StartsWith("Player_"))
                                {
                                    allNamesCorrect = false;
                                    temporaryNames++;
                                }
                            }
                            
                            if (allNamesCorrect)
                            {
                                if (showDebugLogs)
                                    Debug.Log("✅ Todos los nombres ya están correctos");
                                return; // Todos los nombres están bien, salir
                            }
                            else if (showDebugLogs)
                            {
                                Debug.Log($"📊 Quedan {temporaryNames} nombres temporales por actualizar en intento {attempt + 1}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ Intento {attempt + 1} falló: {e.Message}");
                    
                    // Si es rate limit, esperar más tiempo
                    if (e.Message.Contains("Rate limit"))
                    {
                        await Task.Delay(2000); // Esperar 2 segundos adicionales
                    }
                }
            }
            
            if (showDebugLogs)
                Debug.LogWarning($"⚠️ No se pudieron actualizar todos los nombres después de 5 intentos");
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
        
        public PlayerInfo GetPlayerInfo(ulong clientId)
        {
            foreach (var player in players)
            {
                if (player.clientId == clientId)
                {
                    return player;
                }
            }
            throw new InvalidOperationException($"No se encontró un player con clientId {clientId}.");
        }

        public PlayerInfo GetCurrentPlayerInfo() 
        {
            return GetPlayerInfo(NetworkManager.Singleton.LocalClientId);
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