using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens the Midnight Scour Steam store page. Put on Logo / Wishlist Banner (or call Open).
/// </summary>
public class SteamStoreLink : MonoBehaviour, IPointerClickHandler
{
    public const string DefaultStoreUrl =
        "https://store.steampowered.com/app/4725700/Midnight_Scour/?utm_source=demo&utm_medium=game&utm_campaign=in_game";

    [SerializeField] string storeUrl = DefaultStoreUrl;

    public void OnPointerClick(PointerEventData eventData) => Open();

    public void Open()
    {
        string url = string.IsNullOrWhiteSpace(storeUrl) ? DefaultStoreUrl : storeUrl.Trim();
        Application.OpenURL(url);
    }

    public static void OpenStore()
    {
        Application.OpenURL(DefaultStoreUrl);
    }

    /// <summary>Adds this component if missing and ensures the graphic can receive clicks.</summary>
    public static SteamStoreLink EnsureOn(GameObject target)
    {
        if (target == null)
            return null;

        var link = target.GetComponent<SteamStoreLink>();
        if (link == null)
            link = target.AddComponent<SteamStoreLink>();

        var graphic = target.GetComponent<UnityEngine.UI.Graphic>();
        if (graphic != null)
            graphic.raycastTarget = true;

        return link;
    }
}
