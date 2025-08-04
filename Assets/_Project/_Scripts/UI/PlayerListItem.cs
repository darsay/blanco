using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Blanco.UI
{
    public class PlayerListItem : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private TextMeshProUGUI playerStatusText;
        [SerializeField] private Image playerIcon;
        [SerializeField] private Color hostColor = Color.yellow;
        [SerializeField] private Color playerColor = Color.white;
        
        public void SetPlayerInfo(Blanco.Networking.LobbyManager.PlayerInfo playerInfo)
        {
            if (playerNameText != null)
            {
                string playerName = playerInfo.playerName.ToString();
                string hostText = playerInfo.isHost ? " (Host)" : "";
                playerNameText.text = $"{playerName}{hostText}";
            }
            
            if (playerStatusText != null)
            {
                string status = playerInfo.isReady ? "Listo" : "No listo";
                playerStatusText.text = status;
            }
            
            if (playerIcon != null)
            {
                playerIcon.color = playerInfo.isHost ? hostColor : playerColor;
            }
        }
    }
} 