using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Nearby bottom sheet. Rebuilds its list when opened and refreshes distances
/// on the location service's own cadence while it stays open.
/// </summary>
public class NearbyPanelController : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;
    public SheetAnimator sheet;
    public RectTransform listParent;
    public BuildingRowView rowPrefab;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI emptyText;
    public Button closeButton;

    [Header("Dismiss")]
    [Tooltip("Transparent catcher behind the sheet, so tapping outside closes it.")]
    public GameObject scrim;
    public Button scrimButton;

    [Header("Behaviour")]
    [Tooltip("Buildings further than this are not considered nearby.")]
    public double radiusMetres = 500d;

    readonly List<BuildingRowView> _rows = new List<BuildingRowView>();
    readonly List<BuildingData> _shown = new List<BuildingData>();
    string _signature = string.Empty;

    /// <summary>Set by CameraTabController so a row can open the info sheet.</summary>
    public System.Action<BuildingData> onBuildingSelected;

    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (scrimButton != null)
            scrimButton.onClick.AddListener(Close);

        if (scrim != null)
            scrim.SetActive(false);

        if (sheet != null)
            sheet.SnapClosed();
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
            locationService.OnPoseUpdated += RefreshDistances;

        AppStrings.OnLanguageChanged += Rebuild;
    }

    void OnDisable()
    {
        if (locationService != null)
            locationService.OnPoseUpdated -= RefreshDistances;

        AppStrings.OnLanguageChanged -= Rebuild;
    }

    public bool IsOpen
    {
        get { return sheet != null && sheet.IsOpen; }
    }

    public int NearbyCount
    {
        get
        {
            if (locationService == null || !locationService.HasValidPose)
                return 0;

            return locationService.BuildingsWithin(radiusMetres).Count;
        }
    }

    public void Open()
    {
        Rebuild();

        if (scrim != null)
            scrim.SetActive(true);

        if (sheet != null)
            sheet.Open();
    }

    public void Close()
    {
        if (scrim != null)
            scrim.SetActive(false);

        if (sheet != null)
            sheet.Close();
    }

    public void Rebuild()
    {
        if (titleText != null)
            titleText.text = AppStrings.Get("nearby.title");

        ClearRows();

        bool hasPose = locationService != null && locationService.HasValidPose;
        List<BuildingData> nearby = hasPose
            ? locationService.BuildingsWithin(radiusMetres)
            : new List<BuildingData>();

        if (emptyText != null)
        {
            emptyText.gameObject.SetActive(nearby.Count == 0);
            emptyText.text = hasPose
                ? string.Format(AppStrings.Get("nearby.empty"), Mathf.RoundToInt((float)radiusMetres))
                : AppStrings.Get("nearby.noLocation");
        }

        _signature = Signature(nearby);
        _shown.Clear();
        _shown.AddRange(nearby);

        for (int i = 0; i < nearby.Count; i++)
        {
            BuildingData building = nearby[i];
            BuildingRowView row = Instantiate(rowPrefab, listParent);

            row.Bind(building, locationService.DistanceMetresTo(building), () =>
            {
                if (onBuildingSelected != null)
                    onBuildingSelected(building);
            });

            _rows.Add(row);
        }
    }

    void RefreshDistances()
    {
        if (!IsOpen)
            return;

        // Destroying and recreating every row once a second made the list flicker
        // and churned garbage for no visible change. Only the distances move most
        // of the time, so rebuild only when the set of buildings actually differs.
        List<BuildingData> nearby = locationService != null && locationService.HasValidPose
            ? locationService.BuildingsWithin(radiusMetres)
            : new List<BuildingData>();

        if (Signature(nearby) != _signature)
        {
            Rebuild();
            return;
        }

        for (int i = 0; i < _rows.Count && i < _shown.Count; i++)
        {
            if (_rows[i] == null || _rows[i].distanceText == null)
                continue;

            _rows[i].distanceText.text = AppStrings.FormatDistance(
                locationService.DistanceMetresTo(_shown[i]), locationService.IsApproximate);
        }
    }

    static string Signature(List<BuildingData> buildings)
    {
        return string.Join("|", buildings.ConvertAll(b => b.buildingId));
    }

    void ClearRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] == null)
                continue;

            // Destroy is deferred to the end of the frame, so unparent first -
            // otherwise the layout counts both the dying rows and their
            // replacements on the rebuild that happens in between.
            _rows[i].transform.SetParent(null, false);
            Destroy(_rows[i].gameObject);
        }

        _rows.Clear();
    }
}
