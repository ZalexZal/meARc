using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates the rounded sprites the interface is drawn with. Unity's default UI
/// sprite is a hard-edged rectangle, which is the single biggest reason a built
/// screen looks nothing like its mockup.
///
/// Each sprite is a small anti-aliased texture imported with a 9-slice border, so
/// one asset renders a card, a button or a capsule at any size without the corner
/// radius stretching.
/// </summary>
public static class UISprites
{
    const string Folder = "Assets/UI";

    /// <summary>Soft-cornered rectangle for cards, fields and buttons.</summary>
    public static Sprite Rounded { get { return GetOrCreate("UIRounded", 64, 20); } }

    /// <summary>Fully round ends, for pills, switch tracks and segmented controls.</summary>
    public static Sprite Pill { get { return GetOrCreate("UIPill", 64, 32); } }

    /// <summary>A plain circle, for dots and switch knobs.</summary>
    public static Sprite Circle { get { return GetOrCreate("UICircle", 64, 32, false); } }

    static Sprite GetOrCreate(string name, int size, int radius, bool sliced = true)
    {
        string path = Folder + "/" + name + ".png";

        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "UI");

        File.WriteAllBytes(path, Render(size, radius).EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;

        // The border is what stops the corners from stretching when the sprite is
        // scaled to a card or a wide button.
        if (sliced)
            importer.spriteBorder = new Vector4(radius, radius, radius, radius);

        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>
    /// Signed-distance rounded rectangle, anti-aliased across one pixel at the edge.
    /// </summary>
    static Texture2D Render(int size, float radius)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size * 0.5f;
        float inner = half - radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }
}
