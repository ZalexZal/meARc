using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Creator or viewer, as two option cards: "I make trails" and "I explore and
/// walk", the website's sign-up question in the same words. Nothing is chosen
/// until somebody chooses.
///
/// What follows the answer is part of the same question, so it is switched
/// here: the four lines of work only for a creator, and for a viewer the note
/// that says why nobody is asking them what they do. Whoever owns the cards
/// decides what saving means, as with <see cref="WorkChoiceView"/>.
/// </summary>
public class RoleChoiceView : MonoBehaviour
{
    [Tooltip("Creator first, then viewer.")]
    public Button[] options = new Button[0];
    public Image[] radios = new Image[0];
    public RectTransform[] holes = new RectTransform[0];
    public Image[] edges = new Image[0];

    [Tooltip("The line of work: its question, its four rows and its error. On " +
             "screen only while creator is chosen.")]
    public GameObject workGroup;

    [Tooltip("role.viewerNote. On screen only while viewer is chosen.")]
    public GameObject viewerNote;

    /// <summary>The two answers, in the order the cards stand.</summary>
    public static readonly string[] Keys = { AccountRole.Creator, AccountRole.Viewer };

    /// <summary>"creator", "viewer", or null.</summary>
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

        for (int i = 0; i < options.Length && i < Keys.Length; i++)
        {
            if (options[i] == null)
                continue;

            string key = Keys[i];
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
        Selected = AccountRole.IsValid(key) ? key : null;
        Paint();
    }

    public void Paint()
    {
        Color accent = ThemeManager.Get(ThemeRole.Accent);

        for (int i = 0; i < Keys.Length; i++)
        {
            bool on = Keys[i] == Selected;

            if (i < radios.Length && radios[i] != null)
                radios[i].color = on ? accent : ThemeManager.Get(ThemeRole.FillStrong);

            if (i < holes.Length && holes[i] != null)
                holes[i].sizeDelta = on ? new Vector2(22f, 22f) : new Vector2(50f, 50f);

            if (i < edges.Length && edges[i] != null)
                edges[i].color = on ? accent : ThemeManager.Get(ThemeRole.Line);
        }

        if (workGroup != null)
            workGroup.SetActive(Selected == AccountRole.Creator);

        if (viewerNote != null)
            viewerNote.SetActive(Selected == AccountRole.Viewer);
    }
}
