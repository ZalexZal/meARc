using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Маршрути": every trail the reader can walk, searchable, over the blurred map.
///
///   Маршрути                                   (×)
///   ( ⌕ Име или град                             )
///   [Всички] (Образование) (Туризъм) (Недвижими…) →
///   (Всички градове ▾)  (Най-близо)
///   3 маршрута
///   ┌──────────────────────────────────────────┐
///   │ [▣] Ески джамия                    350 м │
///   │     Ямбол · 5 спирки                     │
///   └──────────────────────────────────────────┘
///   (               Влез с код               )
///
/// The list is every public trail (public_trails) plus the reader's own, so a
/// private trail joined by code is found the same way. The search is the
/// website's (TrailFilter), so a name found there is found here. Line of work
/// plays no part: everyone sees and walks every public trail.
///
/// Tapping a trail already joined opens it; tapping any other joins it, as the
/// library always did. A joined trail can be left from its row (two taps).
/// </summary>
public class TrailsDoor : MonoBehaviour
{
    /// <summary>One filter chip, coloured by role so it follows the theme.</summary>
    [Serializable]
    public class Chip
    {
        public Button button;
        public Image fill;
        public GameObject outline;
        public TextMeshProUGUI label;
        public Image icon;
    }

    /// <summary>One trail in the list, as drawn. Built from whichever sources know it.</summary>
    public class Entry
    {
        public string id;
        public string name;
        public string category;
        public TrailService.CityJson city;
        public int stops = -1;
        public double distance = -1d;
        public bool mine;
    }

    [Header("Search")]
    public TMP_InputField searchField;

    [Tooltip("Всички, then TrailFilter.Categories in order.")]
    public Chip[] chips;

    public Button cityButton;
    public TextMeshProUGUI cityLabel;
    public Chip nearestChip;

    [Header("City picker")]
    public GameObject cityMenu;
    [Tooltip("The picker's scrolling list; sized to its rows up to 60% of the screen.")]
    public LayoutElement cityListSize;
    public ScrollRect cityScroll;
    public float cityRowHeight = 140f;
    public RectTransform cityRowsParent;
    public RectTransform cityRowTemplate;

    [Header("List")]
    public TextMeshProUGUI countLabel;
    public GameObject listCard;
    public RectTransform listParent;
    [Tooltip("One trail's row, built once and switched off; copies of it are the list.")]
    public RectTransform rowTemplate;
    public ScrollRect scroll;

    [Tooltip("education, tourism, estate, other - TrailFilter.Categories order.")]
    public Sprite[] categoryIcons;

    [Header("States")]
    public GameObject waitingState;
    public GameObject failedState;
    public TextMeshProUGUI failedLabel;

    [Tooltip("Nothing matches: the sentence and Clear filters.")]
    public GameObject emptyState;
    public TextMeshProUGUI emptyLabel;
    public Button clearButton;

    [Header("Controls")]
    public Button joinButton;

    // --- filters, kept between openings
    string _query = string.Empty;
    int _category;           // 0 = all, else TrailFilter.Categories[_category - 1]
    string _cityKey;         // null = all cities, "" = no city
    bool _nearest;
    bool _nearestTouched;

    readonly List<RectTransform> _rows = new List<RectTransform>();
    readonly List<RectTransform> _cityRows = new List<RectTransform>();
    List<Entry> _entries = new List<Entry>();
    List<Entry> _sample;     // the preview's stand-in data
    string _armedId;
    bool _joining;
    bool _wired;

    void Awake()
    {
        Wire();
    }

    void Wire()
    {
        if (_wired)
            return;

        _wired = true;

        if (rowTemplate != null)
            rowTemplate.gameObject.SetActive(false);
        if (cityRowTemplate != null)
            cityRowTemplate.gameObject.SetActive(false);
        if (cityMenu != null)
            cityMenu.SetActive(false);

        if (searchField != null)
            searchField.onValueChanged.AddListener(text =>
            {
                _query = text ?? string.Empty;
                Draw();
            });

        for (int i = 0; chips != null && i < chips.Length; i++)
        {
            int index = i;
            if (chips[i] != null && chips[i].button != null)
                chips[i].button.onClick.AddListener(() =>
                {
                    _category = index;
                    Haptics.Tick();
                    Draw();
                });
        }

        if (nearestChip != null && nearestChip.button != null)
            nearestChip.button.onClick.AddListener(() =>
            {
                _nearest = !_nearest;
                _nearestTouched = true;
                Haptics.Tick();
                Draw();
            });

        if (cityButton != null)
            cityButton.onClick.AddListener(OpenCities);

        if (clearButton != null)
            clearButton.onClick.AddListener(ClearFilters);
    }

