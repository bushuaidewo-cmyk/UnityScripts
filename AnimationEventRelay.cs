using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    private PlayerController playerController;

    private void Awake()
    {
        // 找到父物体上的 PlayerController
        playerController = GetComponentInParent<PlayerController>();
    }

    // ========== Duck / GetUp ==========
    public void OnGetUpStart()
    {
        if (playerController != null)
            playerController.OnGetUpStart();
    }

    public void OnGetUpEnd()
    {
        if (playerController != null)
            playerController.OnGetUpEnd();
    }

    public void OnDuckCancelable()
    {
        if (playerController != null)
            playerController.OnDuckCancelable();
    }

}


