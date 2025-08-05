using UnityEngine;

public class PlayerCollider : MonoBehaviour
{
    [SerializeField] private GameObject modelToChange;
    [SerializeField] private string highlightLayer = "Highlighted";

    private int originalLayer;
    private bool isHighlighted = false;

    public void Highlight(bool enable)
    {
        if (modelToChange == null) return;

        if (enable && !isHighlighted)
        {
            originalLayer = modelToChange.layer;
            modelToChange.layer = LayerMask.NameToLayer(highlightLayer);
            isHighlighted = true;
        }
        else if (!enable && isHighlighted)
        {
            modelToChange.layer = originalLayer;
            isHighlighted = false;
        }
    }
}
