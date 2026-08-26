using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the starter Category and Era assets so the filter pills aren't empty on
/// first run. These are a starting point, not a decision - rename, add and delete
/// them freely; the UI reads whatever is in the folders.
/// </summary>
public static class StarterAssetGenerator
{
    const string CategoriesFolder = "Assets/Resources/Categories";
    const string ErasFolder = "Assets/Resources/Eras";

    [MenuItem("Tools/Building Data/Create Starter Categories and Eras")]
    public static void Generate()
    {
        EnsureFolder("Categories");
        EnsureFolder("Eras");

        int created = 0;

        created += MakeCategory("Residential", "Жилищна");
        created += MakeCategory("Religious", "Религиозна");
        created += MakeCategory("Civic", "Обществена");
        created += MakeCategory("Cultural", "Културна");
        created += MakeCategory("Industrial", "Промишлена");
        created += MakeCategory("Commercial", "Търговска");
        created += MakeCategory("Educational", "Образователна");
        created += MakeCategory("Military", "Военна");

        created += MakeEra("Pre-socialist", "Предсоциалистически", 0, 1943, 1);
        created += MakeEra("Socialist era", "Социалистически период", 1944, 1989, 2);
        created += MakeEra("Transition", "Преход", 1990, 1999, 3);
        created += MakeEra("Modern", "Модерен", 2000, 9999, 4);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string report = created == 0
            ? "Everything already exists - nothing was overwritten."
            : $"Created {created} new asset(s). Existing ones were left untouched.";

        Debug.Log("[Starter Assets] " + report);
        EditorUtility.DisplayDialog("Starter Categories and Eras", report, "OK");
    }

    static void EnsureFolder(string leaf)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        if (!AssetDatabase.IsValidFolder("Assets/Resources/" + leaf))
            AssetDatabase.CreateFolder("Assets/Resources", leaf);
    }

    static int MakeCategory(string english, string bulgarian)
    {
        string path = CategoriesFolder + "/" + english.Replace(" ", "") + ".asset";

        // Never clobber an asset the developer may have already edited.
        if (AssetDatabase.LoadAssetAtPath<BuildingCategory>(path) != null)
            return 0;

        BuildingCategory category = ScriptableObject.CreateInstance<BuildingCategory>();
        category.categoryName = english;
        category.categoryNameBg = bulgarian;

        AssetDatabase.CreateAsset(category, path);
        return 1;
    }

    static int MakeEra(string english, string bulgarian, int start, int end, int order)
    {
        string path = ErasFolder + "/" + english.Replace(" ", "").Replace("-", "") + ".asset";

        if (AssetDatabase.LoadAssetAtPath<EraDefinition>(path) != null)
            return 0;

        EraDefinition era = ScriptableObject.CreateInstance<EraDefinition>();
        era.eraName = english;
        era.eraNameBg = bulgarian;
        era.startYear = start;
        era.endYear = end;
        era.sortOrder = order;

        AssetDatabase.CreateAsset(era, path);
        return 1;
    }
}
