using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The tab bar from Daylight v2: one floating glass capsule, three tabs, and a
/// clear lens under the open one.
///
/// Tap a tab and the lens springs across to it. Press and slide along the bar
/// and the lens follows the finger, stretching with its speed and swelling a
/// little while held; let go and it snaps to the nearest tab, carrying the
/// speed of the flick into the spring. The phone ticks as the lens passes each
/// tab. Over the camera the glass turns dark, by how close the lens is to it.
///
/// The screen changes when the finger lifts, not while it is dragging: sliding
/// across Camera on the way to Profile would otherwise start the AR session and
/// stop it again under the reader's thumb, which is the most expensive thing in
/// the app done twice for nothing.
///
/// The whole bar is one target. Each tab is drawn here but is not a Button of
/// its own: a Button takes the press, and a press the bar never sees is a drag
/// it cannot follow.
/// </summary>
public class GlassTabBar : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                           IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [System.Serializable]
    public class Tab
    {
        public RectTransform root;
        public Graphic[] icon;
        public TMP_Text label;
    }

    [Header("References")]
    public TabNavigationController navigation;
    public RectTransform bar;
    public RectTransform lens;
    public Image glass;
    public Image rim;
    public Image lensFill;
    public Image lensRim;
    public Tab[] tabs = new Tab[3];

    [Header("Geometry")]
    [Tooltip("Inset of the lens from the bar's edge, in reference pixels.")]
    public float padding = 14f;

    float _p;          // where the lens is, in tabs: 0, 1, 2 and everything between
    float _v;          // how fast, in tabs a second
    float _lift;       // 0 at rest, 1 while held
    float _liftV;
    int _target;
    int _active = -1;
    bool _held;
    bool _dragging;
    bool _springing;
    int _lastTick;
    float _pressX;
    float _lastX;
    float _lastT;
    float _scale = 1f;

    // Spring constants, as the prototype's lens: under-damped, so it lands with
    // a little give. Everything else in the app arrives without one.
    const float K = 340f, C = 26f;
    const float LiftK = 520f, LiftC = 26f;

    float TabWidth
    {
        get
        {
            float width = bar != null ? bar.rect.width : 0f;
            return Mathf.Max(1f, (width - padding * 2f) / tabs.Length);
        }
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += Paint;

        // Back from behind a door or a sheet: rise in rather than appear.
        if (bar != null && Application.isPlaying)
        {
            _rise = 1f;
        }

        Paint();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Paint;
        _held = false;
        _dragging = false;
    }

    float _rise;
    float _riseV;

    /// <summary>Moves the lens to a tab without opening it. The controller calls this.</summary>
    public void ShowActive(int index, bool animate)
    {
        index = Mathf.Clamp(index, 0, tabs.Length - 1);
        _active = index;
        _target = index;

        if (!animate || !isActiveAndEnabled || !Application.isPlaying)
        {
            _p = index;
            _v = 0f;
            _springing = false;
            Paint();
            return;
        }

        _springing = true;
    }

    // --------------------------------------------------------------- input

    public void OnPointerDown(PointerEventData eventData)
    {
        _held = true;
        _dragging = false;
        _springing = false;
        _pressX = LocalX(eventData);
        _lastX = _pressX;
        _lastT = Time.unscaledTime;
        _lastTick = Mathf.RoundToInt(_p);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        float x = LocalX(eventData);
        float p = (x - padding - TabWidth * 0.5f) / TabWidth;

        // Past either end it gives, more and more slowly, and comes back on release.
        if (p < 0f)
            p = -0.3f * (1f - Mathf.Exp(p * 2.5f));
        else if (p > tabs.Length - 1)
            p = (tabs.Length - 1) + 0.3f * (1f - Mathf.Exp(-(p - (tabs.Length - 1)) * 2.5f));

        float now = Time.unscaledTime;
        float dt = Mathf.Max(0.016f, now - _lastT);
        float speed = (x - _lastX) / TabWidth / dt;
        _v = Mathf.Lerp(_v, speed, 0.5f);
        _lastX = x;
        _lastT = now;

        _p = p;

        int near = Mathf.Clamp(Mathf.RoundToInt(_p), 0, tabs.Length - 1);
        if (near != _lastTick)
        {
            _lastTick = near;
            Haptics.Tick();
        }

        Paint();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_held)
            return;

        _held = false;

        int to;

        if (!_dragging)
        {
            // A tap: the tab under the finger.
            to = Mathf.Clamp(Mathf.FloorToInt((LocalX(eventData) - padding) / TabWidth), 0, tabs.Length - 1);
            _v = 0f;
        }
        else
        {
            // A flick goes a little further than where it was let go.
            to = Mathf.Clamp(Mathf.RoundToInt(_p + Mathf.Clamp(_v * 0.08f, -0.6f, 0.6f)), 0, tabs.Length - 1);
        }

        _dragging = false;
        _target = to;
        _springing = true;

        if (to != _active)
        {
            Haptics.Tick();
            Open(to);
        }
    }

    void Open(int index)
    {
        _active = index;

        if (navigation == null)
            return;

        if (index == 0)
            navigation.ShowMap();
        else if (index == 1)
            navigation.ShowCamera();
        else
            navigation.ShowSettings();
    }

    float LocalX(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            bar, eventData.position, eventData.pressEventCamera, out Vector2 local);

        // From the bar's left edge, whatever its pivot.
        return local.x + bar.rect.width * bar.pivot.x;
    }

    // -------------------------------------------------------------- motion

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        bool changed = false;

        if (_springing && !_held)
        {
            Step(ref _p, ref _v, _target, K, C, dt);

            if (Mathf.Abs(_p - _target) < 0.0006f && Mathf.Abs(_v) < 0.006f)
            {
                _p = _target;
                _v = 0f;
                _springing = false;
            }

            changed = true;
        }

        float liftGoal = _held ? 1f : 0f;
        if (Mathf.Abs(_lift - liftGoal) > 0.001f || Mathf.Abs(_liftV) > 0.001f)
        {
            Step(ref _lift, ref _liftV, liftGoal, LiftK, LiftC, dt);
            changed = true;
        }

        if (_rise > 0.0005f || Mathf.Abs(_riseV) > 0.001f)
        {
            Step(ref _rise, ref _riseV, 0f, 420f, 41f, dt);

            if (_rise < 0.0005f && Mathf.Abs(_riseV) < 0.001f)
            {
                _rise = 0f;
                _riseV = 0f;
            }

            changed = true;
        }

        if (changed)
            Paint();
    }

    static void Step(ref float x, ref float v, float goal, float k, float c, float dt)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f));
        float h = dt / steps;

        for (int i = 0; i < steps; i++)
        {
            float a = -k * (x - goal) - c * v;
            v += a * h;
            x += v * h;
        }
    }

    // -------------------------------------------------------------- drawing

    void Paint()
    {
        if (bar == null)
            return;

        float tw = TabWidth;

        if (lens != null)
        {
            // Stretched along the way it is going, squashed across it, swollen a
            // touch while held - the liquid part of liquid glass.
            float stretch = Mathf.Min(Mathf.Abs(_v * tw) / 7200f, 0.2f);
            float sx = 1f + stretch + _lift * 0.1f;
            float sy = 1f - stretch * 0.4f + _lift * 0.1f;

            lens.sizeDelta = new Vector2(tw, lens.sizeDelta.y);
            lens.anchoredPosition = new Vector2(padding + _p * tw + tw * 0.5f, 0f);
            lens.localScale = new Vector3(sx, sy, 1f);
        }

        // How far into the camera the lens is: 1 on the camera tab, 0 a tab away.
        // Only in dark mode: in light mode the camera keeps the light bar, as
        // its card does - the bar used to go dark there and the whole tab read
        // as dark mode (feedback, 6 Nov 2026).
        bool night = ThemeManager.IsDark;
        float dark = night ? Mathf.Max(0f, 1f - Mathf.Abs(_p - 1f)) : 0f;

        Color darkGlass = new Color(30f / 255f, 30f / 255f, 33f / 255f, 0.72f);
        Color darkRim = new Color(1f, 1f, 1f, 0.12f);

        if (glass != null)
            glass.color = Color.Lerp(ThemeManager.Get(ThemeRole.Glass), darkGlass, dark);

        if (rim != null)
            rim.color = Color.Lerp(ThemeManager.Get(ThemeRole.GlassRim), darkRim, dark);

        Color lensLight = night ? new Color(1f, 1f, 1f, 0.14f) : new Color(1f, 1f, 1f, 0.96f);
        Color lensDark = new Color(1f, 1f, 1f, 0.14f);

        if (lensFill != null)
            lensFill.color = Color.Lerp(lensLight, lensDark, dark);

        if (lensRim != null)
        {
            Color rimLight = night ? new Color(1f, 1f, 1f, 0.26f) : new Color(0f, 0f, 0f, 0.07f);
            lensRim.color = Color.Lerp(rimLight, new Color(1f, 1f, 1f, 0.26f), dark);
        }

        Color muted = Color.Lerp(ThemeManager.Get(ThemeRole.TextMuted), new Color(0.682f, 0.682f, 0.698f), dark);
        Color accent = Color.Lerp(ThemeManager.Get(ThemeRole.AccentText), new Color(0.161f, 0.592f, 1f), dark);

        for (int i = 0; i < tabs.Length; i++)
        {
            Tab tab = tabs[i];
            if (tab == null)
                continue;

            float near = Mathf.Max(0f, 1f - Mathf.Abs(_p - i));
            Color colour = Color.Lerp(muted, accent, near);

            if (tab.label != null)
                tab.label.color = colour;

            if (tab.icon != null)
            {
                for (int g = 0; g < tab.icon.Length; g++)
                {
                    if (tab.icon[g] != null)
                        tab.icon[g].color = colour;
                }
            }

            if (tab.root != null)
            {
                float mag = 1f + (0.06f + _lift * 0.12f) * near;
                tab.root.localScale = new Vector3(mag, mag, 1f);
            }
        }

        // The rise after a sheet or a door: from just below its place.
        if (!float.IsNaN(_restY))
        {
            Vector2 at = bar.anchoredPosition;
            at.y = _restY - _rise * 60f;
            bar.anchoredPosition = at;
        }
    }

    float _restY = float.NaN;

    void Awake()
    {
        if (bar != null)
            _restY = bar.anchoredPosition.y;
    }

    void Start()
    {
        if (float.IsNaN(_restY) && bar != null)
            _restY = bar.anchoredPosition.y;

        if (_active < 0)
            ShowActive(0, false);
    }
}
