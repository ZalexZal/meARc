using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The app's one toast: a dark pill that drops in from the top, says one thing,
/// and offers at most one thing to do about it.
///
///   Театър е на 20 м.                                  Отвори
///   Спирката е скрита от картата.                      Отмени
///   Кодът е копиран.
///
/// Daylight v2 moved it from a card at the bottom - where it landed on top of
/// the next-stop card and under the reader's thumb - to the top, where nothing
/// else lives. It is still how a nearby stop announces itself, and it is now
/// also what everything else says "done" with.
///
/// The component stays on the always-active holder so it keeps hearing alerts;
/// the pill itself is switched on and off.
/// </summary>
public class ProximityToast : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;
    public InfoPanelController infoPanel;

    [Header("Parts")]
    [Tooltip("The pill's wrapper, not this object.")]
    public GameObject root;
    public RectTransform card;
    public TextMeshProUGUI messageText;

    [Tooltip("The one action, at the pill's right end.")]
    public Button openButton;
    public TextMeshProUGUI actionLabel;

    [Header("Kept so older scenes load")]
    public Image colourDot;
    public Image colourRing;
    public TextMeshProUGUI captionText;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI distanceText;
    public Button dismissButton;

    [Header("Behaviour")]
    public float secondsOnScreen = 6f;

    [Tooltip("Where the pill rests: from the top of the safe area, downwards.")]
    public float restY = -28f;

    [Tooltip("How far above its rest it starts, as a negative number.")]
    public float slideDistance = -260f;
    public float slideSeconds = 0.4f;

    public static ProximityToast Instance { get; private set; }

    BuildingData _marker;
    System.Action _run;
    float _shownAt;
    float _stay;
    float _slideStart;
    bool _sliding;
    bool _leaving;

    public static bool IsShowing { get; private set; }

    void Awake()
    {
        Instance = this;

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
        Instance = this;

        if (locationService == null)
            locationService = UserLocationService.Instance;

        ProximityAlerts.OnAlert += Show;
        AppStrings.OnLanguageChanged += Render;
        BuildingDatabase.OnBuildingsChanged += DropIfGone;
    }

    void OnDisable()
    {
        IsShowing = false;
        ProximityAlerts.OnAlert -= Show;
        AppStrings.OnLanguageChanged -= Render;
        BuildingDatabase.OnBuildingsChanged -= DropIfGone;
    }

    void DropIfGone()
    {
        if (_marker != null && BuildingDatabase.GetById(_marker.buildingId) == null)
            Dismiss();
    }

    /// <summary>A stop announced while the sheet is up would be talking over it.</summary>
    bool Covering()
    {
        return CaptureMode.IsOpen ||
               (infoPanel != null && infoPanel.sheet != null && infoPanel.sheet.IsOpen);
    }

    /// <summary>
    /// Says something, with an optional action. A bare message stays a little
    /// under three seconds; one that offers an action stays long enough to reach.
    /// </summary>
    public static void Say(string message, string action = null, System.Action run = null)
    {
        if (Instance != null)
            Instance.Speak(message, action, run);
    }

    void Speak(string message, string action, System.Action run)
    {
        if (card == null || string.IsNullOrEmpty(message))
            return;

        _marker = null;
        _run = run;
        _stay = string.IsNullOrEmpty(action) ? 2.8f : 4.5f;

        if (messageText != null)
            messageText.text = message;

        SetAction(action);
        Enter();
    }

    /// <summary>A stop the reader has just walked up to.</summary>
    public void Show(BuildingData marker)
    {
        if (marker == null || card == null || Covering())
            return;

        _marker = marker;
        _run = null;
        _stay = secondsOnScreen;
        Render();
        Enter();
    }

    void Enter()
    {
        _shownAt = Time.unscaledTime;
        _leaving = false;

        if (root != null)
            root.SetActive(true);

        IsShowing = true;
        _sliding = true;
        _slideStart = Time.unscaledTime;
        card.anchoredPosition = new Vector2(0f, restY - slideDistance);
    }

    void SetAction(string action)
    {
        bool has = !string.IsNullOrEmpty(action);

        if (openButton != null && openButton.gameObject.activeSelf != has)
            openButton.gameObject.SetActive(has);

        if (actionLabel != null && has)
            actionLabel.text = action;
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

        if (_marker != null && Covering())
        {
            Dismiss();
            return;
        }

        if (_sliding)
            return;

        if (_marker != null)
            Render();

        if (Time.unscaledTime - _shownAt > _stay)
            Dismiss();
    }

    void Slide()
    {
        if (!_sliding || card == null)
            return;

        float t = slideSeconds <= 0f
            ? 1f
            : Mathf.Clamp01((Time.unscaledTime - _slideStart) / slideSeconds);

        // In quick and settling, out a little faster.
        float eased = _leaving ? t * t : 1f - Mathf.Pow(1f - t, 4f);
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
            _run = null;
        }
    }

    void Render()
    {
        if (_marker == null)
            return;

        if (locationService == null)
            locationService = UserLocationService.Instance;

        double metres = locationService != null ? locationService.DistanceMetresTo(_marker) : -1d;

        string text = metres < 0d
            ? _marker.DisplayName
            : string.Format(AppStrings.Get("alert.near"), _marker.DisplayName,
                            AppStrings.FormatDistance(metres,
                                locationService != null && locationService.IsApproximate));

        if (messageText != null && messageText.text != text)
            messageText.text = text;

        SetAction(AppStrings.Get("alert.open"));
    }

    void Open()
    {
        BuildingData marker = _marker;
        System.Action run = _run;
        Dismiss();

        if (run != null)
        {
            run();
            return;
        }

        if (infoPanel != null && marker != null)
            infoPanel.ShowPanel(marker);
    }
}
