using UnityEngine;

/// <summary>
/// A filterable building type. Categories are assets rather than an enum so new
/// ones can be invented without touching code - creating the asset is what makes
/// the filter pill appear in the Search tab.
/// </summary>
[CreateAssetMenu(fileName = "NewCategory", menuName = "Building Data/Category")]
public class BuildingCategory : ScriptableObject
{
    [Tooltip("English label, e.g. \"Residential\".")]
    public string categoryName;

    [Tooltip("Bulgarian label, e.g. \"Жилищна\".")]
    public string categoryNameBg;

    [Tooltip("Optional free-text key for a future icon lookup. Unused for now.")]
    public string iconName;

    [Header("Marker")]
    [Tooltip("Style key this category's markers use. Must match one of the keys on " +
             "the MarkerSpawner. Blank falls back to the spawner's default prefab.")]
    public string markerStyle;

    [Tooltip("Tint applied to this category's markers. Lets colour-coded " +
             "classification work before any marker art exists.")]
    public Color markerColour = Color.white;

    /// <summary>Label for the language currently selected in Settings.</summary>
    public string DisplayName
    {
        get
        {
            if (AppStrings.CurrentLanguage == AppLanguage.Bulgarian &&
                !string.IsNullOrWhiteSpace(categoryNameBg))
            {
                return categoryNameBg;
            }

            return string.IsNullOrWhiteSpace(categoryName) ? name : categoryName;
        }
    }
}
