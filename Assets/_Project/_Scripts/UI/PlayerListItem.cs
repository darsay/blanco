using UnityEngine;
using TMPro;
using Unity.Netcode;

namespace Blanco.UI
{
    public class PlayerListItem : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private TextMeshProUGUI playerIdText;
        [SerializeField] private TextMeshProUGUI playerStatusText;
        
        public void SetPlayerInfo(Blanco.Networking.LobbyManager.PlayerInfo playerInfo)
        {
            if (playerNameText != null)
            {
                playerNameText.text = playerInfo.playerName.ToString();
            }
            
            if (playerIdText != null)
            {
                playerIdText.text = $"ID: {playerInfo.clientId}";
            }
            
            if (playerStatusText != null)
            {
                string status = playerInfo.isReady ? "✅ Listo" : "⏳ Esperando";
                if (playerInfo.isHost)
                {
                    status += " (Host)";
                }
                playerStatusText.text = status;
            }
            
            // Color diferente para el host
            if (playerInfo.isHost)
            {
                if (playerNameText != null)
                    playerNameText.color = Color.yellow;
                if (playerIdText != null)
                    playerIdText.color = Color.yellow;
                if (playerStatusText != null)
                    playerStatusText.color = Color.yellow;
            }
            else
            {
                if (playerNameText != null)
                    playerNameText.color = Color.white;
                if (playerIdText != null)
                    playerIdText.color = Color.white;
                if (playerStatusText != null)
                    playerStatusText.color = Color.white;
            }
        }
    }
} 