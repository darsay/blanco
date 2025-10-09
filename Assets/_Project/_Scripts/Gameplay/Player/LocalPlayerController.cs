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

    [Header("Camera Control")]
    [SerializeField] private bool enforceCursorLock = true;
    [SerializeField] private float forcedLookBlendDuration = 0.35f;
    [SerializeField] private float forcedLookHoldGrace = 0.15f;
    [SerializeField] private float voteSelectionFocusHeightOffset = 1.6f;

    [Header("Rotation Limits")]
    public float minVerticalAngle = -45f;
    public float maxVerticalAngle = 45f;
    public float minHorizontalAngle = -90f;
    public float maxHorizontalAngle = 90f;

    private float verticalRotation = 0f;
    private float horizontalRotation = 0f;
    private ulong currentVoteTarget = PlayerActionsSync.NoTarget;
    private bool cameraInputLocked;
    private bool cameraManualUnlockRequired;
    private float cameraUnlockTime;
    private Coroutine forcedLookRoutine;

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
        if (!IsOwner)
            return;

        if (enforceCursorLock)
        {
            MaintainCursorLock();
        }

        if (NetworkManager.Singleton.IsHost && MatchManager.Instance != null && MatchManager.Instance.currentState.Value != MatchManager.MatchState.Playing)
        {
            HandleBeginingMatch();
        }

        HandlePointing();
        HandleSeeCard();
        HandleVotingSelection();

        UpdateCameraInput();
        ApplyCameraRotation();
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

    void UpdateCameraInput()
    {
        if (playerCamera == null)
            return;

        if (cameraInputLocked)
        {
            if (!cameraManualUnlockRequired && Time.time >= cameraUnlockTime)
            {
                ReleaseForcedLook();
            }
        }

        if (cameraInputLocked)
            return;

        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;

        horizontalRotation += mouseX;
        verticalRotation -= mouseY;

        horizontalRotation = Mathf.Clamp(horizontalRotation, minHorizontalAngle, maxHorizontalAngle);
        verticalRotation = Mathf.Clamp(verticalRotation, minVerticalAngle, maxVerticalAngle);
    }

    void ApplyCameraRotation()
    {
        if (playerCamera == null)
            return;

        playerCamera.transform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
    }

    void MaintainCursorLock()
    {
        if (!enforceCursorLock)
            return;

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        if (Cursor.visible)
        {
            Cursor.visible = false;
        }
    }

    void ReleaseForcedLook()
    {
        cameraInputLocked = false;
        cameraManualUnlockRequired = false;
        cameraUnlockTime = 0f;

        if (forcedLookRoutine != null)
        {
            StopCoroutine(forcedLookRoutine);
            forcedLookRoutine = null;
        }
    }

    public void ForceLookAtPoint(Vector3 worldPoint, float blendDuration, float holdDuration, bool lockInput, bool manualUnlock = false)
    {
        if (playerCamera == null)
            return;

        Vector3 worldDirection = worldPoint - playerCamera.transform.position;
        if (worldDirection.sqrMagnitude < 0.0001f)
            return;

        Transform pivot = playerCamera.transform.parent != null ? playerCamera.transform.parent : playerCamera.transform;
        Quaternion targetWorldRotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
        Quaternion targetLocalRotation = pivot != playerCamera.transform
            ? Quaternion.Inverse(pivot.rotation) * targetWorldRotation
            : targetWorldRotation;

        Vector3 euler = targetLocalRotation.eulerAngles;
        float targetVertical = Mathf.Clamp(NormalizeAngle(euler.x), minVerticalAngle, maxVerticalAngle);
        float targetHorizontal = Mathf.Clamp(NormalizeAngle(euler.y), minHorizontalAngle, maxHorizontalAngle);

        if (forcedLookRoutine != null)
        {
            StopCoroutine(forcedLookRoutine);
        }

        forcedLookRoutine = StartCoroutine(BlendCameraRotation(targetVertical, targetHorizontal, Mathf.Max(0f, blendDuration)));

        if (lockInput)
        {
            cameraInputLocked = true;
            cameraManualUnlockRequired = manualUnlock;
            cameraUnlockTime = manualUnlock
                ? float.PositiveInfinity
                : Time.time + Mathf.Max(holdDuration, forcedLookHoldGrace);
        }
    }

    IEnumerator BlendCameraRotation(float targetVertical, float targetHorizontal, float duration)
    {
        float startVertical = verticalRotation;
        float startHorizontal = horizontalRotation;

        if (duration <= 0f)
        {
            verticalRotation = targetVertical;
            horizontalRotation = targetHorizontal;
            ApplyCameraRotation();
            forcedLookRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            verticalRotation = Mathf.LerpAngle(startVertical, targetVertical, t);
            horizontalRotation = Mathf.LerpAngle(startHorizontal, targetHorizontal, t);
            ApplyCameraRotation();
            elapsed += Time.deltaTime;
            yield return null;
        }

        verticalRotation = targetVertical;
        horizontalRotation = targetHorizontal;
        ApplyCameraRotation();
        forcedLookRoutine = null;
    }

    static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
        {
            angle -= 360f;
        }
        else if (angle < -180f)
        {
            angle += 360f;
        }
        return angle;
    }

    void ApplyVoteSelectionCameraLock(ulong targetClientId, PlayerController targetController)
    {
        if (targetClientId == PlayerActionsSync.NoTarget)
        {
            ReleaseSelectionCameraLock();
            return;
        }

        if (TryGetSelectionFocusPoint(targetClientId, targetController, out var focusPoint))
        {
            ForceLookAtPoint(focusPoint, forcedLookBlendDuration, 0f, true, true);
        }
        else
        {
            cameraInputLocked = true;
            cameraManualUnlockRequired = true;
            cameraUnlockTime = float.PositiveInfinity;
        }
    }

    void ReleaseSelectionCameraLock()
    {
        if (cameraManualUnlockRequired)
        {
            ReleaseForcedLook();
        }
    }

    bool TryGetSelectionFocusPoint(ulong clientId, PlayerController controller, out Vector3 focusPoint)
    {
        if (controller != null)
        {
            focusPoint = controller.transform.position + Vector3.up * voteSelectionFocusHeightOffset;
            return true;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
        {
            focusPoint = client.PlayerObject.transform.position + Vector3.up * voteSelectionFocusHeightOffset;
            return true;
        }

        focusPoint = Vector3.zero;
        return false;
    }

    PlayerController ResolvePlayerController(ulong clientId)
    {
        if (clientId == PlayerActionsSync.NoTarget)
            return null;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
        {
            return client.PlayerObject.GetComponent<PlayerController>();
        }

        return null;
    }

    void HandleVotingSelection()
    {
        if (isGhost)
            return;

        if (RoundManager.Instance == null || RoundManager.Instance.currentState.Value != RoundManager.RoundState.Voting)
            return;

        PlayerCollider targetCollider = GetCurrentVoteTarget();

        bool mousePressed = Input.GetMouseButtonDown(0);
        bool mouseHeld = Input.GetMouseButton(0);
        bool hasSelection = currentVoteTarget != PlayerActionsSync.NoTarget;

        if (mousePressed && cameraInputLocked && cameraManualUnlockRequired && hasSelection)
        {
            ClearVoteSelection(true);
            return;
        }

        if ((mousePressed || mouseHeld) && targetCollider != null && targetCollider.OwnerController != null && !targetCollider.OwnerController.IsGhost && !cameraInputLocked)
        {
            ulong targetClientId = targetCollider.OwnerController.OwnerClientId;
            if (targetClientId != OwnerClientId)
            {
                SelectVoteTarget(targetClientId);
            }
        }
        else if (mousePressed && !cameraInputLocked)
        {
            ClearVoteSelection(true);
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

        ApplyVoteSelectionCameraLock(targetClientId, ResolvePlayerController(targetClientId));
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

        ReleaseSelectionCameraLock();
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
            UIGameplayManager.Instance?.SetLocalVoteSelection("You can't vote");
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

        if (newValue == PlayerActionsSync.NoTarget)
        {
            ReleaseSelectionCameraLock();
        }
        else
        {
            ApplyVoteSelectionCameraLock(newValue, ResolvePlayerController(newValue));
        }
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
        ReleaseForcedLook();

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








