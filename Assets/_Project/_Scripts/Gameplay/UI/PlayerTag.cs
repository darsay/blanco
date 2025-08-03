using TMPro;
using UnityEngine;

public class PlayerTag : MonoBehaviour
{
    [SerializeField]
    TextMeshProUGUI playerNameText;

    [SerializeField]
    LookAtCamera lookAtCamera;



    public void SetName(string playerName)
    {
        if (playerNameText != null)
        {
            playerNameText.text = playerName;
        }

        SetTargetCamera();
    }

    public void SetTargetCamera()
    {
        if (lookAtCamera != null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                lookAtCamera.SetCamera(mainCamera);
            }
        }
    }
}
