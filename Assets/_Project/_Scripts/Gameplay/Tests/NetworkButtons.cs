using Unity.Netcode;
using UnityEngine;

public class NetworkButtons : MonoBehaviour
{
    [SerializeField] private PlayerSpawner spawner;

    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();
        spawner.SpawnHostPlayer();
        transform.gameObject.SetActive(false);
    }

    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
        transform.gameObject.SetActive(false);
    }

    public void StartClient()
    {
        NetworkManager.Singleton.StartClient();
        transform.gameObject.SetActive(false);
    }


    // private void Awake() {
    //     GetComponent<UnityTransport>().SetDebugSimulatorParameters(
    //         packetDelay: 120,
    //         packetJitter: 5,
    //         dropRate: 3);
    // }
}
