using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One marker in the trail graph: a numbered disc with its name beside it.
///
/// Filled for a marker already found, hollow for one still ahead - the same
/// language the strip on the map screen uses, so the two pictures of the same
/// walk agree with each other.
/// </summary>
public class TrailGraphNodeView : MonoBehaviour
{
    [Header("References")]
    public Button button;
    [Tooltip("The themed base. Never coloured from here - a ThemedElement owns " +
             "it, and hand-painting it as well is two things arguing over one " +
             "colour, which the theme loses whenever it repaints last.")]
    public Image disc;

    [Tooltip("The marker's own colour, laid over the base and shown only once " +
             "the marker has been found.")]
    public Image fill;

    public Image ring;
    public TextMeshProUGUI numberLabel;
    public TextMeshProUGUI nameLabel;
    public TextMeshProUGUI distanceLabel;

    Action _onClick;

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(Click);
    }

    void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(Click);
    }

    public void Bind(BuildingData data, int number, bool found, Action onClick)
    {
        _onClick = onClick;

        if (numberLabel != null)
            numberLabel.text = number.ToString();

        if (nameLabel != null)
            nameLabel.text = data != null ? data.DisplayName : string.Empty;

        if (distanceLabel != null)
        {
            // Hidden rather than zero when there is no fix. A graph that claims
            // every marker is nought metres away indoors reads as broken.
            UserLocationService here = UserLocationService.Instance;
            bool known = here != null && here.HasValidPose && data != null;

            distanceLabel.gameObject.SetActive(known);

            if (known)
                distanceLabel.text = AppStrings.FormatDistance(here.DistanceMetresTo(data));
        }

        // The marker's own colour, so a node and the pin on the map are
        // recognisably the same object rather than two lists of the same names.
        Color colour = MarkerPalette.Resolve(data);

        if (fill != null)
        {
            // Always its own colour, solid once found and a wash of it before.
            // Showing an unfound marker as the ring alone does not work: RingFor
            // picks whichever of black or white contrasts with the MARKER, so a
            // dark blue marker got a white ring and disappeared into a white
            // card. This way every node carries its colour whatever the theme.
            fill.color = found ? colour : new Color(colour.r, colour.g, colour.b, 0.22f);
            fill.gameObject.SetActive(true);
        }

        // The ring is what carries the separation, not the marker colour: a marker
        // with no colour of its own resolves to white, and a white ring around a
        // white disc on a white card is nothing at all. RingFor picks whichever
        // of black or white the marker is not, which is what the map pins do.
        if (ring != null)
            ring.color = MarkerPalette.RingFor(colour);

        if (numberLabel != null)
        {
            // On a found marker the number sits on the marker's colour; on one
            // still ahead it sits on the card, and those need different inks.
            // On a solid disc the number sits on the marker's colour; on a wash
            // of it, it sits on the card, and those need different inks.
            numberLabel.color = found
                ? MarkerPalette.OnColour(colour)
                : ThemeManager.Get(ThemeRole.TextPrimary);
        }
    }

    void Click()
    {
        if (_onClick != null)
            _onClick();
    }
}
