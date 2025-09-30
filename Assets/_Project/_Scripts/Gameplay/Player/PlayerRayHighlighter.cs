using UnityEngine;

public class PlayerRayHighlighter : MonoBehaviour
{
    [SerializeField] private float maxDistance = 100f;
    [SerializeField] private Camera cam;

    private PlayerCollider lastTarget;

    public PlayerCollider CurrentTarget => lastTarget;

    void Update()
    {
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxDistance))
        {
            var target = hit.collider.GetComponentInParent<PlayerCollider>();

            if (target != null)
            {
                if (target.OwnerController != null && target.OwnerController.IsGhost)
                {
                    RestorePrevious();
                    return;
                }

                if (target != lastTarget)
                {
                    RestorePrevious();

                    lastTarget = target;
                    target.Highlight(true);
                }
                return;
            }
        }

        RestorePrevious();
    }

    private void OnDisable()
    {
        RestorePrevious();
    }

    void RestorePrevious()
    {
        if (lastTarget != null)
        {
            lastTarget.Highlight(false);
            lastTarget = null;
        }
    }

    public void ClearCurrentTarget()
    {
        RestorePrevious();
    }
}
