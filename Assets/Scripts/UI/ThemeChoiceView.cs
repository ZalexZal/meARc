using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Theme as three choices - like the phone, light, dark - in one segmented
/// control. "Like the phone" is first because it is what almost everyone wants
/// and the only one that follows the phone into the evening.
///
/// One white thumb marks the choice and slides to the next one, the way the
/// phone's own segmented controls do; it can be tapped across or pushed across
/// with a finger, and lands on whichever choice it is let go nearest.
/// </summary>
public class ThemeChoiceView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Button[] buttons = new Button[3];
    public Image[] faces = new Image[3];
    public TextMeshProUGUI[] labels = new TextMeshProUGUI[3];

    [Tooltip("The white thumb under the chosen label. Without one, each choice " +
             "lights its own face instead.")]
    public RectTransform thumb;
    public Image thumbImage;

    [Tooltip("Seconds the thumb takes to slide, roughly.")]
    public float slideTime = 0.09f;

    static readonly ThemeManager.Mode[] Modes =
    {
        ThemeManager.Mode.System, ThemeManager.Mode.Light, ThemeManager.Mode.Dark
    };

    static readonly string[] Keys = { "theme.system", "theme.light", "theme.dark" };

    float _pos = -1f;
    float _speed;
    float _dragStart;
    bool _dragging;

    void Awake()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            if (buttons[i] != null)
                buttons[i].onClick.AddListener(() => Choose(index));
        }
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += Paint;
        AppStrings.OnLanguageChanged += Paint;
        _pos = -1f;
        Paint();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Paint;
        AppStrings.OnLanguageChanged -= Paint;
        _dragging = false;
    }

    void Choose(int index)
    {
        if (Modes[index] != ThemeManager.CurrentMode)
            Haptics.Tick();

        ThemeManager.SetMode(Modes[index]);
        Paint();
    }

    int Current
    {
        get
        {
            int at = System.Array.IndexOf(Modes, ThemeManager.CurrentMode);
            return at < 0 ? 0 : at;
        }
    }

    public void Paint()
    {
        int chosen = _dragging ? Mathf.RoundToInt(_pos) : Current;

        for (int i = 0; i < Modes.Length; i++)
        {
            bool on = i == chosen;

            if (i < faces.Length && faces[i] != null)
            {
                // Still the button's target, so still drawn - just clear when the
                // thumb is doing the marking.
                Color face = ThemeManager.Get(ThemeRole.Surface);
                face.a = thumb == null && on ? 1f : 0f;
                faces[i].color = face;
            }

            if (i < labels.Length && labels[i] != null)
            {
                labels[i].text = AppStrings.Get(Keys[i]);
                labels[i].color = ThemeManager.Get(ThemeRole.TextPrimary);
                labels[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        if (thumbImage != null)
            thumbImage.color = ThemeManager.Get(ThemeRole.Surface);

        // First paint, before any slide: straight there.
        if (_pos < 0f)
        {
            _pos = Current;
            Place();
        }
    }

    void Update()
    {
        if (thumb == null || _dragging)
            return;

        float target = Current;

        if (Mathf.Approximately(_pos, target))
        {
            // Layout can move the choices after the first paint.
            Place();
            return;
        }

        _pos = Mathf.SmoothDamp(_pos, target, ref _speed, slideTime, Mathf.Infinity, Time.unscaledDeltaTime);

        if (Mathf.Abs(_pos - target) < 0.004f)
        {
            _pos = target;
            _speed = 0f;
        }

        Place();
    }

    /// <summary>Where choice i starts, in this control's space, and how big it is.</summary>
    bool Slot(int i, out float left, out Vector2 size, out float y)
    {
        left = 0f;
        size = Vector2.zero;
        y = 0f;

        if (i < 0 || i >= buttons.Length || buttons[i] == null)
            return false;

        RectTransform b = (RectTransform)buttons[i].transform;
        Rect r = b.rect;
        left = b.localPosition.x - r.width * b.pivot.x;
        size = r.size;
        y = b.localPosition.y + (0.5f - b.pivot.y) * r.height;
        return true;
    }

    void Place()
    {
        if (thumb == null)
            return;

        int a = Mathf.Clamp(Mathf.FloorToInt(_pos), 0, buttons.Length - 1);
        int b = Mathf.Min(a + 1, buttons.Length - 1);
        float t = _pos - a;

        if (!Slot(a, out float leftA, out Vector2 sizeA, out float yA) ||
            !Slot(b, out float leftB, out Vector2 sizeB, out float yB))
        {
            return;
        }

        thumb.pivot = new Vector2(0f, 0.5f);
        thumb.sizeDelta = Vector2.Lerp(sizeA, sizeB, t);
        thumb.localPosition = new Vector3(Mathf.Lerp(leftA, leftB, t), Mathf.Lerp(yA, yB, t), 0f);
    }

    // ------------------------------------------------------------- the finger

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (thumb == null || !SideDrag.Claim(this, eventData))
            return;

        _dragging = true;
        _dragStart = _pos;
        _speed = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        // One choice's width is one step.
        float step = Slot(0, out float left0, out _, out _) && Slot(1, out float left1, out _, out _)
            ? left1 - left0
            : 1f;

        int before = Mathf.RoundToInt(_pos);
        _pos = Mathf.Clamp(_dragStart + SideDrag.Moved(transform, eventData) / Mathf.Max(1f, step),
                           0f, buttons.Length - 1);
        Place();

        if (Mathf.RoundToInt(_pos) != before)
            Paint();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        _dragging = false;
        Choose(Mathf.Clamp(Mathf.RoundToInt(_pos), 0, buttons.Length - 1));
    }
}
