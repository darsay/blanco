using Blanco.Networking;
using DG.Tweening;
using System.Collections;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class LocalPlayerController : NetworkBehaviour
{
    [Header("References")]
    public PlayerActionsSync playerActionsSync;
    public CinemachineCamera playerCamera;
    public Rig RightHandRig;
    public Rig LeftHandRig;
    public GameObject revolver;

    [Header("Voting")]
    [SerializeField] private PlayerRayHighlighter rayHighlighter;
    [SerializeField] private float voteRayDistance = 100f;
    [SerializeField] private LayerMask voteLayerMask = ~0;
    [SerializeField] private Animator revolverAnimator;
    [SerializeField] private ParticleSystem successfulShotVfx;
    [SerializeField] private ParticleSystem jamVfx;
    [SerializeField] private AudioSource successfulShotAudio;
    [SerializeField] private AudioSource jamAudio;
    [SerializeField] private string shootTrigger = "Shoot";
    [SerializeField] private string jamTrigger = "Jam";

    [Header("Sensitivity")]
    public float sensitivity = 100f;

    [Header("Rotation Limits")]
    public float minVerticalAngle = -45f;
    public float maxVerticalAngle = 45f;
    public float minHorizontalAngle = -90f;
    public float maxHorizontalAngle = 90f;

    private float verticalRotation = 0f;
    private float horizontalRotation = 0f;
    private ulong currentVoteTarget = PlayerActionsSync.NoTarget;

    // Marcas internas
    bool _positionalReady = false;
    string _positionalChannelName = null;
    private bool feedbackRefsValidated;
    private bool isGhost;

    private void Awake()
    {
        if (rayHighlighter == null)
        {
            rayHighlighter = GetComponentInChildren<PlayerRayHighlighter>();
        }
    }

    private void Start()
    {
        RightHandRig.weight = 0f;
        LeftHandRig.weight = 0f;
    }

    void Update()
    {
        if (!IsOwner) return;

        if (NetworkManager.Singleton.IsHost && MatchManager.Instance != null && MatchManager.Instance.currentState.Value != MatchManager.MatchState.Playing)
        {
            HandleBeginingMatch();
        }

        HandlePointing();
        HandleSeeCard();
        HandleVotingSelection();

        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;

        horizontalRotation += mouseX;
        verticalRotation -= mouseY;

        horizontalRotation = Mathf.Clamp(horizontalRotation, minHorizontalAngle, maxHorizontalAngle);
        verticalRotation = Mathf.Clamp(verticalRotation, minVerticalAngle, maxVerticalAngle);

        if (playerCamera != null)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
        }
    }

    private void HandleBeginingMatch()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            MatchManager.Instance.OnBeginMatch();
        }
    }

    void HandlePointing()
    {
        if (isGhost)
            return;

        if (RoundManager.Instance != null && RoundManager.Instance.currentState.Value == RoundManager.RoundState.Voting)
            return;

        if (Input.GetMouseButtonDown(1))
        {
            Point(true);
        }
        else if (Input.GetMouseButtonUp(1))
        {
            Point(false);
        }
    }

    void HandleSeeCard()
    {
        if (isGhost)
            return;

        if (RoundManager.Instance == null || RoundManager.Instance.currentState.Value != RoundManager.RoundState.Talking)
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

    void HandleVotingSelection()
    {
        if (isGhost)
            return;

        if (RoundManager.Instance == null || RoundManager.Instance.currentState.Value != RoundManager.RoundState.Voting)
            return;

        PlayerCollider targetCollider = GetCurrentVoteTarget();

        if (Input.GetMouseButtonDown(0))
        {
            if (targetCollider != null && targetCollider.OwnerController != null && !targetCollider.OwnerController.IsGhost)
            {
                ulong targetClientId = targetCollider.OwnerController.OwnerClientId;
                if (targetClientId != OwnerClientId)
                {
                    SelectVoteTarget(targetClientId);
                }
            }
            else
            {
                ClearVoteSelection(true);
            }
        }
    }

    PlayerCollider GetCurrentVoteTarget()
    {
        if (rayHighlighter != null && IsSelectableTarget(rayHighlighter.CurrentTarget))
        {
            return rayHighlighter.CurrentTarget;
        }

        if (playerCamera == null) return null;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        var hits = Physics.RaycastAll(ray, voteRayDistance, voteLayerMask, QueryTriggerInteraction.Ignore);
        PlayerCollider best = null;
        float bestDistance = float.MaxValue;

        foreach (var hit in hits)
        {
            var potential = hit.collider.GetComponentInParent<PlayerCollider>();
            if (!IsSelectableTarget(potential))
                continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                best = potential;
            }
        }

        return best;
    }

    bool IsSelectableTarget(PlayerCollider collider)
    {
        if (collider == null)
            return false;

        var controller = collider.OwnerController;
        if (controller == null)
            return false;

        if (controller.IsGhost)
            return false;

        if (controller.OwnerClientId == OwnerClientId)
            return false;

        return true;
    }

    void SelectVoteTarget(ulong targetClientId)
    {
        if (currentVoteTarget == targetClientId)
            return;

        currentVoteTarget = targetClientId;

        if (playerActionsSync != null)
        {
            playerActionsSync.selectedVoteTarget.Value = targetClientId;
        }

        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.SubmitVoteServerRpc(targetClientId);
        }

        UpdateVoteSelectionUI(targetClientId);
    }

    void ClearVoteSelection(bool notifyServer)
    {
        if (currentVoteTarget == PlayerActionsSync.NoTarget)
            return;

        currentVoteTarget = PlayerActionsSync.NoTarget;

        if (playerActionsSync != null)
        {
            playerActionsSync.selectedVoteTarget.Value = PlayerActionsSync.NoTarget;
        }

        if (notifyServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.SubmitVoteServerRpc(PlayerActionsSync.NoTarget);
        }

        UpdateVoteSelectionUI(PlayerActionsSync.NoTarget);
    }

    void UpdateVoteSelectionUI(ulong targetClientId)
    {
        if (UIGameplayManager.Instance == null)
            return;

        if (targetClientId == PlayerActionsSync.NoTarget)
        {
            UIGameplayManager.Instance.SetLocalVoteSelection("No target selected");
            return;
        }

        string displayName = ResolvePlayerName(targetClientId);
        UIGameplayManager.Instance.SetLocalVoteSelection($"Aiming at {displayName}");
    }

    public void SetGhostState(bool ghost)
    {
        if (isGhost == ghost)
            return;

        isGhost = ghost;

        if (ghost)
        {
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.SubmitVoteServerRpc(PlayerActionsSync.NoTarget);
            }

            ClearVoteSelection(false);
            Point(false);
            Aim(false);
            SeeCard(false);
        }

        if (playerActionsSync != null)
        {
            playerActionsSync.isPlayerPointing.Value = false;
            playerActionsSync.isPlayerCheckingCard.Value = false;
            playerActionsSync.isPlayerAiming.Value = false;
            playerActionsSync.selectedVoteTarget.Value = PlayerActionsSync.NoTarget;
        }

        if (rayHighlighter != null)
        {
            rayHighlighter.ClearCurrentTarget();
            rayHighlighter.enabled = !ghost;
        }

        if (ghost)
        {
            UIGameplayManager.Instance?.SetLocalVoteSelection("No target selected");
            if (VivoxService.Instance != null)
            {
                VivoxService.Instance.MuteInputDevice();
            }
        }
        else
        {
            if (VivoxService.Instance != null)
            {
                VivoxService.Instance.UnmuteInputDevice();
            }
        }
    }

    void ValidateFeedbackReferences()
    {
        if (feedbackRefsValidated)
            return;

        feedbackRefsValidated = true;

        if (revolverAnimator == null)
        {
            Debug.LogWarning($"[{nameof(LocalPlayerController)}] Missing revolver animator on {gameObject.name}.", this);
        }

        if (successfulShotVfx == null)
        {
            Debug.LogWarning($"[{nameof(LocalPlayerController)}] Missing successful shot VFX on {gameObject.name}.", this);
        }

        if (jamVfx == null)
        {
            Debug.LogWarning($"[{nameof(LocalPlayerController)}] Missing jam VFX on {gameObject.name}.", this);
        }

        if (successfulShotAudio == null)
        {
            Debug.LogWarning($"[{nameof(LocalPlayerController)}] Missing successful shot AudioSource on {gameObject.name}.", this);
        }

        if (jamAudio == null)
        {
            Debug.LogWarning($"[{nameof(LocalPlayerController)}] Missing jam AudioSource on {gameObject.name}.", this);
        }

        if (revolverAnimator != null && string.IsNullOrEmpty(shootTrigger))
        {
            Debug.LogWarning($"[{nameof(LocalPlayerController)}] Shoot trigger name is empty on {gameObject.name}.", this);
        }

        if (revolverAnimator != null && string.IsNullOrEmpty(jamTrigger))
        {
            Debug.LogWarning($"[{nameof(LocalPlayerController)}] Jam trigger name is empty on {gameObject.name}.", this);
        }
    }    string ResolvePlayerName(ulong clientId)
    {
        if (LobbyManager.Instance != null)
        {
            try
            {
                var info = LobbyManager.Instance.GetPlayerInfo(clientId);
                if (!info.playerName.IsEmpty)
                {
                    return info.playerName.ToString();
                }
            }
            catch
            {
                // Ignorar lookup fallido, usar fallback
            }
        }

        return $"Jugador {clientId}";
    }

    void OnVoteOutcomeChanged(PlayerActionsSync.VoteOutcome previousValue, PlayerActionsSync.VoteOutcome newValue)
    {
        if (newValue == PlayerActionsSync.VoteOutcome.None)
            return;

        bool success = newValue == PlayerActionsSync.VoteOutcome.Success;
        bool isTie = newValue == PlayerActionsSync.VoteOutcome.Tie;

        if (isTie)
        {
            PlayVotingResult(false, true);
        }
        else
        {
            PlayVotingResult(success, false);
        }
    }

    void OnSelectedVoteTargetChanged(ulong previousValue, ulong newValue)
    {
        if (!IsOwner)
            return;

        currentVoteTarget = newValue;
        UpdateVoteSelectionUI(newValue);
    }

    void PlayVotingResult(bool success, bool isTie)
    {
        if (revolverAnimator != null)
        {
            if (success)
            {
                if (!string.IsNullOrEmpty(shootTrigger))
                {
                    revolverAnimator.SetTrigger(shootTrigger);
                }
            }
            else if (!string.IsNullOrEmpty(jamTrigger))
            {
                revolverAnimator.SetTrigger(jamTrigger);
            }
        }

        if (successfulShotVfx != null)
        {
            if (success)
            {
                successfulShotVfx.Play();
            }
            else
            {
                successfulShotVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        if (jamVfx != null)
        {
            if (success)
            {
                jamVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            else
            {
                jamVfx.Play();
            }
        }

        if (success)
        {
            if (successfulShotAudio != null)
            {
                successfulShotAudio.Play();
            }
        }
        else if (!isTie && jamAudio !=  null)
        {
            jamAudio.Play();
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
        if (revolver != null)
        {
            revolver.SetActive(active);
        }

        var targetValue = active ? 1f : 0f;

        playerActionsSync.isPlayerAiming.Value = active;
        DOTween.To(() => RightHandRig.weight, x => RightHandRig.weight = x, targetValue, 0.3f);
    }

    //VIVOX
    void OnEnable()
    {
        feedbackRefsValidated = false;
        ValidateFeedbackReferences();

        if (playerActionsSync != null)
        {
            playerActionsSync.voteOutcome.OnValueChanged += OnVoteOutcomeChanged;
            playerActionsSync.selectedVoteTarget.OnValueChanged += OnSelectedVoteTargetChanged;

            if (IsOwner)
            {
                currentVoteTarget = playerActionsSync.selectedVoteTarget.Value;
                UpdateVoteSelectionUI(currentVoteTarget);
            }
        }

        if (VivoxService.Instance != null)
        {
            VivoxService.Instance.ChannelJoined += OnChannelJoined;
            VivoxService.Instance.ChannelLeft += OnChannelLeft;
        }

        // ?? Bootstrap por si ya estabas unido antes de que este script se habilite
        StartCoroutine(BootstrapExistingChannel());
    }

    void OnDisable()
    {
        if (playerActionsSync != null)
        {
            playerActionsSync.voteOutcome.OnValueChanged -= OnVoteOutcomeChanged;
            playerActionsSync.selectedVoteTarget.OnValueChanged -= OnSelectedVoteTargetChanged;
        }

        if (VivoxService.Instance != null)
        {
            VivoxService.Instance.ChannelJoined -= OnChannelJoined;
            VivoxService.Instance.ChannelLeft -= OnChannelLeft;
        }
    }

    IEnumerator BootstrapExistingChannel()
    {
        if (VivoxService.Instance == null)
            yield break;

        // Espera a que el servicio este disponible y logueado
        while (VivoxService.Instance != null && !VivoxService.Instance.IsLoggedIn)
            yield return null;

        if (VivoxService.Instance == null)
            yield break;

        // Espera un frame por seguridad (escena recien cargada)
        yield return null;

        // ? Intenta detectar si YA estas en el canal posicional (variantes por SDK)
        if (LobbyManager.CurrentLobby != null && IsChannelAlreadyConnected(LobbyManager.CurrentLobby.LobbyCode))
        {
            _positionalChannelName = LobbyManager.CurrentLobby.LobbyCode;
            _positionalReady = true;
        }
    }

    // Utilidad: adapta por tu SDK v16 si el nombre difiere
    bool IsChannelAlreadyConnected(string channelName)
    {
        var channels = VivoxService.Instance?.ActiveChannels;
        if (channels != null)
        {
            foreach (var ch in channels)
            {
                if (string.Equals(ch.Key, channelName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    void OnChannelJoined(string channelName)
    {
        if (LobbyManager.CurrentLobby != null && channelName == LobbyManager.CurrentLobby.LobbyCode)
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
        if (!_positionalReady || VivoxService.Instance == null)
            return;
        if (Time.unscaledTime - _lastSent < kInterval)
            return;
        _lastSent = Time.unscaledTime;

        if (playerCamera == null)
            return;

        VivoxService.Instance.Set3DPosition(
            playerCamera.transform.position,
            playerCamera.transform.position,
            playerCamera.transform.forward.normalized,
            playerCamera.transform.up.normalized,
            _positionalChannelName,
            allowPanning: true
        );
    }
}








