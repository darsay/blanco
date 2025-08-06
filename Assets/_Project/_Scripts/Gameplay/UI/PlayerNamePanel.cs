using TMPro;
using UnityEngine;

public class PlayerNamePanel : MonoBehaviour
{
    [SerializeField]
    TextMeshProUGUI playerNameText;

    public void SetName(Blanco.Networking.LobbyManager.PlayerInfo player)
    {
        string displayName = player.playerName.ToString();
        if (player.isHost)
        {
            displayName += " (Host)";
        }
        
        playerNameText.text = displayName;

        if (player.isHost)
        {
            playerNameText.color = Color.yellow;
        }
        else
        {
            playerNameText.color = Color.white;
        }
    }
}
