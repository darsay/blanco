using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class NetworkLauncher : MonoBehaviour
{
    public Button hostButton;
    public Button clientButton;

    void Start()
    {
        hostButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartHost();
        });

        clientButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartClient();
        });
    }
}
