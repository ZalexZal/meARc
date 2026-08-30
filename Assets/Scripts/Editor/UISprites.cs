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
    public static Sprite Rounded { get { return GetOrCreate("UIRounded28", 96, 28); } }

    /// <summary>Fully round ends, for pills, switch tracks and segmented controls.</summary>
    public static Sprite Pill { get { return GetOrCreate("UIPill", 64, 32); } }

    /// <summary>A plain circle, for dots and switch knobs.</summary>
    public static Sprite Circle { get { return GetOrCreate("UICircle", 64, 32, false); } }

    /// <summary>
    /// A ring. This is what separates a marker from whatever is behind it, so it
    /// has to be a real hole rather than a circle in the background colour - the
    /// map, the camera feed and the sheet are three different backgrounds.
    /// </summary>
    public static Sprite Ring { get { return GetOrCreateRing("UIRing", 64, 7); } }

    /// <summary>
    /// A triangle pointing up. Kept for anything that needs a bare wedge; the
    /// direction arrows use the icon set.
    /// </summary>
    public static Sprite Triangle { get { return GetOrCreateTriangle("UITriangle", 64); } }

    /// <summary>
    /// The cone of light in front of the reader, as Google Maps and Waze draw it:
    /// bright at the dot and fading out into the distance.
    ///
    /// A flat triangle would read as a solid object sitting on the map. The fade is
    /// what makes it read as a direction instead.
    /// </summary>
    public static Sprite Beam { get { return GetOrCreateBeam("UIBeam", 128); } }

    /// <summary>
    /// The border of a rounded card, with nothing inside it.
    ///
    /// A card floating over a map needs an edge. Without one it reads as a hole in
    /// the screen rather than as a surface above it, and that is most of what makes
    /// a screen look unfinished.
    /// </summary>
    public static Sprite RoundedOutline
    {
        get { return GetOrCreateOutline("UIRoundedOutline", 96, 28f, 3f); }
    }

    static Sprite GetOrCreateRing(string name, int size, float thickness)
    {
        return Save(name, RenderRing(size, thickness), 0);
    }

    static Sprite GetOrCreateOutline(string name, int size, float radius, float thickness)
    {
        return Save(name, RenderOutline(size, radius, thickness), Mathf.CeilToInt(radius));
    }

    static Sprite GetOrCreateBeam(string name, int size)
    {
        return Save(name, RenderBeam(size), 0);
    }

    static Sprite GetOrCreateTriangle(string name, int size)
    {
        return Save(name, RenderTriangle(size), 0);
    }

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

    /// <summary>Shared write-and-import step, so every generated sprite lands the same way.</summary>
    static Sprite Save(string name, Texture2D texture, int border)
    {
        string path = Folder + "/" + name + ".png";

        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "UI");

        File.WriteAllBytes(path, texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;

        if (border > 0)
            importer.spriteBorder = new Vector4(border, border, border, border);

        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>
    /// A rounded rectangle drawn as an outline. Same signed-distance field as the
    /// filled one, taken in absolute value so only the edge is left.
    /// </summary>
    /// <summary>
    /// A wedge with its point at the bottom centre, opening upwards, fading out as
    /// it goes. The spread is set by the rect it is drawn into, not by the sprite,
    /// so the same texture serves any field of view.
    /// </summary>
    static Texture2D RenderBeam(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        float apexX = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            // Height above the point, zero to one.
            float up = (y + 0.5f) / size;

            // The wedge opens to the full width of the texture at the top.
            float halfWidth = up * size * 0.5f;

            // Bright at the reader, gone by the far edge. Squared so it holds its
            // brightness near the dot instead of fading immediately.
            float strength = (1f - up) * (1f - up) * 0.9f + 0.1f;

            for (int x = 0; x < size; x++)
            {
                float across = Mathf.Abs(x + 0.5f - apexX);

                // One pixel of softness on the sloped edges, same as everywhere else.
                float edge = Mathf.Clamp01(halfWidth - across + 0.5f);

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, edge * strength));
            }
        }

        texture.Apply();
        return texture;
    }

    static Texture2D RenderOutline(int size, float radius, float thickness)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size * 0.5f;
        float inner = half - radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // The standard rounded-rectangle distance: negative inside,
                // positive outside, and correct in the middle rather than only
                // near the edge. Its absolute value is what leaves a border.
                float qx = Mathf.Abs(x + 0.5f - half) - inner;
                float qy = Mathf.Abs(y + 0.5f - half) - inner;

                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float distance = Mathf.Min(Mathf.Max(qx, qy), 0f) + outside - radius;

                float alpha = Mathf.Clamp01(thickness * 0.5f - Mathf.Abs(distance) + 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }

    static Texture2D RenderRing(int size, float thickness)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size * 0.5f;
        float outer = half - 1f;
        float inner = outer - thickness;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha = Mathf.Clamp01(outer - distance + 0.5f) *
                              Mathf.Clamp01(distance - inner + 0.5f);

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }

    static Texture2D RenderTriangle(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        // Apex at the top centre, base along the bottom, inset a pixel so the
        // anti-aliased edge has somewhere to fade into.
        Vector2 apex = new Vector2(size * 0.5f, size - 1f);
        Vector2 left = new Vector2(1f, 1f);
        Vector2 right = new Vector2(size - 1f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2(x + 0.5f, y + 0.5f);

                float inside = Mathf.Min(
                    Mathf.Min(Edge(point, left, right), Edge(point, right, apex)),
                    Edge(point, apex, left));

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(inside + 0.5f)));
            }
        }

        texture.Apply();
        return texture;
    }

    /// <summary>Signed distance from a point to the line through a and b, positive inside.</summary>
    static float Edge(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        float length = edge.magnitude;

        if (length < 0.0001f)
            return 0f;

        return (edge.x * (point.y - a.y) - edge.y * (point.x - a.x)) / length;
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
