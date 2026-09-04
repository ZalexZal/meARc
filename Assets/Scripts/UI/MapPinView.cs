using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One marker on the map.
///
/// The bulb sits exactly on the coordinate and the label rides above it, because a
/// pin whose label is centred on the point is a pin that lies about where the
/// thing is by half a label.
/// </summary>
public class MapPinView : MonoBehaviour
{
    [Header("Parts")]
    public Button button;

    [Tooltip("Ring behind the bulb. This is what separates a marker from the map " +
             "and from the accent when the trail picked the same colour.")]
    public Image ring;

    [Tooltip("The marker's own colour.")]
    public Image bulb;

    [Tooltip("Grows when this pin is the selected one, so selection reads without " +
             "relying on colour alone.")]
    public Image selection;

    public TextMeshProUGUI label;
    public Image labelPlate;

    [Header("Sizes")]
    public float bulbSize = 26f;
    public float selectedBulbSize = 34f;

    BuildingData _building;
    System.Action<BuildingData> _onSelected;
    bool _selected;
    bool _labelAllowed = true;

    public BuildingData Building { get { return _building; } }

    public void Bind(BuildingData building, System.Action<BuildingData> onSelected)
    {
        _building = building;
        _onSelected = onSelected;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Raise);
        }

        Refresh();
    }

    /// <summary>Re-reads colour and name, e.g. after a language or theme change.</summary>
    void OnEnable()
    {
        // Repaint on the way in, not only on the event.
        //
        // This is the asymmetry the plate bug was really made of. ThemedElement
        // calls Apply() in OnEnable and then subscribes; this only subscribed.
        // A pin lives under MapPanel, which is switched off while Settings - the
        // one screen with the theme toggle on it - is open, so the pin is always
        // disabled at the exact moment the theme changes and always misses the
        // event. Subscribing to a message you are guaranteed not to be there for
        // is not a subscription.
        //
        // It costs a name and a colour per pin and it also picks up a language
        // change, which arrives the same way and through the same closed door.
        Refresh();
        ThemeManager.OnThemeChanged += Refresh;
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Refresh;
    }

    /// <summary>
    /// Repaints the pin.
    ///
    /// Still subscribed to the theme, though the plate and the label no longer
    /// move with it - the marker's own colour and its ring do, because RingFor
    /// picks whichever of black or white the marker is not.
    ///
    /// The plate used to be painted here from Surface and was the thing that
    /// broke: UIFactory.Plain bakes the colour it is handed and attaches no
    /// ThemedElement, so the prefab froze at whatever the editor's theme was
    /// when it was written - #1A1F27, the dark Surface - while the label beside
    /// it did follow the theme, because UIFactory.Text does attach one. Dark
    /// text on a dark plate, in the light theme only.
    /// </summary>
    public void Refresh()
    {
        if (_building == null)
            return;

        Color colour = MarkerPalette.Resolve(_building);

        if (bulb != null)
            bulb.color = colour;

        if (ring != null)
            ring.color = MarkerPalette.RingFor(colour);

        // Against the map, not against the chrome. See MapInk: Surface and
        // TextPrimary made a white plate carrying near-black text sit on a cream
        // map among its own near-black labels, which is a name nobody can find.
        if (label != null)
        {
            label.text = _building.DisplayName;
            label.color = MapInk.OnPlate;
        }

        if (labelPlate != null)
            labelPlate.color = MapInk.Plate;
    }

    public void SetSelected(bool selected)
    {
        if (selection != null)
        {
            selection.gameObject.SetActive(selected);
            selection.color = ThemeManager.Get(ThemeRole.Accent);
        }

        if (bulb != null)
        {
            float size = selected ? selectedBulbSize : bulbSize;
            bulb.rectTransform.sizeDelta = new Vector2(size, size);

            if (ring != null)
                ring.rectTransform.sizeDelta = new Vector2(size + 8f, size + 8f);
        }

        _selected = selected;
        ApplyLabel();
    }

    /// <summary>
    /// Whether this pin may show its name.
    ///
    /// The map turns labels off once there are enough pins that they would overlap,
    /// because two names crossing each other are worse than none - but the selected
    /// pin and the one being walked to always keep theirs, since those are the two
    /// the reader is actually asking about.
    /// </summary>
    public void SetLabelAllowed(bool allowed)
    {
        _labelAllowed = allowed;
        ApplyLabel();
    }

    void ApplyLabel()
    {
        if (label == null)
            return;

        label.transform.parent.gameObject.SetActive(_selected || _labelAllowed);
    }

    void Raise()
    {
        if (_onSelected != null)
            _onSelected(_building);
    }
}
