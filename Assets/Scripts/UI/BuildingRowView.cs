using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row in a building list. The same prefab serves the Nearby sheet and Search
/// results, so a change to row layout lands in both places at once.
/// </summary>
public class BuildingRowView : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI subtitleText;
    public TextMeshProUGUI distanceText;
    public Button button;

    Action _onClick;

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    /// <summary>
    /// Pass null for distance when there is no pose - the label hides rather than
    /// showing a zero, so Search works indoors without looking broken.
    /// </summary>
    public void Bind(BuildingData data, double? distanceMetres, Action onClick)
    {
        bool approximate = UserLocationService.Instance != null &&
                           UserLocationService.Instance.IsApproximate;

        _onClick = onClick;

        if (data == null)
            return;

        nameText.text = data.DisplayName;
        subtitleText.text = BuildSubtitle(data);

        bool hasDistance = distanceMetres.HasValue && distanceMetres.Value >= 0d;
        distanceText.gameObject.SetActive(hasDistance);

        if (hasDistance)
            distanceText.text = AppStrings.FormatDistance(distanceMetres.Value, approximate);
    }

    static string BuildSubtitle(BuildingData data)
    {
        string category = data.category != null ? data.category.DisplayName : null;
        string year = data.YearBuilt;

        bool hasCategory = !string.IsNullOrWhiteSpace(category);
        bool hasYear = !string.IsNullOrWhiteSpace(year);

        if (hasCategory && hasYear)
            return category + " · " + year;

        if (hasCategory)
            return category;

        return hasYear ? year : string.Empty;
    }

    void HandleClick()
    {
        if (_onClick != null)
            _onClick();
    }
}
