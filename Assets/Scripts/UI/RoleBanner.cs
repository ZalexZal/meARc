using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The website's sign-up stage, as a strip: the role's seal, its chip
/// ("Образование · създател", "Зрител", "Нов профил") and its headline, on the
/// role's own sky. It changes as the answer is given, which is the point - the
/// reader sees what the choice means before saving it.
///
/// The skies and the ink are the website's (lib/stage.ts), day and night; this
/// class paints them itself, because no theme role carries six skies.
/// </summary>
public class RoleBanner : MonoBehaviour
{
    [Tooltip("The sky: the whole strip's background.")]
    public Image sky;

    [Tooltip("The seal's face for a creator: a rounded square.")]
    public Image sealSquare;

    [Tooltip("The seal's face for a viewer, and before any answer: a circle.")]
    public Image sealRound;

    public Image glyph;

    [Tooltip("In GlyphKeys order: teacher, guide, broker, other, viewer, none, creator.")]
    public Sprite[] glyphs = new Sprite[0];

    [Tooltip("The chip's pill behind the tag.")]
    public Image chip;
    public TextMeshProUGUI tagLabel;
    public TextMeshProUGUI headLabel;

    /// <summary>The order of <see cref="glyphs"/>. "creator" is a creator with no line of work yet.</summary>
    public static readonly string[] GlyphKeys =
        { "teacher", "guide", "broker", "other", "viewer", "none", "creator" };

    string _role;
    string _work;

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += Paint;
        AppStrings.OnLanguageChanged += Paint;
        Paint();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Paint;
        AppStrings.OnLanguageChanged -= Paint;
    }

    /// <summary>The answer so far. A viewer's work is ignored, as on the website.</summary>
    public void Show(string role, string work)
    {
        _role = AccountRole.IsValid(role) ? role : null;
        _work = LineOfWork.IsValid(work) ? work : null;
        Paint();
    }

    /// <summary>
    /// The website's stageFor: viewer, a creator's line of work, or "none" for
    /// nobody yet and for a creator who has not said what they do.
    /// </summary>
    public static string StageKey(string role, string work)
    {
        if (role == AccountRole.Viewer)
            return "viewer";

        if (role == AccountRole.Creator && LineOfWork.IsValid(work))
            return work;

        return "none";
    }

    /// <summary>lib/stage.ts SKIES.</summary>
    public static Color Sky(string key, bool night)
    {
        switch (key)
        {
            case "teacher": return Hex(night ? 0x0f2a22 : 0xd3ecd6);
            case "guide": return Hex(night ? 0x2b2114 : 0xf9e2bd);
            case "broker": return Hex(night ? 0x0d2238 : 0xcfe5f8);
            case "other": return Hex(night ? 0x1b2130 : 0xe3e8f0);
            case "viewer": return Hex(night ? 0x0f2a24 : 0xd0ece2);
            default: return Hex(night ? 0x17191c : 0xecedf0);
        }
    }

    public void Paint()
    {
        bool night = ThemeManager.IsDark;
        string key = StageKey(_role, _work);

        if (sky != null)
            sky.color = Sky(key, night);

        // lib/stage.ts TEXT: ink, chip-bg and sign-ink, day and night.
        Color ink = Hex(night ? 0xf5f5f7 : 0x1d1d1f);
        Color face = night ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.8f);
        Color sign = Hex(night ? 0x8fc4ff : 0x0066cc);

        // Square for a creator, round for a viewer and for nobody yet: the
        // website's seal shapes.
        bool square = _role == AccountRole.Creator;

        if (sealSquare != null)
        {
            sealSquare.gameObject.SetActive(square);
            sealSquare.color = face;
        }

        if (sealRound != null)
        {
            sealRound.gameObject.SetActive(!square);
            sealRound.color = face;
        }

        if (glyph != null)
        {
            string glyphKey = _role == AccountRole.Creator && key == "none" ? "creator" : key;
            int index = System.Array.IndexOf(GlyphKeys, glyphKey);
            if (index >= 0 && index < glyphs.Length && glyphs[index] != null)
                glyph.sprite = glyphs[index];
            glyph.color = sign;
        }

        if (chip != null)
            chip.color = face;

        if (tagLabel != null)
        {
            // A creator with no line of work yet is "Създател", not "Нов профил".
            tagLabel.text = _role == AccountRole.Creator && key == "none"
                ? AppStrings.Get("stage.none.tagCreator")
                : AppStrings.Get("stage." + key + ".tag");
            tagLabel.color = ink;
        }

        if (headLabel != null)
        {
            headLabel.text = AppStrings.Get("stage." + key + ".head");
            headLabel.color = ink;
        }
    }

    static Color Hex(int rgb)
    {
        return new Color32((byte)((rgb >> 16) & 0xff), (byte)((rgb >> 8) & 0xff), (byte)(rgb & 0xff), 255);
    }
}
