using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The four lines of work as a radio list, in LineOfWork.Keys order: broker,
/// teacher, guide, other. Nothing is chosen until somebody chooses, because a
/// pre-selected answer is an answer nobody gave.
///
/// The same list sits in the sign-up form and in the sheet Settings and the
/// once-after-sign-in question open; this class only holds and paints the
/// choice, and whoever owns it decides what saving means.
/// </summary>
public class WorkChoiceView : MonoBehaviour
{
    [Tooltip("One per LineOfWork.Keys entry, in that order.")]
    public Button[] options = new Button[0];
    public Image[] radios = new Image[0];
    public RectTransform[] holes = new RectTransform[0];
    public Image[] edges = new Image[0];

    /// <summary>The chosen key, or null.</summary>
    public string Selected { get; private set; }

    /// <summary>Raised when the reader picks one.</summary>
    public event System.Action OnChosen;

    bool _wired;

    void Awake()
    {
        Wire();
    }

    void Wire()
    {
        if (_wired)
            return;

        _wired = true;

        for (int i = 0; i < options.Length && i < LineOfWork.Keys.Length; i++)
        {
            if (options[i] == null)
                continue;

            string key = LineOfWork.Keys[i];
            options[i].onClick.AddListener(() => Pick(key));
        }
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += Paint;
        Paint();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Paint;
    }

    void Pick(string key)
    {
        Haptics.Tick();
        Select(key);

        if (OnChosen != null)
            OnChosen();
    }

    /// <summary>Shows a choice without anybody having tapped it; null clears.</summary>
    public void Select(string key)
    {
        Selected = LineOfWork.IsValid(key) ? key : null;
        Paint();
    }

    public void Paint()
    {
        for (int i = 0; i < LineOfWork.Keys.Length; i++)
        {
            bool on = LineOfWork.Keys[i] == Selected;
            Color accent = ThemeManager.Get(ThemeRole.Accent);

            if (i < radios.Length && radios[i] != null)
                radios[i].color = on ? accent : ThemeManager.Get(ThemeRole.FillStrong);

            if (i < holes.Length && holes[i] != null)
                holes[i].sizeDelta = on ? new Vector2(22f, 22f) : new Vector2(50f, 50f);

            if (i < edges.Length && edges[i] != null)
                edges[i].color = on ? accent : ThemeManager.Get(ThemeRole.Line);
        }
    }
}
