using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The language page: Bulgarian or English as a two-row radio list, and the
/// name of the one in use on the Profile row that opens it.
/// </summary>
public class LanguageChoiceView : MonoBehaviour
{
    public Button bulgarian;
    public Image bulgarianRadio;
    public RectTransform bulgarianHole;
    public Image bulgarianEdge;

    public Button english;
    public Image englishRadio;
    public RectTransform englishHole;
    public Image englishEdge;

    [Tooltip("The value on the Profile row: the language in use, in its own words.")]
    public TextMeshProUGUI valueLabel;

    void Awake()
    {
        if (bulgarian != null)
            bulgarian.onClick.AddListener(() => Set(AppLanguage.Bulgarian));

        if (english != null)
            english.onClick.AddListener(() => Set(AppLanguage.English));
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Paint;
        ThemeManager.OnThemeChanged += Paint;
        Paint();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Paint;
        ThemeManager.OnThemeChanged -= Paint;
    }

    public void Open()
    {
        gameObject.SetActive(true);
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    /// <summary>The language in use, named in itself.</summary>
    public static string CurrentName
    {
        get { return AppStrings.CurrentLanguage == AppLanguage.Bulgarian ? "Български" : "English"; }
    }

    void Set(AppLanguage language)
    {
        Haptics.Tick();
        AppStrings.SetLanguage(language);
        Paint();
    }

    public void Paint()
    {
        bool bg = AppStrings.CurrentLanguage == AppLanguage.Bulgarian;
        Radio(bulgarianRadio, bulgarianHole, bulgarianEdge, bg);
        Radio(englishRadio, englishHole, englishEdge, !bg);

        if (valueLabel != null)
            valueLabel.text = CurrentName;
    }

    static void Radio(Image radio, RectTransform hole, Image edge, bool on)
    {
        Color accent = ThemeManager.Get(ThemeRole.Accent);

        if (radio != null)
            radio.color = on ? accent : ThemeManager.Get(ThemeRole.FillStrong);

        if (hole != null)
            hole.sizeDelta = on ? new Vector2(22f, 22f) : new Vector2(50f, 50f);

        if (edge != null)
            edge.color = on ? accent : ThemeManager.Get(ThemeRole.Line);
    }
}
