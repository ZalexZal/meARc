using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "How old are you?", on the terms screen: 14 or older, 13, or 12 or younger.
///
/// Three pills rather than a date picker. Only the answer is kept, and asking
/// for a birthday to throw most of it away is collecting data for the sake of
/// it. Nothing is chosen at first - a preselected answer is one nobody gave -
/// and Accept will not go through until one is.
///
/// Why three: at 14 the law lets somebody agree for themselves; at 13 a parent
/// has to agree (Bulgaria, GDPR art. 8); and Google Play does not allow an app
/// that collects location to take in children under 13, which this app cannot
/// do without. So 12 and under is told so, and the terms cannot be accepted.
/// </summary>
public class AgeChoiceView : MonoBehaviour
{
    public Button olderButton;
    public Button thirteenButton;
    public Button youngerButton;
    public Image olderFill;
    public Image thirteenFill;
    public Image youngerFill;
    public TextMeshProUGUI olderLabel;
    public TextMeshProUGUI thirteenLabel;
    public TextMeshProUGUI youngerLabel;

    [Tooltip("Says so when Accept was pressed without an answer, or with too young a one.")]
    public TextMeshProUGUI nagLabel;

    const int None = 0, Older = 1, Thirteen = 2, Younger = 3;
    int _choice;

    /// <summary>Whether a parent has to agree: true at 13, false at 14 or older, null otherwise.</summary>
    public bool? Chosen
    {
        get { return _choice == Older ? false : _choice == Thirteen ? (bool?)true : null; }
    }

    /// <summary>12 or younger: nothing in the app is for them.</summary>
    public bool TooYoung { get { return _choice == Younger; } }

    void Awake()
    {
        if (olderButton != null) olderButton.onClick.AddListener(() => Choose(Older));
        if (thirteenButton != null) thirteenButton.onClick.AddListener(() => Choose(Thirteen));
        if (youngerButton != null) youngerButton.onClick.AddListener(() => Choose(Younger));
    }

    void OnEnable()
    {
        // The phone's earlier answer, when there is one, offered back rather
        // than asked from scratch - this screen comes back with new terms.
        TermsService terms = TermsService.Instance;
        bool? under14 = terms != null ? terms.Under14 : null;
        _choice = under14 == true ? Thirteen : under14 == false ? Older : None;

        Say(null);
        ThemeManager.OnThemeChanged += Draw;
        Draw();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Draw;
    }

    void Choose(int choice)
    {
        _choice = choice;
        Say(choice == Younger ? "age.tooYoung" : null);
        Draw();
    }

    public void Nag()
    {
        Say(TooYoung ? "age.tooYoung" : "age.nag");
    }

    void Say(string key)
    {
        if (nagLabel == null)
            return;

        nagLabel.gameObject.SetActive(!string.IsNullOrEmpty(key));
        nagLabel.text = string.IsNullOrEmpty(key) ? string.Empty : AppStrings.Get(key);
    }

    /// <summary>Public for the preview harness, where OnEnable never runs.</summary>
    public void Draw()
    {
        Paint(olderFill, olderLabel, _choice == Older);
        Paint(thirteenFill, thirteenLabel, _choice == Thirteen);
        Paint(youngerFill, youngerLabel, _choice == Younger);
    }

    static void Paint(Image fill, TextMeshProUGUI label, bool on)
    {
        if (fill != null)
            fill.color = ThemeManager.Get(on ? ThemeRole.Accent : ThemeRole.SurfaceAlt);

        if (label != null)
            label.color = ThemeManager.Get(on ? ThemeRole.OnAccent : ThemeRole.TextPrimary);
    }
}
