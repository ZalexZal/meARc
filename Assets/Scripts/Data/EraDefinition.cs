using UnityEngine;

/// <summary>
/// A named span of years used to group buildings. Boundaries live in the asset so
/// they can be argued with and edited without a rebuild.
/// </summary>
[CreateAssetMenu(fileName = "NewEra", menuName = "Building Data/Era")]
public class EraDefinition : ScriptableObject
{
    [Tooltip("English label, e.g. \"Socialist era\".")]
    public string eraName;

    [Tooltip("Bulgarian label, e.g. \"Социалистически период\".")]
    public string eraNameBg;

    [Tooltip("First year in this era, inclusive.")]
    public int startYear;

    [Tooltip("Last year in this era, inclusive. Use 9999 for \"and later\".")]
    public int endYear = 9999;

    [Tooltip("Controls the left-to-right order of the era pills. Lower comes first.")]
    public int sortOrder;

    /// <summary>Label for the language currently selected in Settings.</summary>
    public string DisplayName
    {
        get
        {
            if (AppStrings.CurrentLanguage == AppLanguage.Bulgarian &&
                !string.IsNullOrWhiteSpace(eraNameBg))
            {
                return eraNameBg;
            }

            return string.IsNullOrWhiteSpace(eraName) ? name : eraName;
        }
    }

    public bool Contains(int year)
    {
        return year >= startYear && year <= endYear;
    }
}
