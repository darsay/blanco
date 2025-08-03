using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using static MatchManager;

public class PlayerSpawner : NetworkBehaviour
{
    public static PlayerSpawner Instance;
    [SerializeField] private List<Transform> spawnPoints;
    [SerializeField] private Transform lookAtTarget;
    [SerializeField] private PlayerController playerPrefab;
    private int nextSpawnIndex = 0;

    private void Awake()
    {
        Instance = this;
    }

    public void SpawnHostPlayer()
    {
        if (IsServer)
        {
            HandleClientConnected(NetworkManager.Singleton.LocalClientId);
        }
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServer)
            return;

        Debug.Log($"[SERVER] Spawning player for client {clientId}");

        if (NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject != null)
            return;

        Transform spawnPoint = GetNextSpawnPoint();

        GameObject playerInstance = Instantiate(playerPrefab.gameObject, spawnPoint.position, spawnPoint.rotation);
        Vector3 direction = (lookAtTarget.position - spawnPoint.position).normalized;
        Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
        playerInstance.transform.rotation = lookRotation;

        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(clientId);
    }

    private Transform GetNextSpawnPoint()
    {
        Transform point = spawnPoints[nextSpawnIndex];
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Count;
        return point;
    }
}