    void OnEnable()
    {
        Wire();
        AppStrings.OnLanguageChanged += Refresh;
        Refresh();
        Load();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Refresh;
        if (cityMenu != null)
            cityMenu.SetActive(false);
    }

    /// <summary>Once per opening: the public list, and the reader's own if that failed.</summary>
    void Load()
    {
        TrailService service = TrailService.Instance;
        if (service == null)
            return;

        service.RetryIfFailed();
        // On the service, so closing the screen mid-request does not strand it;
        // drawn when it comes back if the screen is still up.
        service.StartCoroutine(service.LoadAll(() =>
        {
            if (this != null && isActiveAndEnabled)
                Refresh();
        }));
    }

    /// <summary>Gathers the trails again from the service and draws them.</summary>
    public void Refresh()
    {
        if (_sample == null)
            _entries = Gather();

        Draw();
    }

    static bool Located(out double lat, out double lng)
    {
        UserLocationService location = UserLocationService.Instance;
        bool located = location != null && location.HasValidPose;
        lat = located ? location.Latitude : 0d;
        lng = located ? location.Longitude : 0d;
        return located;
    }

    List<Entry> Gather()
    {
        List<Entry> list = new List<Entry>();
        Dictionary<string, Entry> byId = new Dictionary<string, Entry>();
        TrailService service = TrailService.Instance;

        if (service == null)
            return list;

        bool located = Located(out double lat, out double lng);

        // Every public trail, then the near ones in case the full list has not
        // come back, then the reader's own (private ones included).
        AddListed(list, byId, service.All, located, lat, lng);
        AddListed(list, byId, service.Near, located, lat, lng);

        for (int i = 0; service.Mine != null && i < service.Mine.Count; i++)
        {
            TrailService.Trail trail = service.Mine[i];
            Entry entry;

            if (!byId.TryGetValue(trail.id, out entry))
            {
                entry = new Entry
                {
                    id = trail.id,
                    name = trail.name,
                    category = trail.category,
                    city = trail.city
                };
                byId[trail.id] = entry;
                list.Add(entry);
            }

            entry.mine = true;
        }

        return list;
    }

    static void AddListed(List<Entry> list, Dictionary<string, Entry> byId,
                          List<TrailService.Listed> source, bool located, double lat, double lng)
    {
        for (int i = 0; source != null && i < source.Count; i++)
        {
            TrailService.Listed listed = source[i];

            if (string.IsNullOrEmpty(listed.id) || byId.ContainsKey(listed.id))
                continue;

            double startLat = listed.start_lat != 0d ? listed.start_lat : listed.lat;
            double startLng = listed.start_lng != 0d ? listed.start_lng : listed.lng;

            double distance = -1d;
            if (located && (startLat != 0d || startLng != 0d))
                distance = GeoMath.DistanceMetres(lat, lng, startLat, startLng);
            else if (located && listed.distance_m > 0d)
                distance = listed.distance_m;

            Entry entry = new Entry
            {
                id = listed.id,
                name = listed.name,
                category = listed.category,
                city = listed.city,
                stops = listed.stop_count,
                distance = distance
            };

            byId[listed.id] = entry;
            list.Add(entry);
        }
    }

    // ---------------------------------------------------------------- preview

    /// <summary>The preview harness's data, drawn by the same code the app uses.</summary>
    public void UseSample(List<Entry> entries)
    {
        Wire();
        _sample = entries;
        _entries = entries ?? new List<Entry>();
    }

    /// <summary>Sets every filter at once, as the preview harness needs.</summary>
    public void SetFilters(string query, int category, string cityKey, bool nearest)
    {
        Wire();
        _query = query ?? string.Empty;
        _category = category;
        _cityKey = cityKey;
        _nearest = nearest;
        _nearestTouched = true;

        if (searchField != null)
            searchField.SetTextWithoutNotify(_query);

        Draw();
    }

    // ------------------------------------------------------------------- draw

