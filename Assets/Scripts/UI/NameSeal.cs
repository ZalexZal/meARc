using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The small seal beside a signed-in name: the website's NameSeal.
///
///   creator  a rounded square holding the line of work's glyph
///            (the pencil while the work is not known yet)
///   viewer   a circle holding the eye
///   null     nothing at all - somebody who never answered gets no seal
///
/// Near-black with a white glyph by day, the other way round at night.
/// Follows the signed-in account on its own (SupabaseAuth.OnWorkChanged), so a
/// role changed in the role sheet repaints it at once; Show() sets it by hand
/// for the preview harness.
/// </summary>
public class NameSeal : MonoBehaviour
{
    [Tooltip("The face for a creator: a rounded square.")]
    public Image square;

    [Tooltip("The face for a viewer: a circle.")]
    public Image round;

    public Image glyph;

    [Tooltip("In GlyphKeys order: teacher, guide, broker, other, viewer, pencil.")]
    public Sprite[] glyphs = new Sprite[0];

    [Tooltip("Off for the preview harness, which calls Show itself.")]
    public bool followAccount = true;

    /// <summary>The order of <see cref="glyphs"/>. "pencil" is a creator with no line of work yet.</summary>
    public static readonly string[] GlyphKeys = { "teacher", "guide", "broker", "other", "viewer", "pencil" };

    static readonly Color Ink = new Color32(0x1d, 0x1d, 0x1f, 0xff);
    static readonly Color Paper = new Color32(0xf5, 0xf5, 0xf7, 0xff);

    string _role;
    string _work;

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += Paint;

        if (followAccount)
        {
            SupabaseAuth.OnWorkChanged += FromAccount;
            SupabaseAuth.OnSessionChanged += FromAccount;
            FromAccount();
        }
        else
        {
            Paint();
        }
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Paint;
        SupabaseAuth.OnWorkChanged -= FromAccount;
        SupabaseAuth.OnSessionChanged -= FromAccount;
    }

    /// <summary>A guest has no role, so no seal.</summary>
    void FromAccount()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        bool person = auth != null && auth.IsSignedIn && !auth.IsAnonymous;
        Show(person ? auth.Role : null, person ? auth.Work : null);
    }

    /// <summary>The role and work to show. A viewer's work is ignored, as on the website.</summary>
    public void Show(string role, string work)
    {
        _role = AccountRole.IsValid(role) ? role : null;
        _work = LineOfWork.IsValid(work) ? work : null;
        Paint();
    }

    /// <summary>The website's sealState glyph: the work, the eye, or the pencil.</summary>
    public static string GlyphKey(string role, string work)
    {
        if (role == AccountRole.Viewer)
            return "viewer";

        if (role == AccountRole.Creator)
            return LineOfWork.IsValid(work) ? work : "pencil";

        return null;
    }

    public void Paint()
    {
        string key = GlyphKey(_role, _work);
        bool shown = key != null;

        // The faces, not the whole object: this component has to stay enabled
        // to hear the role arrive.
        bool creator = _role == AccountRole.Creator;
        bool night = ThemeManager.IsDark;
        Color face = night ? Paper : Ink;
        Color mark = night ? Ink : Color.white;

        if (square != null)
        {
            Toggle(square.gameObject, shown && creator);
            square.color = face;
        }

        if (round != null)
        {
            Toggle(round.gameObject, shown && !creator);
            round.color = face;
        }

        if (glyph != null)
        {
            Toggle(glyph.gameObject, shown);

            int index = shown ? System.Array.IndexOf(GlyphKeys, key) : -1;
            if (index >= 0 && index < glyphs.Length && glyphs[index] != null)
                glyph.sprite = glyphs[index];

            glyph.color = mark;
        }

        // Folded out of a layout row while there is nothing to show, so the name
        // beside it does not keep a gap.
        LayoutElement element = GetComponent<LayoutElement>();
        if (element != null && element.ignoreLayout == shown)
            element.ignoreLayout = !shown;
    }

    static void Toggle(GameObject go, bool on)
    {
        if (go.activeSelf != on)
            go.SetActive(on);
    }
}
