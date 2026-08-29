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
    public void Refresh()
    {
        if (_building == null)
            return;

        Color colour = MarkerPalette.Resolve(_building);

        if (bulb != null)
            bulb.color = colour;

        if (ring != null)
            ring.color = MarkerPalette.RingFor(colour);

        if (label != null)
        {
            label.text = _building.DisplayName;
            label.color = ThemeManager.Get(ThemeRole.TextPrimary);
        }

        if (labelPlate != null)
            labelPlate.color = ThemeManager.Get(ThemeRole.Surface);
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