    bool AnyDistance()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].distance >= 0d)
                return true;
        }

        return false;
    }

    void Draw()
    {
        TrailService service = TrailService.Instance;
        bool located = AnyDistance();

        // Nearest first is the default once there is something to measure from.
        if (!_nearestTouched)
            _nearest = located;

        List<Entry> shown = Filter();

        bool filtering = _query.Trim().Length > 0 || _category != 0 || _cityKey != null;
        bool loaded = _sample != null || service == null || service.AllLoaded || service.HasLoaded;
        bool failed = _sample == null && service != null && service.AllFailed && _entries.Count == 0;
        bool waiting = !loaded && !failed && _entries.Count == 0;

        Set(waitingState, waiting);
        Set(failedState, failed);
        if (failedLabel != null)
            failedLabel.text = AppStrings.Get("trails.failed");

        bool empty = !waiting && !failed && shown.Count == 0;
        Set(emptyState, empty);

        if (empty && emptyLabel != null)
        {
            string query = _query.Trim();
            emptyLabel.text = query.Length > 0 ? string.Format(AppStrings.Get("find.nothing"), query)
                            : filtering ? AppStrings.Get("find.nothingFiltered")
                            : AppStrings.Get("trails.empty");
        }

        if (clearButton != null)
            Set(clearButton.gameObject, empty && filtering);

        if (countLabel != null)
        {
            Set(countLabel.gameObject, shown.Count > 0);
            countLabel.text = shown.Count == 1
                ? AppStrings.Get("find.countOne")
                : string.Format(AppStrings.Get("find.count"), shown.Count);
        }

        DrawChips(located);
        DrawRows(shown);
        Set(listCard, shown.Count > 0);
    }

    List<Entry> Filter()
    {
        List<Entry> shown = new List<Entry>();
        string category = _category > 0 && _category <= TrailFilter.Categories.Length
            ? TrailFilter.Categories[_category - 1]
            : null;

        for (int i = 0; i < _entries.Count; i++)
        {
            Entry e = _entries[i];

            if (category != null && TrailFilter.Categories[TrailFilter.CategoryIndex(e.category)] != category)
                continue;

            if (_cityKey != null && CityKey(e.city) != _cityKey)
                continue;

            if (!TrailFilter.Matches(_query, e.name,
                                     e.city != null ? e.city.bg : null,
                                     e.city != null ? e.city.en : null))
                continue;

            shown.Add(e);
        }

        CompareInfo compare = Bulgarian();

        shown.Sort((a, b) =>
        {
            if (_nearest)
            {
                // Unknown distances last, then nearest first.
                bool ka = a.distance >= 0d, kb = b.distance >= 0d;
                if (ka != kb) return ka ? -1 : 1;
                if (ka && a.distance != b.distance) return a.distance.CompareTo(b.distance);
            }

            return compare.Compare(a.name ?? string.Empty, b.name ?? string.Empty,
                                   CompareOptions.IgnoreCase);
        });

        return shown;
    }

    static CompareInfo _bulgarian;

    /// <summary>Bulgarian collation, or the invariant one on a phone without it.</summary>
    static CompareInfo Bulgarian()
    {
        if (_bulgarian != null)
            return _bulgarian;

        try { _bulgarian = new CultureInfo("bg-BG").CompareInfo; }
        catch (Exception) { _bulgarian = CultureInfo.InvariantCulture.CompareInfo; }

        return _bulgarian;
    }

    static string CityKey(TrailService.CityJson city)
    {
        return city == null || string.IsNullOrEmpty(city.key) ? string.Empty : city.key;
    }

    void DrawChips(bool located)
    {
        for (int i = 0; chips != null && i < chips.Length; i++)
        {
            Chip chip = chips[i];
            if (chip == null)
                continue;

            bool on = i == _category;
            Paint(chip.fill, on ? ThemeRole.TextPrimary : ThemeRole.Surface);
            Paint(chip.label, on ? ThemeRole.Surface : ThemeRole.TextPrimary);
            Set(chip.outline, !on);

            if (chip.label != null)
                chip.label.text = i == 0 ? AppStrings.Get("find.all")
                                         : TrailService.CategoryLabel(TrailFilter.Categories[i - 1]);

            // The selected category carries its glyph; "All" has none.
            if (chip.icon != null)
            {
                bool glyph = on && i > 0 && categoryIcons != null &&
                             categoryIcons.Length == TrailFilter.Categories.Length;
                Set(chip.icon.gameObject, glyph);
                if (glyph)
                {
                    chip.icon.sprite = categoryIcons[i - 1];
                    Paint(chip.icon, ThemeRole.Surface);
                }
            }
        }

        if (cityLabel != null)
            cityLabel.text = _cityKey == null ? AppStrings.Get("find.allCities") : CityName(_cityKey);

        if (nearestChip != null)
        {
            if (nearestChip.button != null)
                Set(nearestChip.button.gameObject, located);

            Paint(nearestChip.fill, _nearest ? ThemeRole.AccentTint : ThemeRole.Surface);
            Paint(nearestChip.label, _nearest ? ThemeRole.AccentText : ThemeRole.TextPrimary);
            Set(nearestChip.outline, !_nearest);

            if (nearestChip.label != null)
                nearestChip.label.text = AppStrings.Get("find.nearest");
        }
    }

    string CityName(string key)
    {
        if (key.Length == 0)
            return AppStrings.Get("find.noCity");

        for (int i = 0; i < _entries.Count; i++)
        {
            if (CityKey(_entries[i].city) == key)
                return TrailService.CityJson.NameOf(_entries[i].city);
        }

        return key;
    }

    static void Paint(Graphic graphic, ThemeRole role)
    {
        if (graphic == null)
            return;

        ThemedElement themed = graphic.GetComponent<ThemedElement>();
        if (themed != null)
        {
            themed.role = role;
            themed.Apply();
        }
        else
        {
            graphic.color = ThemeManager.Get(role);
        }
    }

    void DrawRows(List<Entry> shown)
    {
        if (listParent == null || rowTemplate == null)
            return;

        while (_rows.Count < shown.Count)
        {
            RectTransform row = Instantiate(rowTemplate, listParent);
            row.name = "Trail";
            _rows.Add(row);
        }

        TrailService service = TrailService.Instance;
        string selected = service != null ? service.SelectedId : null;

        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < shown.Count;
            Set(_rows[i].gameObject, used);

            if (used)
                DrawRow(_rows[i], shown[i], i == 0, shown[i].id == selected);
        }
    }

    void DrawRow(RectTransform row, Entry entry, bool first, bool current)
    {
        TextMeshProUGUI name = Find<TextMeshProUGUI>(row, "Text/Name");
        TextMeshProUGUI sub = Find<TextMeshProUGUI>(row, "Text/Sub");
        TextMeshProUGUI distance = Find<TextMeshProUGUI>(row, "Side/Distance");
        TextMeshProUGUI leave = Find<TextMeshProUGUI>(row, "Side/Leave/Label");
        Button leaveButton = Find<Button>(row, "Side/Leave");
        Image icon = Find<Image>(row, "Chip/Icon");
        Transform rule = row.Find("Rule");

        if (name != null)
            name.text = string.IsNullOrWhiteSpace(entry.name) ? entry.id : entry.name;

        if (sub != null)
        {
            string city = TrailService.CityJson.NameOf(entry.city);
            string stops = entry.stops >= 0 ? string.Format(AppStrings.Get("trails.markers"), entry.stops)
                                            : string.Empty;
            string text = city.Length == 0 ? stops : stops.Length == 0 ? city : city + " · " + stops;
            sub.text = text;
            sub.gameObject.SetActive(text.Length > 0);
        }

        if (icon != null && categoryIcons != null && categoryIcons.Length == TrailFilter.Categories.Length)
            icon.sprite = categoryIcons[TrailFilter.CategoryIndex(entry.category)];

        if (distance != null)
        {
            distance.gameObject.SetActive(entry.distance >= 0d);
            distance.text = entry.distance >= 0d ? AppStrings.FormatDistance(entry.distance) : string.Empty;
        }

        if (leaveButton != null)
        {
            leaveButton.gameObject.SetActive(entry.mine);
            leaveButton.onClick.RemoveAllListeners();
            string id = entry.id;
            leaveButton.onClick.AddListener(() => TapLeave(id));
        }

        if (leave != null)
        {
            bool armed = entry.mine && entry.id == _armedId;
            leave.text = AppStrings.Get(armed ? "trails.leaveConfirm" : "trails.leave");
            Paint(leave, armed ? ThemeRole.StatusPoor : ThemeRole.TextMuted);
        }

        if (rule != null)
            rule.gameObject.SetActive(!first);

        Button button = row.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            string id = entry.id;
            bool mine = entry.mine;
            button.onClick.AddListener(() =>
            {
                if (mine)
                    Open(id);
                else
                    JoinListed(id);
            });
        }
    }

    static T Find<T>(Transform root, string path) where T : Component
    {
        Transform t = root.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    // ------------------------------------------------------------------ cities

    /// <summary>The cities present in the loaded trails, Bulgarian order, plus
    /// "No city" when any trail has none.</summary>
    List<KeyValuePair<string, string>> Cities()
    {
        Dictionary<string, string> names = new Dictionary<string, string>();
        bool noCity = false;

        for (int i = 0; i < _entries.Count; i++)
        {
            string key = CityKey(_entries[i].city);
            if (key.Length == 0)
                noCity = true;
            else if (!names.ContainsKey(key))
                names[key] = TrailService.CityJson.NameOf(_entries[i].city);
        }

        List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>(names);
        CompareInfo compare = Bulgarian();
        list.Sort((a, b) => compare.Compare(a.Value, b.Value, CompareOptions.IgnoreCase));

        if (noCity)
            list.Add(new KeyValuePair<string, string>(string.Empty, AppStrings.Get("find.noCity")));

        list.Insert(0, new KeyValuePair<string, string>(null, AppStrings.Get("find.allCities")));
        return list;
    }

    public void OpenCities()
    {
        if (cityMenu == null || cityRowsParent == null || cityRowTemplate == null)
            return;

        List<KeyValuePair<string, string>> cities = Cities();

        while (_cityRows.Count < cities.Count)
        {
            RectTransform row = Instantiate(cityRowTemplate, cityRowsParent);
            row.name = "City";
            _cityRows.Add(row);
        }

        for (int i = 0; i < _cityRows.Count; i++)
        {
            bool used = i < cities.Count;
            Set(_cityRows[i].gameObject, used);
            if (!used)
                continue;

            string key = cities[i].Key;
            TextMeshProUGUI label = Find<TextMeshProUGUI>(_cityRows[i], "Label");
            Image check = Find<Image>(_cityRows[i], "Check");
            Transform rule = _cityRows[i].Find("Rule");

            if (label != null)
                label.text = cities[i].Value;
            if (check != null)
                check.enabled = key == _cityKey;
            if (rule != null)
                rule.gameObject.SetActive(i > 0);

            Button button = _cityRows[i].GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    _cityKey = key;
                    cityMenu.SetActive(false);
                    Haptics.Tick();
                    Draw();
                });
            }
        }

        cityMenu.SetActive(true);

        // As tall as its rows, up to 60% of the screen; past that it scrolls,
        // with "All cities" first at the top.
        if (cityListSize != null)
        {
            float cap = ((RectTransform)cityMenu.transform).rect.height * 0.6f;
            float wanted = cities.Count * cityRowHeight;
            cityListSize.minHeight = cityListSize.preferredHeight =
                cap > 1f ? Mathf.Min(wanted, cap) : wanted;
        }

        if (cityScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            cityScroll.verticalNormalizedPosition = 1f;
        }
    }

    public void CloseCities()
    {
        if (cityMenu != null)
            cityMenu.SetActive(false);
    }

    void ClearFilters()
    {
        _query = string.Empty;
        _category = 0;
        _cityKey = null;

        if (searchField != null)
            searchField.SetTextWithoutNotify(string.Empty);

        Haptics.Tick();
        Draw();
    }

    // ----------------------------------------------------------------- actions

    void JoinListed(string worldId)
    {
        TrailService service = TrailService.Instance;

        if (service == null || _joining)
            return;

        _joining = true;

        StartCoroutine(service.JoinListed(worldId, ok =>
        {
            _joining = false;

            if (ok)
            {
                if (DoorFlow.Instance != null)
                    DoorFlow.Instance.Entered();
                return;
            }

            // A toast, not the filter's empty state: the list is still right.
            ProximityToast.Say(AppStrings.Get("near.failed"));
        }));
    }

    /// <summary>
    /// First tap arms, second leaves. The list then refreshes from the server
    /// rather than dropping the row on the spot: if the delete did not happen,
    /// the trail should still be there.
    /// </summary>
    void TapLeave(string worldId)
    {
        if (_armedId != worldId)
        {
            _armedId = worldId;
            Haptics.Tick();
            Draw();
            return;
        }

        _armedId = null;
        TrailService service = TrailService.Instance;

        if (service == null)
        {
            Draw();
            return;
        }

        StartCoroutine(service.Leave(worldId, _ => Refresh()));
    }

    void Open(string worldId)
    {
        TrailService service = TrailService.Instance;
        if (service != null)
            service.Select(worldId);

        if (DoorFlow.Instance != null)
            DoorFlow.Instance.Entered();
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }
}
