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
            Debug.Log("✅ PlayerSpawner suscrito a OnClientConnectedCallback");
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= SpawnPlayer;
        }
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

        Transform spawnPoint = GetNextSpawnPoint();
        Debug.Log($"📍 Spawn point: {spawnPoint.name}");

        GameObject playerInstance = Instantiate(playerPrefab.gameObject, spawnPoint.position, spawnPoint.rotation);
        Vector3 direction = (lookAtTarget.position - spawnPoint.position).normalized;
        Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
        playerInstance.transform.rotation = lookRotation;

        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(player.clientId);
        
        Debug.Log($"✅ Player spawnado exitosamente para {player.playerName}");
    }

    private Transform GetNextSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            Debug.LogError("❌ No hay spawn points configurados!");
            return transform; // Fallback
        }
        
        Transform point = spawnPoints[nextSpawnIndex];
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Count;
        return point;
    }
}
