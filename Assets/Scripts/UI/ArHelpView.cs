using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Why are there no stops?" - what to do when the camera cannot get its
/// bearings, opened from the calibration chip.
///
/// Three things a walker can actually do, and the reassurance that the map
/// works without any of it, with how far the next stop is.
/// </summary>
public class ArHelpView : MonoBehaviour
{
    public UserLocationService locationService;
    public Button backButton;
    public Button closeButton;
    public TextMeshProUGUI noteLabel;

    void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(Close);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    public void Open()
    {
        gameObject.SetActive(true);

        // Opened from the chip, with nothing under it to go back to.
        PageBack.Show(backButton, false);
        Draw();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Draw;
        Draw();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Draw;
    }

    void Draw()
    {
        if (noteLabel == null)
            return;

        if (locationService == null)
            locationService = UserLocationService.Instance;

        BuildingData next = NextStopCard.Current;
        double metres = next != null && locationService != null && locationService.HasValidPose
            ? locationService.DistanceMetresTo(next)
            : -1d;

        noteLabel.text = metres >= 0d
            ? string.Format(AppStrings.Get("arhelp.note"), AppStrings.FormatDistance(metres))
            : AppStrings.Get("arhelp.noteNo");
    }
}
