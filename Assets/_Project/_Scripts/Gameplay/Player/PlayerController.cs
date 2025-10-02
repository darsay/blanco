using Blanco.Networking;
using Unity.Cinemachine;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerController : NetworkBehaviour
{
    [Header("References")]
    public CinemachineCamera playerCamera;

    [Header("Card")]
    [SerializeField] private CardController owningCard;
    [SerializeField] private CardController nonOwningCard;

    [Header("Player Prefabs")]
    [SerializeField] private GameObject owningPlayer;
    [SerializeField] private GameObject nonOwningPlayer;

    [Header("Player Controllers")]
    [SerializeField] private LocalPlayerController localPlayerController;
    [SerializeField] private RemotePlayerController remotePlayerController;

    [SerializeField] private PlayerTag playerTag;

    public CardController Card;

    private bool isGhost;
    private bool subscribedToLobbyUpdates;
    private Coroutine lobbySubscriptionCoroutine;

    public bool IsGhost => isGhost;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            if (owningPlayer != null)
                owningPlayer.SetActive(true);
            if (nonOwningPlayer != null)
                nonOwningPlayer.SetActive(false);
            if (playerCamera != null)
                playerCamera.enabled = true;
            Card = owningCard;
        }
        else
        {
            if (owningPlayer != null)
                owningPlayer.SetActive(false);
            if (nonOwningPlayer != null)
                nonOwningPlayer.SetActive(true);
            if (playerCamera != null)
                playerCamera.enabled = false;
            Card = nonOwningCard;
        }

        UpdatePlayerNameFromLobby();
        SubscribeToLobbyUpdates();
        ApplyGhostState(false);
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromLobbyUpdates();
        base.OnNetworkDespawn();
    }

    [ClientRpc]
    public void SetCardValuesClientRpc(FixedString32Bytes word, bool isBlanco)
    {
        if (!IsOwner)
            return;

        // Actualiza la carta local segun si el jugador ocupa el rol de Blanco.
        Card.SetWord(isBlanco ? "Blanco" : word.ToString());
    }

    public void SetPlayerName(string name)
    {
        playerTag?.SetName(name);
    }

    void UpdatePlayerNameFromLobby()
    {
        SetPlayerName(GetDisplayName(OwnerClientId));
    }

    string GetDisplayName(ulong clientId)
    {
        if (LobbyManager.Instance != null)
        {
            try
            {
                var playerInfo = LobbyManager.Instance.GetPlayerInfo(clientId);
                if (!playerInfo.playerName.IsEmpty)
                {
                    return playerInfo.playerName.ToString();
                }
            }
            catch
            {
                // Ignorar lookup fallido
            }
        }

        return $"Jugador {clientId}";
    }

    void SubscribeToLobbyUpdates()
    {
        if (subscribedToLobbyUpdates)
            return;

        var lobby = LobbyManager.Instance;
        if (lobby == null)
        {
            if (lobbySubscriptionCoroutine == null)
            {
                lobbySubscriptionCoroutine = StartCoroutine(WaitForLobbyManagerAndSubscribe());
            }
            return;
        }

        lobby.OnPlayersUpdated += HandleLobbyPlayersUpdated;
        subscribedToLobbyUpdates = true;
    }

    void UnsubscribeFromLobbyUpdates()
    {
        if (lobbySubscriptionCoroutine != null)
        {
            StopCoroutine(lobbySubscriptionCoroutine);
            lobbySubscriptionCoroutine = null;
        }

        if (!subscribedToLobbyUpdates)
            return;

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayersUpdated -= HandleLobbyPlayersUpdated;
        }

        subscribedToLobbyUpdates = false;
    }

    IEnumerator WaitForLobbyManagerAndSubscribe()
    {
        // Reintenta la suscripcion cuando el LobbyManager aun no esta inicializado.
        while (LobbyManager.Instance == null)
        {
            yield return null;
        }

        lobbySubscriptionCoroutine = null;
        SubscribeToLobbyUpdates();
    }

    void HandleLobbyPlayersUpdated(List<LobbyManager.PlayerInfo> lobbyPlayers)
    {
        if (lobbyPlayers == null)
            return;

        foreach (var info in lobbyPlayers)
        {
            if (info.clientId == OwnerClientId)
            {
                SetPlayerName(info.playerName.ToString());
                return;
            }
        }
    }

    [ClientRpc]
    public void ShowCardClientRpc(bool show)
    {
        if (IsOwner)
        {
            localPlayerController?.SeeCard(show);
        }
    }

    [ClientRpc]
    public void PointClientRpc(bool active)
    {
        if (IsOwner)
        {
            localPlayerController?.Point(active);
        }
    }

    [ClientRpc]
    public void AimClientRpc(bool active)
    {
        if (IsOwner)
        {
            localPlayerController?.Aim(active);
        }
    }

    public void SetGhostStateServer(bool ghost)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        ApplyGhostState(ghost);
        SetGhostStateClientRpc(ghost);
    }

    [ClientRpc]
    void SetGhostStateClientRpc(bool ghost)
    {
        ApplyGhostState(ghost);
    }

    public void ResetGhostState()
    {
        SetGhostStateServer(false);
    }

    private void ApplyGhostState(bool ghost)
    {
        isGhost = ghost;

        if (playerTag != null)
        {
            playerTag.gameObject.SetActive(!ghost);
        }

        if (!IsOwner && nonOwningPlayer != null)
        {
            nonOwningPlayer.SetActive(!ghost);
        }

        if (IsOwner)
        {
            localPlayerController?.SetGhostState(ghost);
        }
        else
        {
            remotePlayerController?.SetGhostState(ghost);
        }
    }
}
