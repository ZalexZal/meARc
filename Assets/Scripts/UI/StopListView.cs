using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Every stop on the trail, in its order, as one inset list: the number in the
/// stop's colour, its name, and where it stands - found, here, how far, next.
///
///   2 of 5 found. You open a stop when you are next to it.
///   ①  Безистен                    ● Found
///   ③  Театър „Невена Коканова“    180 m · next
///   ④  Исторически музей           310 m
///
/// Opened by "All stops" and by swiping the next-stop card up. A row opens its
/// stop; the list closes on the way, because the stop's own sheet is the page
/// that follows.
/// </summary>
public class StopListView : MonoBehaviour
{
    public UserLocationService locationService;
    public InfoPanelController infoPanel;

    public TextMeshProUGUI summaryLabel;
    public RectTransform listParent;

    [Tooltip("A row, built once and switched off; copies of it are the list.")]
    public RectTransform rowTemplate;

    public Button backButton;
    public Button closeButton;

    readonly List<RectTransform> _rows = new List<RectTransform>();
    float _timer;

    void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(Close);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (rowTemplate != null)
            rowTemplate.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        AppStrings.OnLanguageChanged += Draw;
        ThemeManager.OnThemeChanged += Draw;
        BuildingDatabase.OnBuildingsChanged += Draw;
        DiscoveryTracker.OnDiscoveryChanged += Draw;
        HiddenMarkers.OnChanged += Draw;
        Draw();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Draw;
        ThemeManager.OnThemeChanged -= Draw;
        BuildingDatabase.OnBuildingsChanged -= Draw;
        DiscoveryTracker.OnDiscoveryChanged -= Draw;
        HiddenMarkers.OnChanged -= Draw;
    }

    public void Open()
    {
        gameObject.SetActive(true);

        // Back only when there is a stop under the list to go back to. Opened
        // from the card or the radar, it is the first sheet up, and Daylight
        // gives a first sheet only its close.
        PageBack.Show(backButton, infoPanel != null && infoPanel.Current != null);
        Draw();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    void Update()
    {
        // Distances change as the reader walks.
        _timer += Time.unscaledDeltaTime;
        if (_timer < 1f)
            return;

        _timer = 0f;
        Draw();
    }

    static List<BuildingData> Ordered()
    {
        List<BuildingData> all = new List<BuildingData>(BuildingDatabase.AllBuildings);
        all.RemoveAll(b => b == null);
        all.Sort((a, b) =>
        {
            int sa = a.step > 0 ? a.step : int.MaxValue;
            int sb = b.step > 0 ? b.step : int.MaxValue;
            return sa != sb ? sa.CompareTo(sb) : string.CompareOrdinal(a.DisplayName, b.DisplayName);
        });
        return all;
    }

    void Draw()
    {
        if (listParent == null || rowTemplate == null)
            return;

        List<BuildingData> stops = Ordered();
        int found = 0;
        for (int i = 0; i < stops.Count; i++)
        {
            if (NextStopCard.Done(stops[i]))
                found++;
        }

        if (summaryLabel != null)
            summaryLabel.text = string.Format(AppStrings.Get("list.summary"), found, stops.Count);

        while (_rows.Count < stops.Count)
        {
            RectTransform row = Instantiate(rowTemplate, listParent);
            row.gameObject.SetActive(true);
            _rows.Add(row);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < stops.Count;
            if (_rows[i].gameObject.activeSelf != used)
                _rows[i].gameObject.SetActive(used);

            if (used)
                DrawRow(_rows[i], stops[i], i == 0);
        }
    }

    void DrawRow(RectTransform row, BuildingData stop, bool first)
    {
        bool done = NextStopCard.Done(stop);
        bool next = stop == NextStopCard.Current;
        Color colour = MarkerPalette.Resolve(stop);

        double metres = locationService != null && locationService.HasValidPose
            ? locationService.DistanceMetresTo(stop)
            : -1d;
        bool near = metres >= 0d && metres <= NextStopCard.NearMetres(stop);
        bool approximate = locationService != null && locationService.IsApproximate;

        Transform rule = row.Find("Rule");
        if (rule != null)
            rule.gameObject.SetActive(!first);

        // The number: filled in its colour once found or next, a ring of it before.
        Image disc = Find<Image>(row, "Line/Badge");
        Image hole = Find<Image>(row, "Line/Badge/Hole");
        TextMeshProUGUI number = Find<TextMeshProUGUI>(row, "Line/Badge/Number");
        bool hollow = !done && !next;

        if (disc != null)
            disc.color = colour;

        if (hole != null)
        {
            hole.gameObject.SetActive(hollow);
            hole.color = ThemeManager.Get(ThemeRole.SurfaceAlt);
        }

        if (number != null)
        {
            number.text = stop.step > 0 ? stop.step.ToString() : "•";
            number.color = hollow ? ThemeManager.Get(ThemeRole.TextPrimary) : MarkerPalette.OnColour(colour);
        }

        TextMeshProUGUI name = Find<TextMeshProUGUI>(row, "Line/Text/Name");
        if (name != null)
            name.text = stop.DisplayName;

        string status;
        bool good = done;

        if (done)
            status = AppStrings.Get("sheet.found");
        else if (near)
            status = AppStrings.Get("list.here") + (next ? " · " + AppStrings.Get("list.next") : string.Empty);
        else if (metres >= 0d)
            status = AppStrings.FormatDistance(metres, approximate) + (next ? " · " + AppStrings.Get("list.next") : string.Empty);
        else
            status = next ? AppStrings.Get("list.nextCap") : string.Empty;

        TextMeshProUGUI line = Find<TextMeshProUGUI>(row, "Line/Text/Status/Label");
        if (line != null)
        {
            line.text = status;
            line.color = ThemeManager.Get(good ? ThemeRole.StatusGood : ThemeRole.TextMuted);
        }

        Image dot = Find<Image>(row, "Line/Text/Status/Dot");
        if (dot != null)
        {
            dot.gameObject.SetActive(good);
            dot.color = ThemeManager.Get(ThemeRole.GoodDot);
        }

        TextMeshProUGUI hidden = Find<TextMeshProUGUI>(row, "Line/Hidden");
        if (hidden != null)
        {
            bool off = HiddenMarkers.IsHidden(stop);
            hidden.gameObject.SetActive(off);
            hidden.text = AppStrings.Get("list.hidden");
        }

        Button button = row.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            BuildingData target = stop;
            button.onClick.AddListener(() =>
            {
                Close();
                if (infoPanel != null)
                    infoPanel.ShowPanel(target);
            });
        }
    }

    static T Find<T>(Transform root, string path) where T : Component
    {
        Transform found = root.Find(path);
        return found != null ? found.GetComponent<T>() : null;
    }
}
