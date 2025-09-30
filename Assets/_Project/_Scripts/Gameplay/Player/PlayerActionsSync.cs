using Unity.Netcode;
using UnityEngine;

public class PlayerActionsSync : NetworkBehaviour
{
    public const ulong NoTarget = ulong.MaxValue;

    public enum VoteOutcome : byte
    {
        None,
        Success,
        Fail,
        Tie,
        NoVote
    }

    public NetworkVariable<Vector3> cameraForward = new(writePerm: NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> isPlayerPointing = new(writePerm: NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> isPlayerCheckingCard = new(writePerm: NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> isPlayerAiming = new(writePerm: NetworkVariableWritePermission.Owner);
    public NetworkVariable<ulong> selectedVoteTarget = new(NoTarget, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<VoteOutcome> voteOutcome = new(VoteOutcome.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [SerializeField] private Transform cameraRoot;

    void Update()
    {
        if (!IsOwner) return;

        cameraForward.Value = cameraRoot.forward;
    }

    public void ResetVotingState()
    {
        if (IsOwner)
        {
            selectedVoteTarget.Value = NoTarget;
        }

        if (NetworkManager.Singleton.IsHost)
        {
            voteOutcome.Value = VoteOutcome.None;
        }
    }
}
