using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The strip that says what the reader is walking to, and how to stop.
///
/// One of these lives on the map and another over the camera. Both read the same
/// static <see cref="GuidanceService"/>, which is what makes "Guide me there"
/// survive the switch between them - the claim the first prototype made and did
/// not keep.
/// </summary>
public class GuidanceBanner : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;

    [Tooltip("The strip itself. Hidden when nothing is being guided to.")]
    public GameObject root;

    [Header("Parts")]
    public BearingArrow arrow;
    public Image colourDot;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI distanceText;
    public Button stopButton;
    public TextMeshProUGUI stopLabel;

    float _timer;

    void Awake()
    {
        if (stopButton != null)
            stopButton.onClick.AddListener(GuidanceService.Stop);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        GuidanceService.OnGuidanceChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;

        Refresh();
    }

    void OnDisable()
    {
        GuidanceService.OnGuidanceChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
    }

    void Update()
    {
        // The distance is the only part that changes as the reader walks, and once
        // a second is as often as a number in metres is worth redrawing.
        _timer += Time.unscaledDeltaTime;
        if (_timer < 0.5f)
            return;

        _timer = 0f;
        RefreshDistance();
    }

    public void Refresh()
    {
        BuildingData target = GuidanceService.Target;

        if (root != null)
            root.SetActive(target != null);

        if (target == null)
            return;

        if (nameText != null)
            nameText.text = target.DisplayName;

        if (colourDot != null)
            colourDot.color = MarkerPalette.Resolve(target);

        if (stopLabel != null)
            stopLabel.text = AppStrings.Get("guide.stop");

        if (arrow != null)
            arrow.SetTarget(target);

        RefreshDistance();
    }

    void RefreshDistance()
    {
        if (distanceText == null)
            return;

        BuildingData target = GuidanceService.Target;
        if (target == null)
            return;

        if (locationService == null)
            locationService = UserLocationService.Instance;

        double metres = locationService != null
            ? locationService.DistanceMetresTo(target)
            : -1d;

        if (metres < 0d)
        {
            distanceText.text = AppStrings.Get("map.noLocation");
            return;
        }

        // Approximate whenever the fix is not a localized one, so a GPS-grade
        // number never reads as a surveyed one.
        bool approximate = locationService != null && locationService.IsApproximate;

        distanceText.text = metres < 15d
            ? AppStrings.Get("guide.arrived")
            : AppStrings.FormatDistance(metres, approximate);
    }
}
