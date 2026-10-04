using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Capture mode: standing at a stop on your own trail and moving it to where
/// it really is.
///
/// A stop placed on the website is placed on a map, and a map is a few metres
/// out in a narrow street - which in the camera means a marker standing in the
/// road instead of in front of the door it is about. Only somebody standing
/// there can see that, so the fix is made from there: the marker moves one
/// metre at a time relative to where the phone is facing, or jumps to where
/// the phone is, and Save writes the new coordinates through move_marker, the
/// same function the website's map uses. The server checks the editor role;
/// the button only appears for owners and editors, but it is not the rule.
///
/// The preview is the marker itself, shifted inside its anchor. Terrain
/// anchors are made with an identity rotation, which ARCore defines as
/// east-up-south, so an offset of (east, 0, -north) metres is exactly where the
/// stop will be once saved - without re-resolving an anchor for every nudge.
///
/// Two more things a creator standing in the street needs (0054):
///
///   Add a stop here - a new stop where the phone is, as a draft only the
///   trail's owner and editors are sent, straight into this bar to be nudged.
///   Its words are written on the website, which publishes it.
///
///   Shape - pin, sphere, cone, cube, sign, one press each, saved through
///   set_marker_shape and swapped in the camera at once, so the choice is made
///   looking at the thing itself.
/// </summary>
public class CaptureMode : MonoBehaviour
{
    public TextMeshProUGUI titleLabel;

    [Tooltip("The calibration coach. Raised while this bar is up: the bar is taller " +
             "than the card it stands in for, and covered the coach's words.")]
    public RectTransform coach;
    public TextMeshProUGUI statusLabel;

    public Button forwardButton;
    public Button backButton;
    public Button leftButton;
    public Button rightButton;
    public Button hereButton;
    public Button saveButton;
    public Button cancelButton;

    [Tooltip("Cycles the stop's shape and saves it.")]
    public Button shapeButton;
    public TextMeshProUGUI shapeLabel;

    /// <summary>The shapes, in the order the button goes through them (0054).</summary>
    public static readonly string[] Shapes = { "pin", "sphere", "cone", "cube", "sign" };

    [Tooltip("Hidden while capturing: the dock's nearest-marker card sits where this bar does.")]
    public GameObject[] hideWhileOpen = new GameObject[0];

    public MarkerSpawner spawner;
    public TabNavigationController tabs;

    /// <summary>One press moves the stop this far.</summary>
    const double StepMetres = 1d;

    /// <summary>
    /// Worse than this, a GPS fix is no improvement on a map. The camera's own
    /// position is used whenever it is tracking, and is better than a metre.
    /// </summary>
    const double RoughestHere = 12d;

    BuildingData _data;
    double _lat0;
    double _lng0;
    double _east;
    double _north;
    bool _busy;
    float _drawTimer;
    string _said;

    public bool Active { get { return _data != null || _placing; } }

    /// <summary>The bar is on screen. It is active exactly while it is open.</summary>
    public static bool IsOpen { get; private set; }

    bool _placing;

    void OnEnable()
    {
        IsOpen = true;
    }

    /// <summary>Whether this reader may move this stop: its trail is theirs, or they edit it.</summary>
    public static bool CanEdit(BuildingData data)
    {
        if (data == null || DemoTrail.Owns(data))
            return false;

        TrailService trails = TrailService.Instance;
        TrailService.Trail trail = trails != null ? trails.Selected : null;

        return trail != null && (trail.role == "owner" || trail.role == "editor");
    }

    /// <summary>Whether this reader may add stops to the open trail: it is theirs, or they edit it.</summary>
    public static bool CanAddHere()
    {
        if (DemoTrail.Active)
            return false;

        TrailService trails = TrailService.Instance;
        TrailService.Trail trail = trails != null ? trails.Selected : null;

        return trail != null && (trail.role == "owner" || trail.role == "editor");
    }

