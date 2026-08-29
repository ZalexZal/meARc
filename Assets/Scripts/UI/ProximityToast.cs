using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The card that slides in when a marker comes into range.
///
/// It carries an action, because an alert that only informs is an interruption:
/// tapping it opens the marker, which is where Guide me there lives.
///
/// It sits at the Canvas root so it appears on any tab, and below the marker sheet
/// in draw order so it can never cover something the reader opened deliberately.
///
/// This component is on a holder that is always active, and only its card is
/// switched off. A component that deactivates its own object stops receiving
/// OnEnable, and this one subscribes to the alert there.
/// </summary>
public class ProximityToast : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;
    public InfoPanelController infoPanel;

    [Header("Parts")]
    [Tooltip("The card wrapper, not this object. See the note on this class.")]
    public GameObject root;
    public RectTransform card;
    public Button openButton;
    public Image colourDot;
    public Image colourRing;
    public TextMeshProUGUI captionText;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI distanceText;
    public Button dismissButton;

    [Header("Behaviour")]
    [Tooltip("How long it stays before retiring itself. Long enough to read while " +
             "walking, short enough not to sit on the map.")]
    public float secondsOnScreen = 9f;

    [Tooltip("Where the card rests, measured from the bottom of the screen.")]
    public float restY = 230f;

    public float slideDistance = 220f;
    public float slideSeconds = 0.22f;

    BuildingData _marker;
    float _shownAt;
    float _slideStart;
    bool _sliding;
    bool _leaving;

    void Awake()
    {
        if (openButton != null)
            openButton.onClick.AddListener(Open);

        if (dismissButton != null)
            dismissButton.onClick.AddListener(Dismiss);

        if (root != null)
            root.SetActive(false);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        ProximityAlerts.OnAlert += Show;
        AppStrings.OnLanguageChanged += Render;
    }

    void OnDisable()
    {
        ProximityAlerts.OnAlert -= Show;
        AppStrings.OnLanguageChanged -= Render;
    }

    public void Show(BuildingData marker)
    {
        if (marker == null || card == null)
            return;

        _marker = marker;
        _shownAt = Time.unscaledTime;
        _leaving = false;

        Render();

        if (root != null)
            root.SetActive(true);

        _sliding = true;
        _slideStart = Time.unscaledTime;
        card.anchoredPosition = new Vector2(0f, restY - slideDistance);
    }

    public void Dismiss()
    {
        if (_leaving || root == null || !root.activeSelf)
            return;

        _leaving = true;
        _sliding = true;
        _slideStart = Time.unscaledTime;
    }

    void Update()
    {
        if (root == null || !root.activeSelf)
            return;

        Slide();

        if (_leaving || _sliding)
            return;

        RefreshDistance();

        if (Time.unscaledTime - _shownAt > secondsOnScreen)
            Dismiss();
    }

    void Slide()
    {
        if (!_sliding || card == null)
            return;

        float t = slideSeconds <= 0f
            ? 1f
            : Mathf.Clamp01((Time.unscaledTime - _slideStart) / slideSeconds);

        // Eased out on the way in and in on the way out, so arriving feels like
        // settling and leaving feels like a decision.
        float eased = _leaving ? t * t : 1f - (1f - t) * (1f - t);

        float from = _leaving ? restY : restY - slideDistance;
        float to = _leaving ? restY - slideDistance : restY;

        card.anchoredPosition = new Vector2(0f, Mathf.Lerp(from, to, eased));

        if (t < 1f)
            return;

        _sliding = false;

        if (_leaving)
        {
            root.SetActive(false);
            _marker = null;
        }
    }

    void Render()
    {
        if (_marker == null)
            return;

        Color colour = MarkerPalette.Resolve(_marker);

        if (colourDot != null)
            colourDot.color = colour;

        if (colourRing != null)
            colourRing.color = MarkerPalette.RingFor(colour);

        if (captionText != null)
            captionText.text = AppStrings.Get("alert.caption");

        if (nameText != null)
            nameText.text = _marker.DisplayName;

        RefreshDistance();
    }

    void RefreshDistance()
    {
        if (distanceText == null || _marker == null)
            return;

        if (locationService == null)
            locationService = UserLocationService.Instance;

        double metres = locationService != null
            ? locationService.DistanceMetresTo(_marker)
            : -1d;

        distanceText.text = metres < 0d
            ? AppStrings.Get("map.noLocation")
            : AppStrings.FormatDistance(metres,
                locationService != null && locationService.IsApproximate);
    }

    void Open()
    {
        BuildingData marker = _marker;
        Dismiss();

        if (infoPanel != null && marker != null)
            infoPanel.ShowPanel(marker);
    }
}
