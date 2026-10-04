using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One marker on the map, in the three states Daylight v2 draws:
///
///   done   small, filled with the stop's colour, a tick on it
///   next   large, filled, its number on it, a soft halo, and the only name
///   later  hollow: the stop's colour as a ring, its number in ink
///
/// The bulb sits exactly on the coordinate and the name rides above it, because
/// a pin whose label is centred on the point lies about where the thing is by
/// half a label. Only the next stop is named - a name on every pin was a map of
/// black ovals, and the next stop is the one the reader is asking about.
/// </summary>
public class MapPinView : MonoBehaviour
{
    public enum State { Later, Next, Done }

    [Header("Parts")]
    public Button button;

    [Tooltip("The surface-coloured disc behind the bulb: what separates a marker " +
             "from the map, and the hollow pin's face.")]
    public Image ring;

    [Tooltip("The marker's own colour.")]
    public Image bulb;

    [Tooltip("The soft halo around the next stop.")]
    public Image halo;

    [Tooltip("Kept so older scenes load; the halo does its job now.")]
    public Image selection;

    public TextMeshProUGUI number;
    public Image tick;

    public TextMeshProUGUI label;
    public Image labelPlate;

    [Header("Sizes")]
    public float bulbSize = 26f;
    public float selectedBulbSize = 34f;

    BuildingData _building;
    System.Action<BuildingData> _onSelected;
    bool _selected;
    bool _labelAllowed = true;
    State _state = State.Later;

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

    void OnEnable()
    {
        // Repaint on the way in, not only on the event: a pin lives under the map
        // panel, which is switched off while Profile - where the theme is chosen -
        // is open, so it always misses the event itself.
        Refresh();
        ThemeManager.OnThemeChanged += Refresh;
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Refresh;
    }

    public void SetState(State state)
    {
        if (_state == state)
            return;

        _state = state;
        Refresh();
    }

    public void Refresh()
    {
        if (_building == null)
            return;

        Color colour = MarkerPalette.Resolve(_building);
        Color surface = ThemeManager.Get(ThemeRole.Surface);
        bool big = _state == State.Next || _selected;

        // Sizes in reference pixels: 36px for the next stop, 26 for one ahead,
        // 24 for one done, each with its surface ring.
        float outer = big ? 100f : _state == State.Done ? 66f : 72f;
        float border = big ? 8f : _state == State.Done ? 6f : 0f;

        if (ring != null)
        {
            ring.rectTransform.sizeDelta = new Vector2(outer, outer);
            ring.color = _state == State.Later && !big ? colour : surface;
        }

        if (bulb != null)
        {
            float inner = _state == State.Later && !big ? outer - 16f : outer - border * 2f;
            bulb.rectTransform.sizeDelta = new Vector2(inner, inner);
            bulb.color = _state == State.Later && !big ? surface : colour;
        }

        if (halo != null)
        {
            bool show = big;
            if (halo.gameObject.activeSelf != show)
                halo.gameObject.SetActive(show);

            Color wash = colour;
            wash.a = 0.28f;
            halo.color = wash;
            halo.rectTransform.sizeDelta = new Vector2(outer + 28f, outer + 28f);
        }

        if (selection != null && selection.gameObject.activeSelf)
            selection.gameObject.SetActive(false);

        bool done = _state == State.Done && !big;
        bool numbered = !done && _building.step > 0;

        if (number != null)
        {
            if (number.gameObject.activeSelf != numbered)
                number.gameObject.SetActive(numbered);

            if (numbered)
            {
                number.text = _building.step.ToString();
                number.fontSize = big ? 42f : 33f;
                number.color = _state == State.Later && !big
                    ? ThemeManager.Get(ThemeRole.TextPrimary)
                    : MarkerPalette.OnColour(colour);
            }
        }

        if (tick != null)
        {
            if (tick.gameObject.activeSelf != done)
                tick.gameObject.SetActive(done);

            tick.color = MarkerPalette.OnColour(colour);
        }

        if (label != null)
        {
            label.text = _building.DisplayName;
            label.color = ThemeManager.Get(ThemeRole.TextPrimary);
        }

        if (labelPlate != null)
            labelPlate.color = ThemeManager.Get(ThemeRole.Glass);

        ApplyLabel();
    }

    public void SetSelected(bool selected)
    {
        if (_selected == selected)
            return;

        _selected = selected;
        Refresh();
    }

    /// <summary>Kept for MapView, which still asks; only the next or chosen pin is named.</summary>
    public void SetLabelAllowed(bool allowed)
    {
        _labelAllowed = allowed;
        ApplyLabel();
    }

    void ApplyLabel()
    {
        if (label == null)
            return;

        bool show = _state == State.Next || _selected;
        GameObject plate = label.transform.parent.gameObject;

        if (plate.activeSelf != show)
            plate.SetActive(show);
    }

    /// <summary>
    /// Keeps the name on the screen. A stop near the edge had its label cut in
    /// half by it ("Test 29 0"); the plate slides in instead, still above its pin.
    /// </summary>
    void LateUpdate()
    {
        if (label == null)
            return;

        RectTransform plate = (RectTransform)label.transform.parent;
        if (!plate.gameObject.activeSelf)
            return;

        RectTransform me = (RectTransform)transform;
        RectTransform area = me.parent as RectTransform;
        if (area == null)
            return;

        float room = area.rect.width * 0.5f - 22f;
        float half = plate.rect.width * 0.5f;
        float x = me.anchoredPosition.x;
        float shift = Mathf.Clamp(0f, -room + half - x, room - half - x);

        Vector2 at = plate.anchoredPosition;
        if (!Mathf.Approximately(at.x, shift))
        {
            at.x = shift;
            plate.anchoredPosition = at;
        }
    }

    void Raise()
    {
        if (_onSelected != null)
            _onSelected(_building);
    }
}