    void Awake()
    {
        Wire(shapeButton, CycleShape);
        Wire(forwardButton, () => Nudge(1f, 0f));
        Wire(backButton, () => Nudge(-1f, 0f));
        Wire(leftButton, () => Nudge(0f, -1f));
        Wire(rightButton, () => Nudge(0f, 1f));
        Wire(hereButton, Here);
        Wire(saveButton, Save);
        Wire(cancelButton, Cancel);
    }

    static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    public void Begin(BuildingData data)
    {
        if (data == null)
            return;

        // A second stop while one is open: put the first back where it was.
        if (_data != null)
            Preview(0d, 0d);

        _data = data;
        _lat0 = data.latitude;
        _lng0 = data.longitude;
        _east = 0d;
        _north = 0d;
        _busy = false;
        _said = null;

        // Add a stop here greys the bar while it places; the stop it made comes
        // back through here, and was left with nothing to press.
        SetInteractable(true);

        if (tabs != null)
            tabs.ShowCamera();

        foreach (GameObject other in hideWhileOpen)
            if (other != null) other.SetActive(false);

        gameObject.SetActive(true);
        LiftCoach(true);
        Draw();
    }

    void LiftCoach(bool up)
    {
        if (coach == null)
            return;

        float y = up ? 0.8f : 0.66f;
        coach.anchorMin = new Vector2(0f, y);
        coach.anchorMax = new Vector2(1f, y);
    }

    void End()
    {
        _data = null;

        foreach (GameObject other in hideWhileOpen)
            if (other != null) other.SetActive(true);

        gameObject.SetActive(false);
    }

    void OnDisable()
    {
        IsOpen = false;
        _placing = false;
        LiftCoach(false);

        // Leaving the camera tab mid-capture is a cancel, not a save. Coroutines
        // stop with the object, so a save in flight is abandoned here too; if it
        // had already landed, the next marker refresh brings the new position.
        if (_data != null)
        {
            Preview(0d, 0d);
            _data = null;
            _busy = false;
            SetInteractable(true);

            foreach (GameObject other in hideWhileOpen)
                if (other != null) other.SetActive(true);
        }
    }

    void Update()
    {
        if (_data == null)
            return;

        // Every frame, because the spawner may have placed the marker - or
        // placed it again - since the last press.
        Preview(_east, _north);

        _drawTimer += Time.unscaledDeltaTime;
        if (_drawTimer >= 0.5f)
        {
            _drawTimer = 0f;
            Draw();
        }
    }

    // ------------------------------------------------------------------ moving

    /// <summary>
    /// One step forward, back, left or right, as the phone is facing.
    ///
    /// Through the camera when the marker is standing in the world - its
    /// anchor's own axes, so "forward" is forward on the screen - and by the
    /// compass when it is not.
    /// </summary>
    void Nudge(float forward, float right)
    {
        if (_data == null || _busy)
            return;

        Transform marker = spawner != null ? spawner.MarkerFor(_data.buildingId) : null;
        Camera eye = Camera.main;

        if (marker != null && marker.parent != null && eye != null)
        {
            Vector3 ahead = Flat(eye.transform.forward, eye.transform.up);
            Vector3 side = Flat(eye.transform.right, eye.transform.right);
            Vector3 local = marker.parent.InverseTransformDirection(ahead * forward + side * right);

            _east += local.x * StepMetres;
            _north += -local.z * StepMetres;
        }
        else
        {
            UserLocationService location = UserLocationService.Instance;
            double bearing = location != null && location.HasHeading ? location.DeviceHeading : 0d;
            double b = bearing * Mathf.Deg2Rad;

            _east += (System.Math.Sin(b) * forward + System.Math.Cos(b) * right) * StepMetres;
            _north += (System.Math.Cos(b) * forward - System.Math.Sin(b) * right) * StepMetres;
        }

        _said = null;
        Draw();
    }

    /// <summary>Level with the ground, falling back when the phone points straight down.</summary>
    static Vector3 Flat(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
        {
            direction = fallback;
            direction.y = 0f;
        }

        return direction.normalized;
    }

