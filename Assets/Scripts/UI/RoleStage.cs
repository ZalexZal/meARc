using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The website's A5 sign-up stage, phone layout: the role's sky with the sun
/// (or the moon and stars at night) and a drifting cloud, the chip, the
/// headline and the three signs, and at the bottom the role's scene, where a
/// blue trail draws itself, two pins drop and a "moment" pill pops in.
///
/// Two states are kept apart, as on the website (rolepick.ts):
/// - the words (chip, headline, signs) follow the answer: <see cref="StageKey"/>;
/// - the drawings (sky colour, scene, moment) follow the "art" key, which is the
///   answer once there is one, and before that walks through the five roles
///   every 7 s so the stage is never empty.
///
/// Colours are the website's (lib/stage.ts), day and night; the timings are
/// stage.css's stage-p5* and stage-a5twinkle keyframes. The hierarchy is built
/// by AppUIBuilder; this class only paints and moves it.
/// </summary>
public class RoleStage : MonoBehaviour
{
    [Header("Sky")]
    [Tooltip("The full hero background.")]
    public Image sky;
    public Image sun, moon;
    public Image[] stars = new Image[0];
    [Tooltip("The cloud; its Image (on it or a child) is tinted.")]
    public RectTransform cloud;

    [Header("Words")]
    [Tooltip("Optional: the chip's pill behind the seal and the tag.")]
    public Image chip;
    public Image chipSeal;

    [Tooltip("The seal's shape follows the role, as the website's: a rounded square " +
             "for a creator, a circle for a viewer, a ring before any answer.")]
    public Sprite sealSquare;
    public Sprite sealRound;
    public Sprite sealRing;

    public Image chipGlyph;
    public TextMeshProUGUI chipLabel;
    public TextMeshProUGUI headLabel;
    public RectTransform signsRow;
    [Tooltip("Three each. A pill's Image (on its GameObject) is its background.")]
    public Image[] signIcons = new Image[0];
    public TextMeshProUGUI[] signLabels = new TextMeshProUGUI[0];
    public GameObject[] signPills = new GameObject[0];

    [Header("Scene")]
    [Tooltip("The scene sprite: Simple, preserveAspect, 480:200.")]
    public Image scene;
    [Tooltip("Filled, Horizontal, origin Left; same frame as the scene.")]
    public Image trail;
    [Tooltip("Two, inside the scene's frame. Each gets a CanvasGroup if it has none.")]
    public RectTransform[] pins = new RectTransform[0];
    public RectTransform moment;
    public Image momentIcon;
    public TextMeshProUGUI momentLabel;
    public CanvasGroup momentGroup;
    [Tooltip("Faded in when the drawings change.")]
    public CanvasGroup sceneGroup;

    [Header("Sprites")]
    [Tooltip("Index = KeyIndex: guide, teacher, broker, other, viewer.")]
    public Sprite[] sceneDay = new Sprite[0];
    public Sprite[] sceneNight = new Sprite[0];
    [Tooltip("icon-<name>.png sprites, paired with iconNames by index.")]
    public Sprite[] icons = new Sprite[0];
    public string[] iconNames = new string[0];

    /// <summary>The order of <see cref="sceneDay"/> and <see cref="sceneNight"/>.</summary>
    public static readonly string[] Keys = { "guide", "teacher", "broker", "other", "viewer" };

    /// <summary>rolepick.ts PREVIEW: the drawings' walk before a choice.</summary>
    public static readonly string[] PreviewOrder = { "guide", "teacher", "broker", "viewer", "other" };

    /// <summary>One loop of the trail, pins and moment, and one preview step (s).</summary>
    public const float LoopSeconds = 7f;
    /// <summary>The sky's ease and the scene's fade-in (s).</summary>
    public const float ChangeSeconds = 0.6f;

    const float PinStagger = 0.6f;
    const float CloudSeconds = 14f;
    const float TwinkleSeconds = 3f;
    static readonly float[] StarDelays = { 0f, 1f, 2f };

    // The phone design is 390 CSS px wide; the app's canvas is 1080.
    const float CssPx = 1080f / 390f;
    // The scene's own frame, and the pins' scale inside it (RoleStage.astro).
    const float SceneWidth = 480f;
    const float PinScale = 1.0415f;
    // The trail is revealed as a 500-unit dash over a path about 330 units long.
    const float TrailDash = 500f;
    const float TrailLength = 330f;

