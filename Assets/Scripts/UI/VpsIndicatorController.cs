using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The chip in the camera's top corner: whether the stops are in place, in words.
///
///   ◌ Looking at the buildings around you…      while it calibrates
///   ● Stops are in place                         then shrinks to its dot
///   ● Stops may be a few metres off              rough, and shrinks too
///   ● I can't get my bearings here · Why?        tap for what to do about it
///
/// Daylight v2 replaced a coloured dot and a bubble of jargon - "VPS", "Poor
/// accuracy 14.3 m" - with the sentence a walker would say. Once the stops are
/// in place the chip gets out of the way, down to a dot; a tap brings the words
/// back for a moment. While it is still looking, a coach in the middle of the
/// screen says what to do with the phone.
///
/// Calibration is remembered: coming back to the camera does not make anyone
/// wait through "looking" again.
/// </summary>
public class VpsIndicatorController : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;

    [Tooltip("The chip itself.")]
    public Button dotButton;
    public Image dotImage;
    public TextMeshProUGUI dotLabel;

    [Header("Daylight v2")]
    [Tooltip("The chip's rect, whose width animates between its words and its dot.")]
    public RectTransform chip;

    [Tooltip("Fades the words out as the chip shrinks.")]
    public CanvasGroup labelGroup;

    [Tooltip("A ring that turns while it is still looking.")]
    public RectTransform spinner;

    [Tooltip("The phone and the sentence in the middle of the screen, while looking.")]
    public GameObject coach;

    [Tooltip("What to do when it cannot get its bearings. Opened from the chip.")]
    public ArHelpView help;

    [Tooltip("Seconds of looking before it says it cannot.")]
    public float giveUpAfter = 20f;

    [Tooltip("The creator's pill in the other corner. It steps down a row while " +
             "the chip is spelled out, and back up beside the dot.")]
    public RectTransform avoid;

    [Header("Kept so older scenes load")]
    public GameObject scrim;
    public Button scrimButton;
    public GameObject bubbleRoot;
    public Image bubbleDot;
    public TextMeshProUGUI bubbleTitle;
    public TextMeshProUGUI bubbleBody;
    public TextMeshProUGUI bubbleAccuracy;

    enum Look { Search, Ok, Rough, Fail }

    VpsStatus _status = VpsStatus.Unavailable;
    Look _look;
    float _since;
    float _compactAt = -1f;
    bool _compact;
    float _width;
    float _target;
    static bool _calibrated;

    const float Dot = 111f;

    void Awake()
    {
        if (dotButton != null)
            dotButton.onClick.AddListener(Tap);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
        {
            locationService.OnStatusChanged += HandleStatusChanged;
            _status = locationService.Status;
        }

        AppStrings.OnLanguageChanged += Refresh;

        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);

        if (scrim != null)
            scrim.SetActive(false);

        _since = 0f;
        _compact = false;
        _compactAt = -1f;
        Refresh();

        // Back on the camera with the stops already placed: straight to the dot.
        if (_look == Look.Ok || _look == Look.Rough)
        {
            if (_calibrated)
            {
                Shrink(true);
            }
            else
            {
                _calibrated = true;
                _compactAt = Time.unscaledTime + 2.6f;
            }
        }

        _width = _target;
        Apply();
    }

    void OnDisable()
    {
        if (locationService != null)
            locationService.OnStatusChanged -= HandleStatusChanged;

        AppStrings.OnLanguageChanged -= Refresh;
    }

    void HandleStatusChanged(VpsStatus status)
    {
        Look was = _look;
        _status = status;
        Refresh();

        // Just found its bearings: say so, then step aside.
        if ((_look == Look.Ok || _look == Look.Rough) && was != _look)
        {
            _calibrated = true;
            Haptics.Confirm();
            _compact = false;
            _compactAt = Time.unscaledTime + 2.6f;
        }
    }

    void Tap()
    {
        if (_look == Look.Fail)
        {
            if (help != null)
                help.Open();
            return;
        }

        // The dot opens back into its words for a moment.
        if (_compact)
        {
            _compact = false;
            _compactAt = Time.unscaledTime + 2.5f;
            Refresh();
        }
    }

    /// <summary>Kept for the camera tab, which closes this as a sheet opens.</summary>
    public void ToggleBubble() { Tap(); }

    public void CloseBubble() { }

    Look Decide()
    {
        switch (_status)
        {
            case VpsStatus.Good: return Look.Ok;
            case VpsStatus.Fair: return Look.Rough;
            // Refused is final. Poor is what Geospatial says for its first
            // seconds almost anywhere, so it is still "looking" until it has
            // had a fair chance.
            case VpsStatus.Refused: return Look.Fail;
            default: return _since >= giveUpAfter ? Look.Fail : Look.Search;
        }
    }

    void Refresh()
    {
        _look = Decide();

        string key = _look == Look.Ok ? "vps.ok"
                   : _look == Look.Rough ? "vps.rough"
                   : _look == Look.Fail ? "vps.fail"
                   : "vps.search";

        if (dotLabel != null)
        {
            string text = AppStrings.Get(key);
            if (dotLabel.text != text)
                dotLabel.text = text;
        }

        // Fixed colours: the chip is dark glass over a camera feed in either theme.
        if (dotImage != null)
        {
            dotImage.color = _look == Look.Ok ? new Color(0.188f, 0.82f, 0.345f)
                           : _look == Look.Search ? new Color(0f, 0f, 0f, 0f)
                           : new Color(1f, 0.624f, 0.039f);
        }

        if (spinner != null && spinner.gameObject.activeSelf != (_look == Look.Search))
            spinner.gameObject.SetActive(_look == Look.Search);

        if (coach != null && coach.activeSelf != (_look == Look.Search))
            coach.SetActive(_look == Look.Search);

        if (_look == Look.Search || _look == Look.Fail)
        {
            _compact = false;
            _compactAt = -1f;
        }

        Shrink(_compact);
    }

    void Shrink(bool compact)
    {
        _compact = compact;

        if (dotLabel != null)
            dotLabel.ForceMeshUpdate();

        // 14 + dot + 9 + words + 14, as the prototype pads it.
        float words = dotLabel != null ? dotLabel.preferredWidth : 300f;
        _target = compact ? Dot : Mathf.Min(39f + 28f + 25f + words + 39f, 1014f);
    }

    void Update()
    {
        _since += Time.unscaledDeltaTime;

        if (_look == Look.Search && _since >= giveUpAfter)
            Refresh();

        if (_compactAt > 0f && Time.unscaledTime >= _compactAt)
        {
            _compactAt = -1f;
            Shrink(true);
        }


        if (spinner != null && spinner.gameObject.activeSelf)
            spinner.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 450f);

        // Eased toward its width over about half a second.
        if (Mathf.Abs(_width - _target) > 0.5f)
        {
            _width = Mathf.Lerp(_width, _target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));
            Apply();
        }
        else if (_width != _target)
        {
            _width = _target;
            Apply();
        }
    }

    void Apply()
    {
        if (chip != null)
            chip.sizeDelta = new Vector2(_width, chip.sizeDelta.y);

        // A row down while the chip needs the width, beside it once it is a dot.
        if (avoid != null)
        {
            float k = Mathf.Clamp01((_width - Dot) / 200f);
            Vector2 at = avoid.anchoredPosition;
            at.y = Mathf.Lerp(-33f, -(33f + 111f + 22f), k);
            avoid.anchoredPosition = at;
        }

        if (labelGroup != null)
            labelGroup.alpha = Mathf.Clamp01((_width - Dot) / 200f);
    }
}
