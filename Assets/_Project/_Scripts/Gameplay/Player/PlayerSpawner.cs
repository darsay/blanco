using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using static MatchManager;
using Blanco.Networking;
using static Blanco.Networking.LobbyManager;

public class PlayerSpawner : NetworkBehaviour
{
    public static PlayerSpawner Instance;
    [SerializeField] private List<Transform> spawnPoints;
    [SerializeField] private Transform lookAtTarget;
    [SerializeField] private PlayerController playerPrefab;
    private Blanco.Networking.LobbyManager lobbyManager;
    private int nextSpawnIndex = 0;
    private readonly Dictionary<ulong, int> seatAssignments = new();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Intentar obtener LobbyManager
        lobbyManager = Blanco.Networking.LobbyManager.Instance;
        
        // Si no existe, intentar en Update
        if (lobbyManager == null)
        {
            Debug.LogWarning("⚠️ LobbyManager.Instance es null, intentando en Update...");
            return;
        }

        if (NetworkManager.Singleton.IsHost)
        {
            SpawnPlayer(0);
            NetworkManager.Singleton.OnClientConnectedCallback += SpawnPlayer;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            Debug.Log("✅ PlayerSpawner suscrito a OnClientConnectedCallback");
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= SpawnPlayer;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    public void SpawnPlayer(ulong networkId)
    {
        PlayerInfo player = lobbyManager.GetPlayerInfo(networkId);
        Debug.Log($"🎯 SpawnPlayer llamado para {player.playerName} (clientId: {player.clientId})");

        if (!NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("⚠️ SpawnPlayer llamado pero no es servidor");
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("❌ NetworkManager.Singleton es null");
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(player.clientId))
        {
            Debug.LogWarning($"⚠️ Cliente {player.clientId} no está conectado");
            return;
        }

        if (NetworkManager.Singleton.ConnectedClients[player.clientId].PlayerObject != null)
        {
            Debug.Log($"⚠️ Cliente {player.clientId} ya tiene PlayerObject");
            return;
        }

        Transform spawnPoint = GetSpawnPointForPlayer(player.clientId);
        Debug.Log($"📍 Spawn point: {spawnPoint.name}");

        GameObject playerInstance = Instantiate(playerPrefab.gameObject, spawnPoint.position, spawnPoint.rotation);
        Vector3 direction = (lookAtTarget.position - spawnPoint.position).normalized;
        Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
        playerInstance.transform.rotation = lookRotation;

        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(player.clientId);
        
        Debug.Log($"✅ Player spawnado exitosamente para {player.playerName}");
    }

    private Transform GetSpawnPointForPlayer(ulong clientId)
    {
        if (seatAssignments.TryGetValue(clientId, out int seatIndex))
        {
            if (seatIndex >= 0 && seatIndex < spawnPoints.Count)
            {
                return spawnPoints[seatIndex];
            }

            seatAssignments.Remove(clientId);
        }

        return AssignNextAvailableSpawnPoint(clientId);
    }

    private Transform AssignNextAvailableSpawnPoint(ulong clientId)
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            Debug.LogError("❌ No hay spawn points configurados!");
            return transform;
        }

        return UseSequentialSpawn(clientId);
    }

    private Transform UseSequentialSpawn(ulong clientId)
    {
        int seatIndex = GetNextSequentialSeat();
        seatAssignments[clientId] = seatIndex;
        return spawnPoints[seatIndex];
    }

    private int GetNextSequentialSeat()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
            return 0;

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            int seatIndex = nextSpawnIndex % spawnPoints.Count;
            nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Count;

            if (IsSeatAvailable(seatIndex))
            {
                return seatIndex;
            }
        }

        Debug.LogWarning("⚠️ No hay asientos disponibles. Usando asiento 0 por defecto.");
        return 0;
    }

    private bool IsSeatAvailable(int seatIndex)
    {
        foreach (var assignment in seatAssignments.Values)
        {
            if (assignment == seatIndex)
                return false;
        }

        return true;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        seatAssignments.Remove(clientId);
    }

    public bool TryGetSeatIndex(ulong clientId, out int seatIndex)
    {
        return seatAssignments.TryGetValue(clientId, out seatIndex);
    }
}
