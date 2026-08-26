using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "12 / 104 buildings discovered" with a progress bar. Updates live rather than
/// only on tab open, so a building discovered from the Camera tab is already
/// reflected by the time the reader switches over.
/// </summary>
public class DiscoveryProgressCard : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI countText;
    public TextMeshProUGUI labelText;
    public RectTransform barFill;

    [Tooltip("Explains what counts as explored, so the number isn't mysterious.")]
    public TextMeshProUGUI noteText;

    void OnEnable()
    {
        DiscoveryTracker.OnDiscoveryChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        BuildingDatabase.OnBuildingsChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        DiscoveryTracker.OnDiscoveryChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        BuildingDatabase.OnBuildingsChanged -= Refresh;
    }

    public void Refresh()
    {
        int discovered = DiscoveryTracker.DiscoveredCount;
        int total = DiscoveryTracker.TotalCount;

        if (countText != null)
            countText.text = discovered + " / " + total;

        if (labelText != null)
            labelText.text = AppStrings.Get("search.discovered");

        if (noteText != null)
            noteText.text = AppStrings.Get("search.discoveredNote");

        if (barFill != null)
        {
            // An empty project would otherwise divide by zero on first run.
            float fraction = total > 0 ? (float)discovered / total : 0f;

            barFill.anchorMin = new Vector2(0f, 0f);
            barFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            barFill.offsetMin = Vector2.zero;
            barFill.offsetMax = Vector2.zero;
        }
    }
}