    // role.ts stageFor, in Keys order: the three signs' glyphs and the moment's.
    static readonly string[,] SignGlyphs =
    {
        { "route", "cols", "ar" },
        { "book", "team", "chart" },
        { "house", "ar", "camera" },
        { "sliders", "route", "swap" },
        { "map", "key", "camera" },
    };
    static readonly string[] MomentGlyphs = { "pin", "plus", "check", "pin", "arrow" };

    /// <summary>Every icon name the stage asks for: icon-&lt;name&gt;.png.</summary>
    public static readonly string[] IconNames =
    {
        "book", "team", "chart", "route", "cols", "ar", "house", "camera", "map", "key",
        "sliders", "swap", "plus", "pin", "check", "arrow",
        "teacher", "guide", "broker", "other", "viewer", "pencil", "ask",
    };

    string _role;
    string _work;
    string _words = "none";
    string _art;
    int _artIndex = -1;

    bool _previewing;
    int _previewAt;
    float _previewSince;

    bool _painted;
    float _loopStart;
    float _fadeStart = float.NegativeInfinity;
    float _skyStart = float.NegativeInfinity;
    Color _skyFrom, _skyTo;

    bool _cached;
    Dictionary<string, Sprite> _icons;
    Vector2[] _pinBase;
    CanvasGroup[] _pinGroups;
    Vector2 _cloudBase, _momentBase;
    Image _cloudImage;
    Image[] _signBacks;

    static float Now { get { return Time.unscaledTime; } }

    void Awake()
    {
        Cache();
    }

    void OnEnable()
    {
        Cache();
        ThemeManager.OnThemeChanged += Repaint;
        AppStrings.OnLanguageChanged += Repaint;

        // Opening the stage starts its loop afresh, with no fade or ease.
        _painted = false;
        _previewSince = Now;
        if (_art == null)
            Show(_role, _work);
        else
        {
            _loopStart = Now;
            Repaint();
        }
        _painted = true;
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Repaint;
        AppStrings.OnLanguageChanged -= Repaint;
        _painted = false;
    }

    /// <summary>
    /// The answer so far. A viewer's work is ignored, as on the website. With
    /// nothing chosen, or a creator with no work, the drawings walk through the
    /// roles; a full answer stops the walk on its own drawing.
    /// </summary>
    public void Show(string role, string work)
    {
        Cache();
        _role = AccountRole.IsValid(role) ? role : null;
        _work = _role == AccountRole.Creator && LineOfWork.IsValid(work) ? work : null;
        _words = StageKey(_role, _work);

        if (_words == "none")
        {
            if (!_previewing || _art == null)
            {
                _previewing = true;
                _previewSince = Now;
                SetArt(PreviewOrder[_previewAt]);
            }
        }
        else
        {
            _previewing = false;
            SetArt(_words);
        }

        PaintWords();
        PaintTheme();
    }

    /// <summary>
    /// The website's stageFor key: viewer, a creator's line of work, or "none"
    /// for nobody yet and for a creator who has not said what they do.
    /// </summary>
    public static string StageKey(string role, string work)
    {
        if (role == AccountRole.Viewer)
            return "viewer";

        if (role == AccountRole.Creator && LineOfWork.IsValid(work))
            return work;

        return "none";
    }

    /// <summary>The index into the scene sprites: guide 0, teacher 1, broker 2, other 3, viewer 4; -1 otherwise.</summary>
    public static int KeyIndex(string key)
    {
        return key == null ? -1 : Array.IndexOf(Keys, key);
    }

    /// <summary>role.ts SKIES, day and night.</summary>
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

    /// <summary>The glyph of a stage's sign (0-2), or null.</summary>
    public static string SignGlyph(string key, int sign)
    {
        int i = KeyIndex(key);
        return i < 0 || sign < 0 || sign > 2 ? null : SignGlyphs[i, sign];
    }

    /// <summary>The glyph of a stage's moment, or null.</summary>
    public static string MomentGlyph(string key)
    {
        int i = KeyIndex(key);
        return i < 0 ? null : MomentGlyphs[i];
    }

    /// <summary>
    /// role.ts sealState's glyph: the eye for a viewer, the job for a creator
    /// with one, a pencil for a creator without, and a question mark before any answer.
    /// </summary>
    public static string SealGlyph(string role, string work)
    {
        if (role == AccountRole.Viewer)
            return "viewer";

        if (role == AccountRole.Creator)
            return LineOfWork.IsValid(work) ? work : "pencil";

        return "ask";
    }

