using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public enum AppIcon
{
    MapPin,
    Camera,
    Settings,
    Navigation,
    Close,
    ChevronRight,
    Back,
    MyLocation,
    Block,
    OpenInNew,
    Search,
    List,
    Warning,
    Check,
    Users,

    // Everything a marker's pieces need.
    Play,
    Pause,
    Video,
    Audio,
    Question,
    Lock,

    // The You tab and the badges.
    Person,
    Flag,
    Star,
    Mountain,
    Book,
    Clock,
    Home,
    Trail,

    // Daylight v2. Drawn from the prototype's own outlines, on the same 24 box
    // turned the right way up (SVG counts y down, these count it up).
    ChevronDown,
    ChevronLeft,
    MapFold,
    More,
    Info,
    Hide,
    Move,
    Copy,
    Share,
    Bulb,
    Walk,
    Spark,
    Plus
}

/// <summary>
/// The app's icons, rasterized from vector shapes at import time.
///
/// They are drawn here rather than typed from an icon font because the project's
/// font atlas carries Latin and Cyrillic and nothing else - a Material Icons glyph
/// renders as an empty box on device. The first attempt drew them from stacked
/// primitives in the builder, and the camera came out looking like an apple: a
/// round body with a stem off to one side. Shapes assembled by eye do that.
///
/// Every icon is defined in a 24x24 box, the same one Material and Lucide use, so
/// weights and proportions are comparable rather than invented per icon. Each is a
/// signed distance function, which gives clean antialiasing at any size and makes
/// holes - a camera lens, the eye of a pin - a subtraction rather than a second
/// sprite in the background colour.
/// </summary>
public static class UIIcons
{
    const string Folder = "Assets/UI/Icons";
    const int Resolution = 128;
    const float Box = 24f;

    /// <summary>Stroke width shared by every outlined shape, in box units.</summary>
    const float Stroke = 2f;

    public static Sprite Get(AppIcon icon)
    {
        string path = Folder + "/" + icon + ".png";

        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
            return existing;

        EnsureFolder();

        File.WriteAllBytes(path, Render(Field(icon)).EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            // Batch mode can defer the import; a second pass settles it. Failing
            // here with a name beats a NullReferenceException three frames later.
            AssetDatabase.Refresh();
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }

        if (importer == null)
            throw new System.Exception("Could not import the generated icon at " + path);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true; // icons are drawn at several sizes
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/UI"))
            AssetDatabase.CreateFolder("Assets", "UI");

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/UI", "Icons");
    }

    // ------------------------------------------------------------------ shapes
    //
    // Each returns a signed distance in box units: negative inside, zero on the
    // edge. Union is min, intersection is max, and subtraction is max(a, -b).

    static float Circle(Vector2 p, float cx, float cy, float r)
    {
        return (p - new Vector2(cx, cy)).magnitude - r;
    }

    static float Ring(Vector2 p, float cx, float cy, float r, float thickness)
    {
        return Mathf.Abs((p - new Vector2(cx, cy)).magnitude - r) - thickness * 0.5f;
    }

    static float RoundRect(Vector2 p, float cx, float cy, float halfX, float halfY, float r)
    {
        Vector2 d = new Vector2(Mathf.Abs(p.x - cx) - halfX + r, Mathf.Abs(p.y - cy) - halfY + r);
        return Mathf.Min(Mathf.Max(d.x, d.y), 0f) + Vector2.Max(d, Vector2.zero).magnitude - r;
    }

    /// <summary>A line with round caps, which is what every stroked icon is made of.</summary>
    static float Segment(Vector2 p, float x1, float y1, float x2, float y2, float thickness)
    {
        Vector2 a = new Vector2(x1, y1);
        Vector2 b = new Vector2(x2, y2);

        Vector2 pa = p - a;
        Vector2 ba = b - a;

        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - thickness * 0.5f;
    }

    /// <summary>Filled triangle. Winding is handled, so point order does not matter.</summary>
    static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        // Distance to the boundary, signed by whether the point is on the same side
        // of all three edges.
        float d = Mathf.Min(Mathf.Min(
            Segment(p, a.x, a.y, b.x, b.y, 0f),
            Segment(p, b.x, b.y, c.x, c.y, 0f)),
            Segment(p, c.x, c.y, a.x, a.y, 0f));

