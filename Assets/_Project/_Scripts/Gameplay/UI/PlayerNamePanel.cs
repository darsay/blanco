using TMPro;
using UnityEngine;

public class PlayerNamePanel : MonoBehaviour
{
    [SerializeField]
    TextMeshProUGUI playerNameText;

    public void SetName(string name)
    {
        playerNameText.text = name;
    }
}