    void Cache()
    {
        if (_cached)
            return;
        _cached = true;

        _icons = new Dictionary<string, Sprite>();
        int count = Mathf.Min(icons.Length, iconNames.Length);
        for (int i = 0; i < count; i++)
        {
            if (icons[i] != null && !string.IsNullOrEmpty(iconNames[i]))
                _icons[iconNames[i]] = icons[i];
        }

        _pinBase = new Vector2[pins.Length];
        _pinGroups = new CanvasGroup[pins.Length];
        for (int i = 0; i < pins.Length; i++)
        {
            if (pins[i] == null)
                continue;
            _pinBase[i] = pins[i].anchoredPosition;
            _pinGroups[i] = pins[i].GetComponent<CanvasGroup>();
            if (_pinGroups[i] == null)
                _pinGroups[i] = pins[i].gameObject.AddComponent<CanvasGroup>();
        }

        if (cloud != null)
        {
            _cloudBase = cloud.anchoredPosition;
            _cloudImage = cloud.GetComponentInChildren<Image>(true);
        }

        if (moment != null)
            _momentBase = moment.anchoredPosition;

        _signBacks = new Image[signPills.Length];
        for (int i = 0; i < signPills.Length; i++)
            _signBacks[i] = signPills[i] != null ? signPills[i].GetComponent<Image>() : null;
    }

    void SetArt(string key)
    {
        if (key == _art)
            return;

        _art = key;
        _artIndex = KeyIndex(key);
        _loopStart = Now;

        if (_painted)
            _fadeStart = Now;

        PaintArt();
    }

    void Repaint()
    {
        PaintWords();
        PaintArt();
        PaintTheme();
    }

    /// <summary>The chip, headline and signs, which follow the answer.</summary>
    void PaintWords()
    {
        if (chipLabel != null)
        {
            // A creator with no line of work yet is "Създател", not "Нов профил".
            chipLabel.text = _words == "none" && _role == AccountRole.Creator
                ? AppStrings.Get("stage.none.tagCreator")
                : AppStrings.Get("stage." + _words + ".tag");
        }

        if (headLabel != null)
            headLabel.text = AppStrings.Get("stage." + _words + ".head");

        if (chipGlyph != null)
            SetIcon(chipGlyph, SealGlyph(_role, _work));

        bool signs = _words != "none";
        if (signsRow != null)
            signsRow.gameObject.SetActive(signs);

        for (int i = 0; i < 3; i++)
        {
            if (i < signPills.Length && signPills[i] != null)
                signPills[i].SetActive(signs);
            if (!signs)
                continue;
            if (i < signLabels.Length && signLabels[i] != null)
                signLabels[i].text = AppStrings.Get("stage." + _words + ".sign" + (i + 1));
            if (i < signIcons.Length && signIcons[i] != null)
                SetIcon(signIcons[i], SignGlyph(_words, i));
        }
    }

    /// <summary>The scene and moment, which follow the drawings' key.</summary>
    void PaintArt()
    {
        bool night = ThemeManager.IsDark;
        bool drawn = _artIndex >= 0;

        if (scene != null)
        {
            Sprite[] set = night ? sceneNight : sceneDay;
            Sprite sprite = drawn && set != null && _artIndex < set.Length ? set[_artIndex] : null;
            scene.sprite = sprite;
            scene.enabled = sprite != null;
        }

        if (momentLabel != null)
            momentLabel.text = drawn ? AppStrings.Get("stage." + _art + ".moment") : "";

        if (momentIcon != null)
            SetIcon(momentIcon, MomentGlyph(_art));

        SkyTo(Sky(_art, night));
    }

