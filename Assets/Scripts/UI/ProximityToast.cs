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

    /// <summary>
    /// Whether an alert is on screen right now.
    ///
    /// Asked by the map, which takes the next-stop strip away while this is up.
    /// A static, because the strip and this live in different top-level panels
    /// and neither has any business holding a reference to the other.
    /// </summary>
    public static bool IsShowing { get; private set; }

    void Awake()
    {
        if (openButton != null)
            openButton.onClick.AddListener(Open);

        if (dismissButton != null)
            dismissButton.onClick.AddListener(Dismiss);

        if (root != null)
            root.SetActive(false);

        IsShowing = false;
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        ProximityAlerts.OnAlert += Show;
        AppStrings.OnLanguageChanged += Render;
        BuildingDatabase.OnBuildingsChanged += DropIfGone;
    }

    /// <summary>
    /// A stop that is no longer on the map is not nearby. Signing out, deleting
    /// the account or switching trails left the last announcement standing,
    /// naming a stop from a trail the reader no longer had open.
    /// </summary>
    void DropIfGone()
    {
        if (_marker != null && BuildingDatabase.GetById(_marker.buildingId) == null)
            Dismiss();
    }

    void OnDisable()
    {
        // A static survives this object, so it is put back by hand. Left true,
        // the map would hide its strip forever after the last alert.
        IsShowing = false;

        ProximityAlerts.OnAlert -= Show;
        AppStrings.OnLanguageChanged -= Render;
        BuildingDatabase.OnBuildingsChanged -= DropIfGone;
    }

    /// <summary>
    /// A stop sheet or the capture bar is open. The card rests over the bottom of
    /// the screen, which is exactly where their buttons are: on the phone it sat
    /// on a sheet's Hide and Report, and on the capture bar's Save and Cancel.
    /// </summary>
    bool Covering()
    {
        return CaptureMode.IsOpen ||
               (infoPanel != null && infoPanel.sheet != null && infoPanel.sheet.IsOpen);
    }

    public void Show(BuildingData marker)
    {
        if (marker == null || card == null || Covering())
            return;

        _marker = marker;
        _shownAt = Time.unscaledTime;
        _leaving = false;

        Render();

        if (root != null)
            root.SetActive(true);

        IsShowing = true;
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

        if (_leaving)
            return;

        if (Covering())
        {
            Dismiss();
            return;
        }

        if (_sliding)
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
            IsShowing = false;
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
