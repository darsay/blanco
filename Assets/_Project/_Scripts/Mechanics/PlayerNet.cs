using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerNet : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> playerName = new();
    public NetworkVariable<FixedString32Bytes> assignedWord = new NetworkVariable<FixedString32Bytes>(
        "", NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Enviar nombre al servidor al empezar
            string nombre = "Jugador " + OwnerClientId;
            EnviarNombreServerRpc(nombre);
            UIManagerNet.Instance.MostrarCarta(assignedWord.Value);
        }

        if (IsClient)
        {
            playerName.OnValueChanged += OnNameChanged;
        }
    }

    void OnNameChanged(FixedString32Bytes anterior, FixedString32Bytes nuevo)
    {
        Debug.Log($"Nombre actualizado: {nuevo}");
    }

    [ServerRpc]
    void EnviarNombreServerRpc(string nombre)
    {
        playerName.Value = nombre;
    }

    public string GetNombre()
    {
        return playerName.Value.ToString();
    }
}

