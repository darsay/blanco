using TMPro;
using UnityEngine;

public class CardController : MonoBehaviour
{
    [SerializeField]
    TextMeshProUGUI cardText;

    public void SetWord(string word)
    {
        cardText.text = word;
    }
}
