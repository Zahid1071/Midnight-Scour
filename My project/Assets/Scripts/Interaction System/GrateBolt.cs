using UnityEngine;

public class GrateBolt : MonoBehaviour, IInteractable
{
    [SerializeField] GrateSystem grate;
    [SerializeField] InventoryItem wrench;

    bool _removed;

    public void Interact()
    {
        if (_removed || grate == null)
            return;

        if (wrench != null)
        {
            InventoryItem selected = InventoryUI.Instance?.GetSelectedItem();
            if (selected != wrench)
            {
                HudManager.Instance?.ShowSubtitle("It won't budge.", 3f);
                return;
            }
        }

        _removed = true;
        grate.NotifyBoltRemoved();
        HudManager.Instance?.ShowSubtitle("Bolt removed.", 2.5f, HudManager.Instance.SuccessColor);
        gameObject.SetActive(false);
    }
}
