using Blanco.Networking;
using DG.Tweening;
using System;
using System.Collections;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using static System.TimeZoneInfo;

public class LocalPlayerController : NetworkBehaviour
{
    [Header("References")]
    public PlayerActionsSync playerActionsSync;
    public CinemachineCamera playerCamera;
    public Rig RightHandRig;
    public Rig LeftHandRig;
    public GameObject revolver;

    [Header("Sensitivity")]
    public float sensitivity = 100f;

    [Header("Rotation Limits")]
    public float minVerticalAngle = -45f;
    public float maxVerticalAngle = 45f;
    public float minHorizontalAngle = -90f;
    public float maxHorizontalAngle = 90f;

    private float verticalRotation = 0f;
    private float horizontalRotation = 0f;

    // Marcas internas
    bool _positionalReady = false;
    string _positionalChannelName = null;


    private void Start()
    {
        RightHandRig.weight = 0f;
        LeftHandRig.weight = 0f;
    }

    void Update()
    {
        if (!IsOwner) return;

        if(NetworkManager.Singleton.IsHost && MatchManager.Instance.currentState.Value == MatchManager.MatchState.WaitingForPlayers)
        {
            HandleBeginingMatch();
        }

        HandlePointing();
        HandleSeeCard();

        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;

        horizontalRotation += mouseX;
        verticalRotation -= mouseY;

        horizontalRotation = Mathf.Clamp(horizontalRotation, minHorizontalAngle, maxHorizontalAngle);
        verticalRotation = Mathf.Clamp(verticalRotation, minVerticalAngle, maxVerticalAngle);

        playerCamera.transform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
    }

    private void HandleBeginingMatch()
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            MatchManager.Instance.OnBeginMatch();
        }
    }

    void HandlePointing()
    {
        if (RoundManager.Instance.currentState.Value == RoundManager.RoundState.Voting)
            return;

        if (Input.GetMouseButtonDown(1))
        {
            Point(true);
        }
        else if(Input.GetMouseButtonUp(1))
        {
            Point(false);
        }
    }

    void HandleSeeCard()
    {
        if(RoundManager.Instance.currentState.Value != RoundManager.RoundState.Talking)
            return;

        if (Input.GetMouseButtonDown(0))
        {
           SeeCard(true);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            SeeCard(false);
        }
    }

    public void SeeCard(bool show)
    {
        var targetValue = show ? 1f : 0f;

        playerActionsSync.isPlayerCheckingCard.Value = show;
        DOTween.To(() => LeftHandRig.weight, x => LeftHandRig.weight = x, targetValue, 0.3f);
    }

    public void Point(bool active)
    {
        var targetValue = active ? 1f : 0f;

        playerActionsSync.isPlayerPointing.Value = active;
        DOTween.To(() => RightHandRig.weight, x => RightHandRig.weight = x, targetValue, 0.3f);
    }

    public void Aim(bool active)
    {
        revolver.SetActive(active);

        var targetValue = active ? 1f : 0f;

        playerActionsSync.isPlayerAiming.Value = active;
        revolver.SetActive(active);
        DOTween.To(() => RightHandRig.weight, x => RightHandRig.weight = x, targetValue, 0.3f);
    }

    //VIVOX
    void OnEnable()
    {
        VivoxService.Instance.ChannelJoined += OnChannelJoined;
        VivoxService.Instance.ChannelLeft += OnChannelLeft;

        // 🔰 Bootstrap por si ya estabas unido antes de que este script se habilite
        StartCoroutine(BootstrapExistingChannel());
    }

    void OnDisable()
    {
        if (VivoxService.Instance != null)
        {
            VivoxService.Instance.ChannelJoined -= OnChannelJoined;
            VivoxService.Instance.ChannelLeft -= OnChannelLeft;
        }
    }

    IEnumerator BootstrapExistingChannel()
    {
        // Espera a que el servicio esté disponible y logueado
        while (VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn)
            yield return null;

        // Espera un frame por seguridad (escena recién cargada)
        yield return null;

        // ✅ Intenta detectar si YA estás en el canal posicional (variantes por SDK)
        if (IsChannelAlreadyConnected(LobbyManager.CurrentLobby.Id))
        {
            _positionalChannelName = LobbyManager.CurrentLobby.Id;
            _positionalReady = true;
        }
    }

    // Utilidad: adapta por tu SDK v16 si el nombre difiere
    bool IsChannelAlreadyConnected(string channelName)
    {
        // (suele existir algo tipo JoinedChannels/ActiveChannels/Channels)
        var channels = VivoxService.Instance.ActiveChannels; // renómbralo si tu propiedad difiere
        if (channels != null)
        {
            foreach (var ch in channels)
            {
                // ch.Name o ch.ChannelName según tu tipo
                if (string.Equals(ch.Key, channelName, System.StringComparison.OrdinalIgnoreCase))
                {
                    // Si el tipo expone estado:
                    // return ch.IsConnected || ch.AudioState == AudioConnectionState.Connected;
                    return true;
                }
            }
        }
        return false;
    }

    void OnChannelJoined(string channelName)
    {
        if (channelName == LobbyManager.CurrentLobby.Id)
        {
            _positionalChannelName = channelName;
            _positionalReady = true;
        }
    }

    void OnChannelLeft(string channelName)
    {
        if (channelName == _positionalChannelName)
        {
            _positionalReady = false;
            _positionalChannelName = null;
        }
    }

    float _lastSent;
    const float kInterval = 0.05f; // 20 Hz

    void LateUpdate()
    {
        if (!_positionalReady) return;
        if (Time.unscaledTime - _lastSent < kInterval) return;
        _lastSent = Time.unscaledTime;

        // Enviar a Vivox (la sobrecarga con Vector3 es la más precisa)
        VivoxService.Instance.Set3DPosition(
            playerCamera.transform.position,                 // speakerPos (boca)
            playerCamera.transform.position,                // listenerPos (oídos)
            playerCamera.transform.forward.normalized,      // listenerAtOrient
            playerCamera.transform.up.normalized,           // listenerUpOrient
            _positionalChannelName,
            allowPanning: true
        );
    }
}