    /// <summary>
    /// Puts the stop where the phone is.
    ///
    /// The camera's position when the marker is in the world: that is the
    /// same frame the marker is drawn in, good to well under a metre. Otherwise
    /// the location service's fix, but only when it is good enough to beat the
    /// map it would be replacing.
    /// </summary>
    void Here()
    {
        if (_data == null || _busy)
            return;

        Transform marker = spawner != null ? spawner.MarkerFor(_data.buildingId) : null;
        Camera eye = Camera.main;
        UserLocationService location = UserLocationService.Instance;

        if (location != null && location.IsMocked)
        {
            _said = AppStrings.Get("task.position.mock");
            Draw();
            return;
        }

        if (marker != null && marker.parent != null && eye != null)
        {
            Vector3 local = marker.parent.InverseTransformPoint(eye.transform.position);
            _east = local.x;
            _north = -local.z;
        }
        else if (location != null && location.HasValidPose &&
                 location.HorizontalAccuracy <= RoughestHere)
        {
            GeoMath.LocalOffsetMetres(_lat0, _lng0, location.Latitude, location.Longitude,
                                      out _east, out _north);
        }
        else
        {
            _said = string.Format(AppStrings.Get("capture.tooRough"),
                                  location != null && location.HasValidPose
                                      ? Mathf.RoundToInt((float)location.HorizontalAccuracy).ToString()
                                      : "?");
            Draw();
            return;
        }

        _said = null;
        Draw();
    }

    void Preview(double east, double north)
    {
        Transform marker = spawner != null && _data != null ? spawner.MarkerFor(_data.buildingId) : null;

        if (marker != null)
            marker.localPosition = new Vector3((float)east, 0f, (float)-north);
    }

    // --------------------------------------------------------------- adding one

    /// <summary>
    /// Camera: "Add a stop here". The bar opens at once and says it is placing,
    /// because the request and the refetch take a second or two and a button
    /// that does nothing visible for that long gets pressed again.
    /// </summary>
    public void AddHere()
    {
        if (_busy || _placing || !CanAddHere())
            return;

        if (_data != null)
        {
            Preview(0d, 0d);
            _data = null;
        }

        _placing = true;
        _said = null;

        if (tabs != null)
            tabs.ShowCamera();

        foreach (GameObject other in hideWhileOpen)
            if (other != null) other.SetActive(false);

        gameObject.SetActive(true);
        LiftCoach(true);
        SetInteractable(false);
        if (cancelButton != null)
            cancelButton.interactable = true;

        Say(AppStrings.Get("capture.newTitle"), AppStrings.Get("capture.placing"));
        StartCoroutine(Place());
    }

    IEnumerator Place()
    {
        UserLocationService location = UserLocationService.Instance;
        TrailService trails = TrailService.Instance;
        SupabaseConfig config = SupabaseConfig.Instance;

        if (location == null || trails == null || config == null || string.IsNullOrEmpty(trails.SelectedId))
        {
            Refuse(AppStrings.Get("report.failed"));
            yield break;
        }

        // The same rules as "put it where I'm standing": never a faked position,
        // and never a fix too rough to be better than a map.
        if (location.IsMocked)
        {
            Refuse(AppStrings.Get("task.position.mock"));
            yield break;
        }

        if (!location.HasValidPose || location.HorizontalAccuracy > RoughestHere)
        {
            Refuse(string.Format(AppStrings.Get("capture.tooRough"),
                                 location.HasValidPose
                                     ? Mathf.RoundToInt((float)location.HorizontalAccuracy).ToString()
                                     : "?"));
            yield break;
        }

        // Named in both languages whatever this phone is set to: the stop belongs
        // to the trail, and its readers may read either.
        string body = string.Format(CultureInfo.InvariantCulture,
            "{{\"p_world\":\"{0}\",\"p_lat\":{1:R},\"p_lng\":{2:R}," +
            "\"p_content\":{{\"bg\":{{\"title\":\"Нова спирка\"}},\"en\":{{\"title\":\"New stop\"}}}}," +
            "\"p_style_key\":\"pin\",\"p_status\":\"draft\",\"p_radius_m\":30}}",
            trails.SelectedId.Replace("\"", ""), location.Latitude, location.Longitude);

        string id = null;

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/place_marker"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string said = Postgrest.Said(request) ?? string.Empty;
                Debug.LogWarning("[Capture] place_marker failed (" + request.responseCode + "): " + said);
                Refuse(AppStrings.Get(said.Contains("not yours") ? "capture.notYours"
                                    : said.Contains("200 stops") ? "capture.daily"
                                    : "report.failed"));
                yield break;
            }

