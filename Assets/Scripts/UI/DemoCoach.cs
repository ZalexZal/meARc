using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one line of instruction the demo shows over the map, and the three steps
/// it walks through: open a stop, answer its question, finish.
///
/// Each step waits for the walker to actually do the thing rather than for a
/// Next button, because doing it once is the whole lesson. It hides while a
/// stop's sheet is open - the sheet is what the walker is reading then - and
/// says the next thing when it closes.
/// </summary>
public class DemoCoach : MonoBehaviour
{
    public GameObject card;
    public TextMeshProUGUI line;
    public Button skipButton;
    public Button finishButton;
    public MapView map;

    [Tooltip("Where the stops go when the phone does not know where it is.")]
    public double fallbackLatitude = 42.4842;
    public double fallbackLongitude = 26.5035;

    int _step;
    bool _sheetOpen;
    bool _placing;

    /// <summary>From the moment it is asked for, including the seconds spent
    /// waiting for a position before there are any stops.</summary>
    public bool Running
    {
        get { return _placing || DemoTrail.Active; }
    }

    void Awake()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(Finish);

        if (finishButton != null)
            finishButton.onClick.AddListener(Finish);

        if (card != null)
            card.SetActive(false);
    }

    void OnEnable()
    {
        InfoPanelController.OnOpened += Opened;
        InfoPanelController.OnClosed += Closed;
        MarkerTaskService.OnProgressChanged += CheckAnswered;
        AppStrings.OnLanguageChanged += Draw;
    }

    void OnDisable()
    {
        InfoPanelController.OnOpened -= Opened;
        InfoPanelController.OnClosed -= Closed;
        MarkerTaskService.OnProgressChanged -= CheckAnswered;
        AppStrings.OnLanguageChanged -= Draw;
    }

    public void Begin()
    {
        StopAllCoroutines();
        StartCoroutine(Place());
    }

    /// <summary>Ends the demo without the closing card - a real trail was chosen.</summary>
    public void Stop()
    {
        StopAllCoroutines();
        _placing = false;
        DemoTrail.End();

        if (card != null)
            card.SetActive(false);
    }

    IEnumerator Place()
    {
        UserLocationService location = UserLocationService.Instance;

        if (location != null)
            location.Begin();

        _placing = true;
        Draw();

        // A few seconds for a first fix once the permission is answered. Past
        // that the stops go in the middle of the map instead; it is a practice,
        // not a walk, so where exactly they stand matters little. The dialog
        // itself does not count: the first build did, and a walker who took ten
        // seconds to read it had their stops put in the town centre.
        float waited = 0f;

        while ((location == null || !location.HasValidPose) && waited < 12f)
        {
            if (location == null || !location.Asking)
                waited += Time.unscaledDeltaTime;

            yield return null;
        }

        double lat = fallbackLatitude;
        double lng = fallbackLongitude;

        if (location != null && location.HasValidPose)
        {
            lat = location.Latitude;
            lng = location.Longitude;
        }
        else if (map != null && map.TryGetViewCentre(out double viewLat, out double viewLng))
        {
            lat = viewLat;
            lng = viewLng;
        }

        _placing = false;
        DemoTrail.Begin(lat, lng);

        if (map != null)
            map.LookAt(lat, lng, 160f);

        _step = 0;
        _sheetOpen = false;
        Draw();
    }

    void Opened(BuildingData stop)
    {
        if (!DemoTrail.Active)
            return;

        _sheetOpen = true;

        if (_step == 0 && DemoTrail.Owns(stop))
            _step = 1;

        Draw();
    }

    void Closed()
    {
        _sheetOpen = false;
        Draw();
    }

    void CheckAnswered()
    {
        if (!DemoTrail.Active || _step != 1 || MarkerTaskService.Instance == null)
            return;

        if (MarkerTaskService.Instance.Progress(DemoTrail.Prefix + "2").solved)
        {
            _step = 2;
            Draw();
        }
    }

    void Draw()
    {
        bool show = (_placing || DemoTrail.Active) && !_sheetOpen;

        if (card != null && card.activeSelf != show)
            card.SetActive(show);

        if (!show)
            return;

        if (line != null)
            line.text = AppStrings.Get(_placing ? "demo.placing" : "demo.step" + (_step + 1));

        bool last = !_placing && _step >= 2;

        if (skipButton != null)
            skipButton.gameObject.SetActive(!last);

        if (finishButton != null)
            finishButton.gameObject.SetActive(last);
    }

    void Finish()
    {
        Stop();

        if (DoorFlow.Instance != null)
            DoorFlow.Instance.ShowReady();
    }
}
