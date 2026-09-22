using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Makes Inter the app's typeface: Regular for body, SemiBold wherever the UI asks
/// for bold. Inter is the open stand-in for SF Pro and carries Cyrillic, so the
/// Bulgarian strings need nothing extra.
///
/// Dynamic atlases, so the assets start empty and learn each glyph the first
/// time it is drawn - Bulgarian and English both, without baking a character
/// set that someone later finds a missing letter in. LiberationSans stays as the
/// fallback for the odd symbol Inter lacks.
///
/// Called at the start of every UI build, so a fresh checkout makes its own.
/// The SDF assets are git-ignored for that reason: a dynamic atlas grows to
/// megabytes as glyphs are drawn, and every build rebuilds the UI against
/// whatever asset this makes, so nothing depends on a stable GUID.
/// </summary>
public static class AppFonts
{
    const string Folder = "Assets/Fonts/Inter/";

    public static void Ensure()
    {
        TMP_FontAsset regular = Make("Inter-Regular");
        TMP_FontAsset semibold = Make("Inter-SemiBold");

        if (regular == null || semibold == null)
            throw new System.Exception("Inter is missing from " + Folder);

        // FontStyles.Bold asks the weight table for 700. Apple's headline weight
        // is 600, so both slots point at SemiBold - there is no heavier face.
        TMP_FontWeightPair[] weights = regular.fontWeightTable;
        weights[6].regularTypeface = semibold;
        weights[7].regularTypeface = semibold;

        TMP_FontAsset liberation = Resources.Load<TMP_FontAsset>(
            "Fonts & Materials/LiberationSans SDF");

        if (liberation != null)
        {
            if (regular.fallbackFontAssetTable == null)
                regular.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();

            if (semibold.fallbackFontAssetTable == null)
                semibold.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();

            if (!regular.fallbackFontAssetTable.Contains(liberation))
                regular.fallbackFontAssetTable.Add(liberation);

            if (!semibold.fallbackFontAssetTable.Contains(liberation))
                semibold.fallbackFontAssetTable.Add(liberation);
        }

        EditorUtility.SetDirty(regular);
        EditorUtility.SetDirty(semibold);

        TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
        if (settings != null && TMP_Settings.defaultFontAsset != regular)
        {
            TMP_Settings.defaultFontAsset = regular;
            EditorUtility.SetDirty(settings);
        }

        AssetDatabase.SaveAssets();
    }

    static TMP_FontAsset Make(string name)
    {
        string path = Folder + name + " SDF.asset";

        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null)
            return existing;

        Font font = AssetDatabase.LoadAssetAtPath<Font>(Folder + name + ".ttf");
        if (font == null)
            return null;

        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(
            font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, true);

        asset.name = name + " SDF";
        AssetDatabase.CreateAsset(asset, path);

        // The atlas texture and material live inside the font asset, or they are
        // lost on the next domain reload and every label draws as boxes.
        asset.atlasTextures[0].name = name + " Atlas";
        asset.material.name = name + " Material";
        AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
        AssetDatabase.AddObjectToAsset(asset.material, asset);

        AssetDatabase.SaveAssets();
        return asset;
    }
}