            // One row comes back; PostgREST may send it bare or in a list.
            string text = request.downloadHandler.text.Trim();
            if (text.StartsWith("["))
                text = text.Substring(1, text.Length - 2);

            PlacedRow row = JsonUtility.FromJson<PlacedRow>(text);
            id = row != null ? row.id : null;
        }

        if (string.IsNullOrEmpty(id))
        {
            Refuse(AppStrings.Get("report.failed"));
            yield break;
        }

        // Fetched like every other stop, so what is moved is exactly what the
        // spawner draws. Bounded: the stop exists either way, and the website has it.
        RemoteMarkerSource source = FindAnyObjectByType<RemoteMarkerSource>();
        if (source != null)
            source.Refresh();

        BuildingData made = null;
        float waited = 0f;

        while (made == null && waited < 12f)
        {
            yield return new WaitForSeconds(0.5f);
            waited += 0.5f;
            made = BuildingDatabase.GetById(id);
        }

        _placing = false;

        if (made == null)
        {
            Refuse(AppStrings.Get("capture.placedNotShown"));
            yield break;
        }

        Begin(made);
        _said = AppStrings.Get("capture.added");
        Draw();
    }

    [System.Serializable]
    class PlacedRow
    {
        public string id;
    }

    /// <summary>A placing that did not happen: said on the bar, with only Cancel left.</summary>
    void Refuse(string message)
    {
        _placing = false;
        Say(AppStrings.Get("capture.newTitle"), message);
        SetInteractable(false);

        if (cancelButton != null)
            cancelButton.interactable = true;
    }

    void Say(string title, string status)
    {
        if (titleLabel != null) titleLabel.text = title;
        if (statusLabel != null) statusLabel.text = status;
    }

    // ------------------------------------------------------------------ shape

    static string ShapeOf(BuildingData data)
    {
        string key = data != null ? data.markerStyle : null;
        return string.IsNullOrEmpty(key) || key == "default" ? "pin" : key;
    }

    void CycleShape()
    {
        if (_data == null || _busy)
            return;

        int at = System.Array.IndexOf(Shapes, ShapeOf(_data));
        StartCoroutine(SaveShape(Shapes[(at + 1) % Shapes.Length]));
    }

    IEnumerator SaveShape(string key)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || _data == null)
            yield break;

        BuildingData data = _data;
        _busy = true;
        SetInteractable(false);

        string body = "{\"p_marker\":\"" + data.buildingId.Replace("\"", "") +
                      "\",\"p_style_key\":\"" + key + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/set_marker_shape"), body, null))
        {
            yield return request.SendWebRequest();

            _busy = false;
            SetInteractable(true);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string said = Postgrest.Said(request) ?? string.Empty;
                Debug.LogWarning("[Capture] set_marker_shape failed (" + request.responseCode + "): " + said);
                _said = AppStrings.Get(said.Contains("not yours") ? "capture.notYours" : "report.failed");
                Draw();
                yield break;
            }
        }

        // Swapped in the camera now: the spawner drops this stop's marker and
        // places it again from the new shape on its next pass. The move being
        // made, if any, is re-applied every frame by Update.
        data.markerStyle = key;

        if (spawner != null)
            spawner.Replace(data.buildingId);

        _said = null;
        Draw();
    }

    // ------------------------------------------------------------------ saving

    void Save()
    {
        if (_data == null || _busy)
            return;

        if (System.Math.Abs(_east) < 0.05d && System.Math.Abs(_north) < 0.05d)
        {
            Cancel();
            return;
        }

        StartCoroutine(Post());
    }

    IEnumerator Post()
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
            yield break;

        _busy = true;
        SetInteractable(false);
        _said = AppStrings.Get("capture.saving");
        Draw();

        GeoMath.OffsetToLatLon(_lat0, _lng0, _east, _north, out double lat, out double lng);

        // Concatenated, not string.Format: in Unity's runtime "{2:R}}}" reads the
        // "}}" as part of the format, so the number came out wrong, the JSON had
        // no closing brace, and every Save was refused "Empty or invalid json".
        string body = "{\"p_marker\":\"" + _data.buildingId.Replace("\"", "") +
                      "\",\"p_lat\":" + lat.ToString("R", CultureInfo.InvariantCulture) +
                      ",\"p_lng\":" + lng.ToString("R", CultureInfo.InvariantCulture) + "}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/move_marker"), body, null))
        {
            yield return request.SendWebRequest();

            _busy = false;
            SetInteractable(true);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string said = Postgrest.Said(request) ?? string.Empty;
                Debug.LogWarning("[Capture] move_marker failed (" + request.responseCode + "): " + said);
                _said = AppStrings.Get(said.Contains("not yours") ? "capture.notYours" : "report.failed");
                Draw();
                yield break;
            }
        }

        // The stop now lives at the new coordinates. The marker is put back to
        // its anchor's centre and the anchor itself re-resolved there, so what
        // is on screen is what everybody else will see.
        BuildingData moved = _data;
        moved.latitude = lat;
        moved.longitude = lng;

        _east = 0d;
        _north = 0d;
        _lat0 = lat;
        _lng0 = lng;
        Preview(0d, 0d);

        if (spawner != null)
            spawner.Replace(moved.buildingId);

        // Said on the bar for a moment rather than closing straight away: a
        // save that vanishes without a word reads as a tap that missed.
        _said = AppStrings.Get(moved.isDraft ? "capture.savedDraft" : "capture.saved");
        _busy = true;
        SetInteractable(false);
        Draw();

        yield return new WaitForSeconds(1.2f);

        _busy = false;
        SetInteractable(true);

        if (_data == moved)
            End();
    }

    void Cancel()
    {
        if (_busy)
            return;

        _placing = false;
        Preview(0d, 0d);
        End();
    }

    // ------------------------------------------------------------------ drawing

    void Draw()
    {
        if (_data == null)
            return;

        if (titleLabel != null)
            titleLabel.text = string.Format(AppStrings.Get("capture.title"), _data.DisplayName);

        if (shapeLabel != null)
            shapeLabel.text = string.Format(AppStrings.Get("capture.shape"),
                                            AppStrings.Get("shape." + ShapeOf(_data)));

        if (statusLabel == null)
            return;

        if (!string.IsNullOrEmpty(_said))
        {
            statusLabel.text = _said;
            return;
        }

        bool placed = spawner != null && spawner.MarkerFor(_data.buildingId) != null;
        double moved = System.Math.Sqrt(_east * _east + _north * _north);

        string where = moved < 0.05d
            ? AppStrings.Get("capture.unmoved")
            : string.Format(AppStrings.Get("capture.moved"),
                            Metres(System.Math.Abs(_north)),
                            AppStrings.Get(_north >= 0d ? "capture.north" : "capture.south"),
                            Metres(System.Math.Abs(_east)),
                            AppStrings.Get(_east >= 0d ? "capture.east" : "capture.west"));

        string said = placed ? where : where + "\n" + AppStrings.Get("capture.noMarker");

        // A stop made here is a draft until it has words: say so, every time,
        // so nobody walks off thinking walkers can already see it.
        if (_data.isDraft)
            said += "\n" + AppStrings.Get("capture.draftNote");

        statusLabel.text = said;
    }

    static string Metres(double value)
    {
        return value.ToString(value < 10d ? "0.#" : "0", CultureInfo.InvariantCulture);
    }

    void SetInteractable(bool on)
    {
        foreach (Button button in new[] { forwardButton, backButton, leftButton, rightButton,
                                          hereButton, saveButton, cancelButton, shapeButton })
        {
            if (button != null)
                button.interactable = on;
        }
    }
}
