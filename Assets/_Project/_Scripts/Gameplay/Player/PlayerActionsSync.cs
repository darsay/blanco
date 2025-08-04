using Unity.Netcode;
using UnityEngine;

public class PlayerActionsSync : NetworkBehaviour
{
    public NetworkVariable<Vector3> cameraForward = new(writePerm: NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> isPlayerPointing = new(writePerm: NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> isPlayerCheckingCard = new(writePerm: NetworkVariableWritePermission.Owner);

    [SerializeField] private Transform cameraRoot;


    void Update()
    {
        if (!IsOwner) return;

        cameraForward.Value = cameraRoot.forward;
    }
}
