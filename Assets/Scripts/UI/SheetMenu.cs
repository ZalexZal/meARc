using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ⋯ menu at the foot of a stop: the things a reader does to a stop rather
/// than with it - learn more, hide it, move it, report it.
///
/// Daylight v2 took these out of a row of three equal grey buttons under every
/// stop, where "Report" sat beside "Hide" at the same weight as the action the
/// sheet was for. Here they are one tap further away and say what they are, and
/// the footer is left with the one thing to do next.
///
/// It opens upward from the button, grows out of its corner, and any tap outside
/// it - or the phone's back gesture - puts it away. Choosing an item closes it.
/// </summary>
public class SheetMenu : MonoBehaviour
{
    [Tooltip("Full-screen and invisible: a tap anywhere else closes the menu.")]
    public Button catcher;
    public RectTransform panel;
    public Button opener;

    [Tooltip("Every item. Each closes the menu on its way to doing its own thing.")]
    public Button[] items;

    float _t;
    bool _open;

    void Awake()
    {
        if (catcher != null)
            catcher.onClick.AddListener(Close);

        if (opener != null)
            opener.onClick.AddListener(Open);

        if (items != null)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null)
                    items[i].onClick.AddListener(Close);
            }
        }

        Set(false);
    }

    public void Open()
    {
        if (panel == null)
            return;

        _open = true;
        _t = 0f;
        Set(true);
        Haptics.Tick();
    }

    public void Close()
    {
        if (!_open)
            return;

        _open = false;
        Set(false);
    }

    void Set(bool on)
    {
        if (catcher != null && catcher.gameObject.activeSelf != on)
            catcher.gameObject.SetActive(on);

        if (panel != null && panel.gameObject.activeSelf != on)
            panel.gameObject.SetActive(on);

        if (panel != null)
            panel.localScale = on ? new Vector3(0.9f, 0.9f, 1f) : Vector3.one;
    }

    void Update()
    {
        if (!_open || panel == null)
            return;

        // Grows from 90% to full over a quarter of a second, eased out.
        _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / 0.25f);
        float k = 1f - Mathf.Pow(1f - _t, 3f);
        float s = Mathf.Lerp(0.9f, 1f, k);
        panel.localScale = new Vector3(s, s, 1f);
    }
}
