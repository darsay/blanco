using TMPro;
using UnityEngine;

public class ClassNamePanel : MonoBehaviour
{
    [SerializeField]
    TextMeshProUGUI playerNameText;

    public void SetPanel(ClassDataBack classData)
    {
        playerNameText.text = classData.name;
    }
}