    /// <summary>Every colour that follows the theme (lib/stage.ts TEXT).</summary>
    void PaintTheme()
    {
        bool night = ThemeManager.IsDark;
        Color ink = Hex(night ? 0xf5f5f7 : 0x1d1d1f);
        Color muted = Hex(night ? 0xc2c2c8 : 0x45454b);
        Color chipBg = new Color(1f, 1f, 1f, night ? 0.10f : 0.8f);
        Color signInk = Hex(night ? 0x8fc4ff : 0x0066cc);

        if (sun != null)
        {
            sun.gameObject.SetActive(!night);
            sun.color = Hex(0xfff6d6);
        }

        if (moon != null)
        {
            moon.gameObject.SetActive(night);
            moon.color = Hex(0xf3eccb);
        }

        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] != null)
                stars[i].gameObject.SetActive(night);
        }

        if (_cloudImage != null)
            _cloudImage.color = new Color(1f, 1f, 1f, night ? 0.07f : 0.95f);

        if (chip != null)
            chip.color = chipBg;

        // Before an answer the seal is a bare muted sign; after it, blue with a
        // white glyph (global.css .seal[data-selected]).
        bool chosen = _role != null;
        if (chipSeal != null)
        {
            Sprite shape = _role == AccountRole.Creator ? sealSquare
                         : _role == AccountRole.Viewer ? sealRound : sealRing;
            if (shape != null)
                chipSeal.sprite = shape;
            chipSeal.color = chosen ? Hex(0x0066cc) : (sealRing != null ? muted : Color.clear);
        }
        if (chipGlyph != null)
            chipGlyph.color = chosen ? Color.white : muted;

        if (chipLabel != null)
            chipLabel.color = ink;
        if (headLabel != null)
            headLabel.color = ink;

        for (int i = 0; i < 3; i++)
        {
            if (i < _signBacks.Length && _signBacks[i] != null)
                _signBacks[i].color = chipBg;
            if (i < signIcons.Length && signIcons[i] != null)
                signIcons[i].color = signInk;
            if (i < signLabels.Length && signLabels[i] != null)
                signLabels[i].color = ink;
        }

        if (momentIcon != null)
            momentIcon.color = Color.white;
        if (momentLabel != null)
            momentLabel.color = Color.white;
    }

    void SkyTo(Color target)
    {
        if (sky == null)
            return;

        if (!_painted)
        {
            sky.color = target;
            _skyTo = target;
            _skyStart = float.NegativeInfinity;
            return;
        }

        _skyFrom = sky.color;
        _skyTo = target;
        _skyStart = Now;
    }

    void SetIcon(Image image, string name)
    {
        Sprite sprite;
        if (name != null && _icons != null && _icons.TryGetValue(name, out sprite))
        {
            image.sprite = sprite;
            image.enabled = true;
        }
        else
            image.enabled = false;
    }

    /// <summary>
    /// For the screenshot harness, where nothing runs: poses the loop as it is
    /// <paramref name="seconds"/> into the current drawing, then draws one frame.
    /// </summary>
    public void PoseAt(float seconds)
    {
        Cache();
        _painted = false;
        Repaint();
        _loopStart = Now - seconds;
        _fadeStart = float.NegativeInfinity;
        _skyStart = float.NegativeInfinity;
        Update();
    }

    void Update()
    {
        float now = Now;

        if (_previewing && now - _previewSince >= LoopSeconds)
        {
            _previewSince = now;
            _previewAt = (_previewAt + 1) % PreviewOrder.Length;
            SetArt(PreviewOrder[_previewAt]);
        }

        // The sky eases (CSS "ease") and the new scene fades in.
        if (sky != null && now - _skyStart <= ChangeSeconds)
            sky.color = Color.Lerp(_skyFrom, _skyTo, Ease(Mathf.Clamp01((now - _skyStart) / ChangeSeconds)));
        else if (sky != null && sky.color != _skyTo)
            sky.color = _skyTo;

        if (sceneGroup != null)
            sceneGroup.alpha = Ease(Mathf.Clamp01((now - _fadeStart) / ChangeSeconds));

        bool drawn = _artIndex >= 0;
        float scale = scene != null && scene.rectTransform.rect.width > 0f
            ? scene.rectTransform.rect.width / SceneWidth
            : 1080f / SceneWidth;
        float loop = now - _loopStart;

        // stage-p5draw: hidden to 30%, fully drawn at 70%.
        if (trail != null)
            trail.fillAmount = drawn ? Draw(Phase(loop, LoopSeconds)) : 0f;

        // stage-p5drop: the second pin 0.6 s after the first.
        for (int i = 0; i < pins.Length; i++)
        {
            if (pins[i] == null)
                continue;
            float y, alpha;
            Drop(Phase(loop - i * PinStagger, LoopSeconds), out y, out alpha);
            pins[i].anchoredPosition = _pinBase[i] + new Vector2(0f, -y * PinScale * scale);
            _pinGroups[i].alpha = drawn ? alpha : 0f;
        }

        // stage-p5pop.
        if (moment != null)
        {
            float y, alpha;
            Pop(Phase(loop, LoopSeconds), out y, out alpha);
            moment.anchoredPosition = _momentBase + new Vector2(0f, -y * CssPx);
            if (momentGroup != null)
                momentGroup.alpha = drawn ? alpha : 0f;
        }

        // stage-p5drift: ±20 px over 14 s, back and forth.
        if (cloud != null)
        {
            int leg = Mathf.FloorToInt(now / CloudSeconds);
            float u = EaseInOut(now / CloudSeconds - leg);
            if ((leg & 1) == 1)
                u = 1f - u;
            cloud.anchoredPosition = _cloudBase + new Vector2(Mathf.Lerp(-20f, 20f, u) * CssPx, 0f);
        }

        // stage-a5twinkle, at night only.
        if (ThemeManager.IsDark)
        {
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null)
                    continue;
                float delay = StarDelays[i % StarDelays.Length];
                stars[i].color = new Color(1f, 1f, 1f, Twinkle(Phase(now - delay, TwinkleSeconds)));
            }
        }
    }

    // ------------------------------------------------------------ keyframes

    /// <summary>Where a looping animation is, 0-1. Before its delay it waits at 0.</summary>
    static float Phase(float t, float period)
    {
        if (t <= 0f)
            return 0f;
        return (t % period) / period;
    }

    /// <summary>
    /// stage-p5drop: y in the keyframes' px, negative up; hidden to 8%, falls
    /// in by 20%, bounces 4 px at 24%, rests to 90%, fades out by 100%.
    /// </summary>
    public static void Drop(float p, out float y, out float alpha)
    {
        if (p < 0.08f) { y = -30f; alpha = 0f; }
        else if (p < 0.20f) { float e = EaseOut(Segment(p, 0.08f, 0.20f)); y = Mathf.Lerp(-30f, 0f, e); alpha = e; }
        else if (p < 0.24f) { y = Mathf.Lerp(0f, -4f, EaseOut(Segment(p, 0.20f, 0.24f))); alpha = 1f; }
        else if (p < 0.28f) { y = Mathf.Lerp(-4f, 0f, EaseOut(Segment(p, 0.24f, 0.28f))); alpha = 1f; }
        else if (p < 0.90f) { y = 0f; alpha = 1f; }
        else { y = 0f; alpha = 1f - EaseOut(Segment(p, 0.90f, 1f)); }
    }

    /// <summary>stage-p5draw as a fill amount: the 500-unit dash over the path.</summary>
    public static float Draw(float p)
    {
        if (p < 0.30f)
            return 0f;
        if (p >= 0.70f)
            return 1f;
        float shown = TrailDash * EaseOut(Segment(p, 0.30f, 0.70f));
        return Mathf.Min(1f, shown / TrailLength);
    }

    /// <summary>
    /// stage-p5pop: y in px, positive down; hidden to 40%, rises in by 52%,
    /// shown to 92%, fades out by 100%.
    /// </summary>
    public static void Pop(float p, out float y, out float alpha)
    {
        if (p < 0.40f) { y = 8f; alpha = 0f; }
        else if (p < 0.52f) { float e = EaseOut(Segment(p, 0.40f, 0.52f)); y = Mathf.Lerp(8f, 0f, e); alpha = e; }
        else if (p < 0.92f) { y = 0f; alpha = 1f; }
        else { y = 0f; alpha = 1f - EaseOut(Segment(p, 0.92f, 1f)); }
    }

    /// <summary>stage-a5twinkle: .35 to 1 and back, ease-in-out each way.</summary>
    public static float Twinkle(float p)
    {
        return p < 0.5f
            ? Mathf.Lerp(0.35f, 1f, EaseInOut(p * 2f))
            : Mathf.Lerp(1f, 0.35f, EaseInOut((p - 0.5f) * 2f));
    }

    static float Segment(float p, float from, float to)
    {
        return Mathf.Clamp01((p - from) / (to - from));
    }

    // CSS timing functions.
    static float Ease(float x) { return Bezier(0.25f, 0.1f, 0.25f, 1f, x); }
    static float EaseOut(float x) { return Bezier(0f, 0f, 0.58f, 1f, x); }
    static float EaseInOut(float x) { return Bezier(0.42f, 0f, 0.58f, 1f, x); }

    /// <summary>A CSS cubic-bezier(x1, y1, x2, y2) at x, solved by bisection.</summary>
    static float Bezier(float x1, float y1, float x2, float y2, float x)
    {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;

        float lo = 0f, hi = 1f, t = x;
        for (int i = 0; i < 20; i++)
        {
            if (Curve(x1, x2, t) < x)
                lo = t;
            else
                hi = t;
            t = (lo + hi) * 0.5f;
        }
        return Curve(y1, y2, t);
    }

    static float Curve(float p1, float p2, float t)
    {
        float u = 1f - t;
        return 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t;
    }

    static Color Hex(int rgb)
    {
        return new Color32((byte)((rgb >> 16) & 0xff), (byte)((rgb >> 8) & 0xff), (byte)(rgb & 0xff), 255);
    }
}
