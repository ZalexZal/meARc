using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows or hides a sheet page's back button. The button sits in a slot with a
/// CanvasGroup so the title stays centred whichever way it goes.
/// </summary>
public static class PageBack
{
    public static void Show(Button back, bool show)
    {
        if (back == null)
            return;

        CanvasGroup slot = back.transform.parent != null
            ? back.transform.parent.GetComponent<CanvasGroup>()
            : null;

        if (slot == null)
        {
            back.gameObject.SetActive(show);
            return;
        }

        slot.alpha = show ? 1f : 0f;
        slot.interactable = show;
        slot.blocksRaycasts = show;
    }
}