        float s1 = Cross(b - a, p - a);
        float s2 = Cross(c - b, p - b);
        float s3 = Cross(a - c, p - c);

        bool inside = (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
        return inside ? -d : d;
    }

    static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    /// <summary>
    /// Keeps only the part of a shape above a line. Turns a ring into an arc,
    /// which is what a question mark and a padlock are both mostly made of.
    /// </summary>
    static float KeepAbove(Vector2 p, float shape, float y)
    {
        return Subtract(shape, p.y - y);
    }

    static float Union(float a, float b)
    {
        return Mathf.Min(a, b);
    }

    static float Subtract(float shape, float hole)
    {
        return Mathf.Max(shape, -hole);
    }

    // ------------------------------------------------------------------- icons

    static Func<Vector2, float> Field(AppIcon icon)
    {
        switch (icon)
        {
            case AppIcon.MapPin:
                return p =>
                {
                    // A teardrop: a disc with a point hanging off the bottom, and a
                    // hole through the middle so it reads as a pin rather than a blob.
                    float head = Circle(p, 12f, 15f, 6.2f);
                    float tail = Triangle(p,
                        new Vector2(12f, 2.5f), new Vector2(6.9f, 11.2f), new Vector2(17.1f, 11.2f));
                    float eye = Circle(p, 12f, 15f, 2.5f);

                    return Subtract(Union(head, tail), eye);
                };

            case AppIcon.Camera:
                return p =>
                {
                    // The hump is centred and wide, and the body barely rounded. The
                    // previous version had a small hump off to one side on a very
                    // round body, which is the recipe for an apple.
                    float body = RoundRect(p, 12f, 10.5f, 9.5f, 6f, 2.2f);
                    float hump = RoundRect(p, 12f, 17f, 4.3f, 1.7f, 1.2f);
                    float lens = Circle(p, 12f, 10.5f, 3.9f);

                    return Subtract(Union(body, hump), lens);
                };

            case AppIcon.Settings:
                return p =>
                {
                    // Sliders, not a gear. A gear at 24 pixels is a grey circle;
                    // three bars with handles survive the size.
                    float bars = Union(Union(
                        Segment(p, 3.5f, 18f, 20.5f, 18f, Stroke),
                        Segment(p, 3.5f, 12f, 20.5f, 12f, Stroke)),
                        Segment(p, 3.5f, 6f, 20.5f, 6f, Stroke));

                    float knobs = Union(Union(
                        RoundRect(p, 8.5f, 18f, 1.6f, 3.4f, 1.4f),
                        RoundRect(p, 15.5f, 12f, 1.6f, 3.4f, 1.4f)),
                        RoundRect(p, 10.5f, 6f, 1.6f, 3.4f, 1.4f));

                    return Union(bars, knobs);
                };

            case AppIcon.Navigation:
                return p =>
                {
                    // The arrowhead every distance in the app points with. Notched at
                    // the base so it reads as a direction rather than a triangle.
                    float left = Triangle(p,
                        new Vector2(12f, 21.5f), new Vector2(4.5f, 3.5f), new Vector2(12f, 7.5f));
                    float right = Triangle(p,
                        new Vector2(12f, 21.5f), new Vector2(12f, 7.5f), new Vector2(19.5f, 3.5f));

                    return Union(left, right);
                };

            case AppIcon.Close:
                return p => Union(
                    Segment(p, 6.5f, 6.5f, 17.5f, 17.5f, Stroke),
                    Segment(p, 17.5f, 6.5f, 6.5f, 17.5f, Stroke));

            case AppIcon.ChevronRight:
                return p => Union(
                    Segment(p, 9.5f, 18.5f, 15.5f, 12f, Stroke),
                    Segment(p, 15.5f, 12f, 9.5f, 5.5f, Stroke));

            case AppIcon.Back:
                return p => Union(Union(
                    Segment(p, 4.5f, 12f, 19.5f, 12f, Stroke),
                    Segment(p, 4.5f, 12f, 10.5f, 18f, Stroke)),
                    Segment(p, 4.5f, 12f, 10.5f, 6f, Stroke));

            case AppIcon.MyLocation:
                return p =>
                {
                    float ring = Ring(p, 12f, 12f, 5f, Stroke * 0.9f);
                    float core = Circle(p, 12f, 12f, 2.1f);

                    float ticks = Union(Union(Union(
                        Segment(p, 12f, 18.2f, 12f, 21.5f, Stroke * 0.9f),
                        Segment(p, 12f, 2.5f, 12f, 5.8f, Stroke * 0.9f)),
                        Segment(p, 18.2f, 12f, 21.5f, 12f, Stroke * 0.9f)),
                        Segment(p, 2.5f, 12f, 5.8f, 12f, Stroke * 0.9f));

                    return Union(Union(ring, core), ticks);
                };

            case AppIcon.Block:
                return p => Union(
                    Ring(p, 12f, 12f, 8f, Stroke),
                    Segment(p, 6.4f, 17.6f, 17.6f, 6.4f, Stroke));

            case AppIcon.OpenInNew:
                return p =>
                {
                    // An open-cornered frame with an arrow leaving it. The gap is
                    // what says "somewhere else" rather than "a box".
                    float frame = Subtract(
                        RoundRect(p, 10.5f, 10.5f, 7.5f, 7.5f, 2.2f),
                        RoundRect(p, 10.5f, 10.5f, 5.5f, 5.5f, 1.2f));

                    // Opens the top-right corner so the arrow can pass through it.
                    frame = Subtract(frame, RoundRect(p, 17f, 17f, 5f, 5f, 0f));

                    float arrow = Union(
                        Segment(p, 13f, 13f, 20f, 20f, Stroke),
                        Triangle(p, new Vector2(21f, 21f),
                                    new Vector2(14.5f, 21f), new Vector2(21f, 14.5f)));

                    return Union(frame, arrow);
                };

            case AppIcon.Search:
                return p => Union(
                    Ring(p, 10.5f, 13.5f, 5.5f, Stroke),
                    Segment(p, 14.6f, 9.4f, 19.5f, 4.5f, Stroke * 1.1f));

            case AppIcon.List:
                return p =>
                {
                    float lines = Union(Union(
                        Segment(p, 9f, 18f, 20.5f, 18f, Stroke),
                        Segment(p, 9f, 12f, 20.5f, 12f, Stroke)),
                        Segment(p, 9f, 6f, 20.5f, 6f, Stroke));

                    float dots = Union(Union(
                        Circle(p, 4.6f, 18f, 1.6f),
                        Circle(p, 4.6f, 12f, 1.6f)),
                        Circle(p, 4.6f, 6f, 1.6f));

                    return Union(lines, dots);
                };

            case AppIcon.Users:
                return p =>
                {
                    // Two people: a head and a pair of shoulders each, the second
                    // set behind and smaller. Shoulders are thick capsules rather
                    // than arcs because a stroked arc at 38 pixels reads as a
                    // smudge, and a capsule keeps its shape all the way down.
                    float front = Union(
                        Circle(p, 9.5f, 16.5f, 4f),
                        Segment(p, 6f, 6f, 13f, 6f, 8f));

                    float behind = Union(
                        Circle(p, 17.6f, 17.2f, 3.1f),
                        Segment(p, 15.8f, 7f, 20f, 7f, 6.4f));

                    return Union(front, behind);
                };

            case AppIcon.Warning:
                return p =>
                {
                    float body = Triangle(p,
                        new Vector2(12f, 21f), new Vector2(1.8f, 3.5f), new Vector2(22.2f, 3.5f));

                    float bar = RoundRect(p, 12f, 13.6f, 1.05f, 3.2f, 1.05f);
                    float dot = Circle(p, 12f, 6.6f, 1.15f);

                    return Subtract(body, Union(bar, dot));
                };

            case AppIcon.Play:
                return p => Triangle(p,
                    new Vector2(7.6f, 4.6f), new Vector2(7.6f, 19.4f), new Vector2(19f, 12f));

            case AppIcon.Pause:
                return p => Union(
                    RoundRect(p, 9f, 12f, 1.75f, 7.2f, 1.1f),
                    RoundRect(p, 15f, 12f, 1.75f, 7.2f, 1.1f));

            case AppIcon.Video:
                return p =>
                {
                    // A frame with a play mark in it, rather than a camcorder. A
                    // camcorder body and lens is two blobs at 44 pixels; this stays
                    // legible because the triangle survives being small.
                    float frame = Subtract(
                        RoundRect(p, 12f, 12f, 9.4f, 6.8f, 2.6f),
                        RoundRect(p, 12f, 12f, 9.4f - Stroke, 6.8f - Stroke, 1.8f));

                    float play = Triangle(p,
                        new Vector2(10.2f, 8.3f), new Vector2(10.2f, 15.7f),
                        new Vector2(16.2f, 12f));

                    return Union(frame, play);
                };

            case AppIcon.Audio:
                return p =>
                {
                    // A level meter. Headphones say "the device"; bars say "there
                    // is a recording here", which is the thing being offered.
                    float bars = Union(Union(Union(Union(
                        RoundRect(p, 4.6f, 12f, 1.05f, 2.4f, 1.05f),
                        RoundRect(p, 8.3f, 12f, 1.05f, 4.8f, 1.05f)),
                        RoundRect(p, 12f, 12f, 1.05f, 7.2f, 1.05f)),
                        RoundRect(p, 15.7f, 12f, 1.05f, 4.2f, 1.05f)),
                        RoundRect(p, 19.4f, 12f, 1.05f, 2.9f, 1.05f));

                    return bars;
                };

            case AppIcon.Question:
                return p =>
                {
                    // Built the way the glyph is: an arc over the top, a stroke
                    // down into the centre, a gap, a dot. KeepAbove is what turns
                    // a whole ring into the arc.
                    // The arc is cut below its own centre, so both ends turn down
                    // the way the printed glyph does. Cut on the centreline it
                    // reads as a croquet hoop.
                    float arc = KeepAbove(p, Ring(p, 12f, 15f, 3.6f, Stroke), 14.2f);

                    float descender = Union(
                        Segment(p, 15.6f, 14.6f, 15.2f, 12.9f, Stroke),
                        Segment(p, 15.2f, 12.9f, 12f, 10.9f, Stroke));

                    float stem = Segment(p, 12f, 10.9f, 12f, 8.6f, Stroke);
                    float dot = Circle(p, 12f, 5.2f, 1.35f);

                    return Union(Union(arc, descender), Union(stem, dot));
                };

            case AppIcon.Lock:
                return p =>
                {
                    float shackle = KeepAbove(p, Ring(p, 12f, 14.2f, 3.9f, Stroke), 14.2f);
                    float body = RoundRect(p, 12f, 9.4f, 6.4f, 4.8f, 1.9f);

                    return Union(shackle, body);
                };

            case AppIcon.Person:
                return p => Union(
                    Circle(p, 12f, 15.6f, 4.3f),
                    Segment(p, 7.2f, 5.6f, 16.8f, 5.6f, 8.6f));

            case AppIcon.Flag:
                return p =>
                {
                    float pole = Segment(p, 6f, 2.5f, 6f, 21f, Stroke);
                    float cloth = Triangle(p,
                        new Vector2(6.8f, 20.5f), new Vector2(6.8f, 11f), new Vector2(18.5f, 15.7f));
                    return Union(pole, cloth);
                };

            case AppIcon.Star:
                return p =>
                {
                    // Two triangles, one pointing up, one down: a star at icon
                    // size, without a five-point polygon to rasterize.
                    float up = Triangle(p,
                        new Vector2(12f, 21.2f), new Vector2(4.2f, 7.6f), new Vector2(19.8f, 7.6f));
                    float down = Triangle(p,
                        new Vector2(12f, 3.2f), new Vector2(19.6f, 16.2f), new Vector2(4.4f, 16.2f));
                    return Union(up, down);
                };

            case AppIcon.Mountain:
                return p =>
                {
                    float peak = Triangle(p,
                        new Vector2(9.5f, 18.5f), new Vector2(2.5f, 5f), new Vector2(16.5f, 5f));
                    float second = Triangle(p,
                        new Vector2(16f, 13.5f), new Vector2(10.5f, 5f), new Vector2(21.5f, 5f));
                    return Union(peak, second);
                };

            case AppIcon.Book:
                return p =>
                {
                    float left = Segment(p, 3.5f, 17f, 11.2f, 15.2f, Stroke);
                    float right = Segment(p, 12.8f, 15.2f, 20.5f, 17f, Stroke);
                    float leftBottom = Segment(p, 3.5f, 6.5f, 11.2f, 4.8f, Stroke);
                    float rightBottom = Segment(p, 12.8f, 4.8f, 20.5f, 6.5f, Stroke);
                    float spine = Segment(p, 12f, 4.5f, 12f, 15.5f, Stroke);
                    float sides = Union(Segment(p, 3.5f, 6.5f, 3.5f, 17f, Stroke),
                                        Segment(p, 20.5f, 6.5f, 20.5f, 17f, Stroke));
                    return Union(Union(Union(left, right), Union(leftBottom, rightBottom)),
                                 Union(spine, sides));
                };

            case AppIcon.Clock:
                return p => Union(Ring(p, 12f, 12f, 8.2f, Stroke),
                                  Union(Segment(p, 12f, 12f, 12f, 17f, Stroke),
                                        Segment(p, 12f, 12f, 15.5f, 10f, Stroke)));

            case AppIcon.Home:
                return p =>
                {
                    float roof = Union(Segment(p, 3.5f, 11.5f, 12f, 19.5f, Stroke),
                                       Segment(p, 12f, 19.5f, 20.5f, 11.5f, Stroke));
                    float walls = Union(Segment(p, 6f, 4f, 6f, 13f, Stroke),
                                        Segment(p, 18f, 4f, 18f, 13f, Stroke));
                    float floor = Segment(p, 6f, 4f, 18f, 4f, Stroke);
                    return Union(Union(roof, walls), floor);
                };

            case AppIcon.Trail:
                return p => Union(Union(
                    Segment(p, 3.5f, 5f, 8.5f, 15f, Stroke),
                    Segment(p, 8.5f, 15f, 13f, 9f, Stroke)),
                    Union(Segment(p, 13f, 9f, 16f, 13f, Stroke),
                          Segment(p, 16f, 13f, 20.5f, 5f, Stroke)));

            case AppIcon.ChevronDown:
                return p => Union(
                    Segment(p, 6f, 15f, 12f, 9f, Stroke),
                    Segment(p, 12f, 9f, 18f, 15f, Stroke));

            case AppIcon.ChevronLeft:
                return p => Union(
                    Segment(p, 15f, 18f, 9f, 12f, Stroke),
                    Segment(p, 9f, 12f, 15f, 6f, Stroke));

            case AppIcon.MapFold:
                return p =>
                {
                    // A folded map: three panels, the middle one standing proud.
                    float s1 = Segment(p, 9f, 20f, 3f, 18f, Stroke * 0.9f);
                    float s2 = Segment(p, 3f, 18f, 3f, 4f, Stroke * 0.9f);
                    float s3 = Segment(p, 3f, 4f, 9f, 6f, Stroke * 0.9f);
                    float s4 = Segment(p, 9f, 6f, 15f, 4f, Stroke * 0.9f);
                    float s5 = Segment(p, 15f, 4f, 21f, 6f, Stroke * 0.9f);
                    float s6 = Segment(p, 21f, 6f, 21f, 20f, Stroke * 0.9f);
                    float s7 = Segment(p, 21f, 20f, 15f, 18f, Stroke * 0.9f);
                    float s8 = Segment(p, 15f, 18f, 9f, 20f, Stroke * 0.9f);
                    float folds = Union(Segment(p, 9f, 20f, 9f, 6f, Stroke * 0.9f),
                                        Segment(p, 15f, 18f, 15f, 4f, Stroke * 0.9f));
                    return Union(Union(Union(Union(s1, s2), Union(s3, s4)),
                                       Union(Union(s5, s6), Union(s7, s8))), folds);
                };

            case AppIcon.More:
                return p => Union(Union(
                    Circle(p, 5f, 12f, 1.6f), Circle(p, 12f, 12f, 1.6f)), Circle(p, 19f, 12f, 1.6f));

            case AppIcon.Info:
                return p => Union(Union(
                    Ring(p, 12f, 12f, 9f, Stroke * 0.9f),
                    Segment(p, 12f, 13f, 12f, 7f, Stroke * 0.9f)),
                    Circle(p, 12f, 16.5f, 1.2f));

            case AppIcon.Hide:
                return p =>
                {
                    // An eye - the almond where two circles overlap - with the
                    // stroke through it that every app reads as "not shown".
                    float almond = Mathf.Max(Circle(p, 12f, 4f, 11f), Circle(p, 12f, 20f, 11f));
                    float lid = Mathf.Abs(almond) - Stroke * 0.45f;
                    float pupil = Ring(p, 12f, 12f, 2.6f, Stroke * 0.9f);
                    float eye = Union(lid, pupil);
                    // A gap either side of the slash, so it reads as cut.
                    eye = Subtract(eye, Segment(p, 3.5f, 20.5f, 20.5f, 3.5f, Stroke * 2.6f));
                    return Union(eye, Segment(p, 3.5f, 20.5f, 20.5f, 3.5f, Stroke));
                };

            case AppIcon.Move:
                return p =>
                {
                    float cross = Union(Segment(p, 12f, 3f, 12f, 21f, Stroke * 0.9f),
                                        Segment(p, 3f, 12f, 21f, 12f, Stroke * 0.9f));
                    float up = Union(Segment(p, 8.5f, 17.5f, 12f, 21f, Stroke * 0.9f),
                                     Segment(p, 12f, 21f, 15.5f, 17.5f, Stroke * 0.9f));
                    float down = Union(Segment(p, 8.5f, 6.5f, 12f, 3f, Stroke * 0.9f),
                                       Segment(p, 12f, 3f, 15.5f, 6.5f, Stroke * 0.9f));
                    float left = Union(Segment(p, 6.5f, 15.5f, 3f, 12f, Stroke * 0.9f),
                                       Segment(p, 3f, 12f, 6.5f, 8.5f, Stroke * 0.9f));
                    float right = Union(Segment(p, 17.5f, 15.5f, 21f, 12f, Stroke * 0.9f),
                                        Segment(p, 21f, 12f, 17.5f, 8.5f, Stroke * 0.9f));
                    return Union(Union(cross, Union(up, down)), Union(left, right));
                };

            case AppIcon.Copy:
                return p =>
                {
                    float front = Subtract(RoundRect(p, 14f, 10f, 6f, 6f, 2f),
                                           RoundRect(p, 14f, 10f, 6f - Stroke * 0.9f, 6f - Stroke * 0.9f, 1.2f));
                    float back = Subtract(RoundRect(p, 10f, 14f, 6f, 6f, 2f),
                                          RoundRect(p, 10f, 14f, 6f - Stroke * 0.9f, 6f - Stroke * 0.9f, 1.2f));
                    // The back sheet only where the front one is not.
                    back = Subtract(back, RoundRect(p, 14f, 10f, 7.4f, 7.4f, 2f));
                    return Union(front, back);
                };

            case AppIcon.Share:
                return p =>
                {
                    float shaft = Segment(p, 12f, 9f, 12f, 21f, Stroke * 0.9f);
                    float head = Union(Segment(p, 7f, 16f, 12f, 21f, Stroke * 0.9f),
                                       Segment(p, 12f, 21f, 17f, 16f, Stroke * 0.9f));
                    float tray = Union(Union(Segment(p, 5f, 11f, 5f, 5f, Stroke * 0.9f),
                                             Segment(p, 5f, 5f, 19f, 5f, Stroke * 0.9f)),
                                       Segment(p, 19f, 5f, 19f, 11f, Stroke * 0.9f));
                    return Union(Union(shaft, head), tray);
                };

            case AppIcon.Bulb:
                return p =>
                {
                    float glass = KeepAbove(p, Ring(p, 12f, 14.6f, 5.6f, Stroke * 0.9f), 11.2f);
                    float neck = Union(Segment(p, 7.9f, 10.9f, 9.2f, 7.6f, Stroke * 0.9f),
                                       Segment(p, 16.1f, 10.9f, 14.8f, 7.6f, Stroke * 0.9f));
                    float base1 = Segment(p, 9f, 6f, 15f, 6f, Stroke * 0.9f);
                    float base2 = Segment(p, 10f, 3f, 14f, 3f, Stroke * 0.9f);
                    return Union(Union(glass, neck), Union(base1, base2));
                };

            case AppIcon.Walk:
                return p =>
                {
                    float head = Circle(p, 13f, 19.5f, 1.9f);
                    float leg1 = Union(Segment(p, 10f, 3f, 12f, 9f, Stroke * 0.9f),
                                       Segment(p, 12f, 9f, 15f, 6f, Stroke * 0.9f));
                    float leg2 = Segment(p, 15f, 6f, 15f, 3f, Stroke * 0.9f);
                    float arm1 = Union(Segment(p, 8f, 13f, 11f, 16f, Stroke * 0.9f),
                                       Segment(p, 11f, 16f, 14f, 15f, Stroke * 0.9f));
                    float arm2 = Union(Segment(p, 14f, 15f, 16f, 12f, Stroke * 0.9f),
                                       Segment(p, 16f, 12f, 19f, 12f, Stroke * 0.9f));
                    float body = Segment(p, 11f, 16f, 9.5f, 10f, Stroke * 0.9f);
                    return Union(Union(Union(head, leg1), Union(leg2, arm1)), Union(arm2, body));
                };

            case AppIcon.Spark:
                return p =>
                {
                    float a = Union(Segment(p, 12f, 21f, 12f, 17f, Stroke * 0.9f),
                                    Segment(p, 12f, 7f, 12f, 3f, Stroke * 0.9f));
                    float b = Union(Segment(p, 3f, 12f, 7f, 12f, Stroke * 0.9f),
                                    Segment(p, 17f, 12f, 21f, 12f, Stroke * 0.9f));
                    float c = Union(Segment(p, 6f, 18f, 8.5f, 15.5f, Stroke * 0.9f),
                                    Segment(p, 15.5f, 8.5f, 18f, 6f, Stroke * 0.9f));
                    float d = Union(Segment(p, 6f, 6f, 8.5f, 8.5f, Stroke * 0.9f),
                                    Segment(p, 15.5f, 15.5f, 18f, 18f, Stroke * 0.9f));
                    return Union(Union(a, b), Union(c, d));
                };

            case AppIcon.Plus:
                return p => Union(Segment(p, 12f, 5f, 12f, 19f, Stroke * 1.1f),
                                  Segment(p, 5f, 12f, 19f, 12f, Stroke * 1.1f));

            default: // Check
                return p => Union(
                    Segment(p, 4.5f, 12.5f, 9.8f, 6.8f, Stroke * 1.2f),
                    Segment(p, 9.8f, 6.8f, 19.5f, 17.5f, Stroke * 1.2f));
        }
    }

    // --------------------------------------------------------------- rasterizer

    static Texture2D Render(Func<Vector2, float> field)
    {
        Texture2D texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, true);

        float unitsPerPixel = Box / Resolution;
        float pixelsPerUnit = Resolution / Box;

        Color[] pixels = new Color[Resolution * Resolution];

        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                Vector2 point = new Vector2((x + 0.5f) * unitsPerPixel, (y + 0.5f) * unitsPerPixel);

                // The distance is already in box units, so one multiply converts it
                // to pixels and the edge fades across exactly one of them.
                float alpha = Mathf.Clamp01(0.5f - field(point) * pixelsPerUnit);

                pixels[y * Resolution + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }
}
