using UnityEngine;

public class GrateBolt : MonoBehaviour, IInteractable
{
    [SerializeField] GrateSystem grate;
    [SerializeField] InventoryItem wrench;

    bool _removed;

    public bool Interact()
    {
        if (_removed || grate == null)
            return false;

        if (wrench != null)
        {
            InventoryItem selected = InventoryUI.Instance?.GetSelectedItem();
            if (selected != wrench)
            {
                InteractionFeedback.Fail("It won't budge.", 3f);
                return false;
            }
        }

        _removed = true;
        grate.NotifyBoltRemoved(transform.position);
        HudManager.Instance?.ShowSubtitle("Bolt removed.", 2.5f, HudManager.Instance.SuccessColor);
        gameObject.SetActive(false);
        return true;
    }
}
