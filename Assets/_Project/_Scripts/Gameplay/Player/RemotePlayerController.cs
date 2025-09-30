using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class RemotePlayerController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform torsoRotator;
    [SerializeField] private Transform torsoTarget;
    [SerializeField] private PlayerActionsSync actionsSync;
    [SerializeField] private Rig rightHandRig;
    [SerializeField] private Rig cardHandRig;
    [SerializeField] private GameObject revolver;

    [Header("Voting Feedback")]
    [SerializeField] private Animator revolverAnimator;
    [SerializeField] private ParticleSystem successfulShotVfx;
    [SerializeField] private ParticleSystem jamVfx;
    [SerializeField] private AudioSource successfulShotAudio;
    [SerializeField] private AudioSource jamAudio;
    [SerializeField] private string shootTrigger = "Shoot";
    [SerializeField] private string jamTrigger = "Jam";

    private bool feedbackRefsValidated;
    private bool isGhost;

    private void Awake()
    {
        if (rightHandRig != null)
        {
            rightHandRig.weight = 0f;
        }

        if (cardHandRig != null)
        {
            cardHandRig.weight = 0f;
        }
    }

    private void OnEnable()
    {
        feedbackRefsValidated = false;
        ValidateFeedbackReferences();

        if (!IsOwner && actionsSync != null)
        {
            actionsSync.isPlayerPointing.OnValueChanged += OnPointingChanged;
            actionsSync.isPlayerCheckingCard.OnValueChanged += OnCheckingCardChanged;
            actionsSync.isPlayerAiming.OnValueChanged += OnPlayerAiming;
            actionsSync.voteOutcome.OnValueChanged += OnVoteOutcomeChanged;
        }
    }

    private void OnDisable()
    {
        if (!IsOwner && actionsSync != null)
        {
            actionsSync.isPlayerPointing.OnValueChanged -= OnPointingChanged;
            actionsSync.isPlayerCheckingCard.OnValueChanged -= OnCheckingCardChanged;
            actionsSync.isPlayerAiming.OnValueChanged -= OnPlayerAiming;
            actionsSync.voteOutcome.OnValueChanged -= OnVoteOutcomeChanged;
        }
    }

    void Update()
    {
        if (IsOwner || actionsSync == null || isGhost)
            return;

        Vector3 direction = actionsSync.cameraForward.Value;
        if (direction == Vector3.zero)
            return;

        if (torsoRotator != null)
        {
            torsoRotator.forward = direction;

            if (torsoTarget != null)
            {
                torsoTarget.position = torsoRotator.position + direction * 2f;
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
            Debug.LogWarning($"[{nameof(RemotePlayerController)}] Missing revolver animator on {gameObject.name}.", this);
        }

        if (successfulShotVfx == null)
        {
            Debug.LogWarning($"[{nameof(RemotePlayerController)}] Missing successful shot VFX on {gameObject.name}.", this);
        }

        if (jamVfx == null)
        {
            Debug.LogWarning($"[{nameof(RemotePlayerController)}] Missing jam VFX on {gameObject.name}.", this);
        }

        if (successfulShotAudio == null)
        {
            Debug.LogWarning($"[{nameof(RemotePlayerController)}] Missing successful shot AudioSource on {gameObject.name}.", this);
        }

        if (jamAudio == null)
        {
            Debug.LogWarning($"[{nameof(RemotePlayerController)}] Missing jam AudioSource on {gameObject.name}.", this);
        }

        if (revolverAnimator != null && string.IsNullOrEmpty(shootTrigger))
        {
            Debug.LogWarning($"[{nameof(RemotePlayerController)}] Shoot trigger name is empty on {gameObject.name}.", this);
        }

        if (revolverAnimator != null && string.IsNullOrEmpty(jamTrigger))
        {
            Debug.LogWarning($"[{nameof(RemotePlayerController)}] Jam trigger name is empty on {gameObject.name}.", this);
        }
    }

    public void SetGhostState(bool ghost)
    {
        if (isGhost == ghost)
            return;

        isGhost = ghost;

        if (revolver != null)
        {
            revolver.SetActive(!ghost);
        }

        if (rightHandRig != null)
        {
            rightHandRig.weight = 0f;
        }

        if (cardHandRig != null)
        {
            cardHandRig.weight = 0f;
        }

        if (successfulShotVfx != null)
        {
            successfulShotVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (jamVfx != null)
        {
            jamVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnPointingChanged(bool previousValue, bool newValue)
    {
        if (isGhost)
            return;

        float targetWeight = newValue ? 1f : 0f;

        if (rightHandRig != null)
        {
            DOTween.To(() => rightHandRig.weight, x => rightHandRig.weight = x, targetWeight, 0.3f);
        }
    }

    private void OnCheckingCardChanged(bool previousValue, bool newValue)
    {
        if (isGhost)
            return;

        float targetWeight = newValue ? 1f : 0f;

        if (cardHandRig != null)
        {
            DOTween.To(() => cardHandRig.weight, x => cardHandRig.weight = x, targetWeight, 0.3f);
        }
    }

    private void OnPlayerAiming(bool previousValue, bool newValue)
    {
        if (isGhost)
            return;

        if (revolver != null)
        {
            revolver.SetActive(newValue);
        }

        float targetWeight = newValue ? 1f : 0f;

        if (rightHandRig != null)
        {
            DOTween.To(() => rightHandRig.weight, x => rightHandRig.weight = x, targetWeight, 0.3f);
        }
    }

    private void OnVoteOutcomeChanged(PlayerActionsSync.VoteOutcome previousValue, PlayerActionsSync.VoteOutcome newValue)
    {
        if (newValue == PlayerActionsSync.VoteOutcome.None || isGhost)
            return;

        bool success = newValue == PlayerActionsSync.VoteOutcome.Success;
        bool isTie = newValue == PlayerActionsSync.VoteOutcome.Tie;
        PlayVotingResult(success, isTie);
    }

    private void PlayVotingResult(bool success, bool isTie)
    {
        if (isGhost)
            return;

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
        else if (!isTie && jamAudio != null)
        {
            jamAudio.Play();
        }
    }
}
