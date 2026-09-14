using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// All eleven badges, each with the rule that earns it.
///
/// The rule is always shown, earned or not. A badge that does not say how it is
/// earned is a lottery ticket, and a school group will spend the walk asking why
/// they did not get one.
/// </summary>
public class BadgesPageView : MonoBehaviour
{
    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI countLabel;

    [Tooltip("One row per badge, in Badges.Keys order.")]
    public Image[] rings = new Image[0];
    public Image[] icons = new Image[0];
    public TextMeshProUGUI[] titles = new TextMeshProUGUI[0];
    public TextMeshProUGUI[] rules = new TextMeshProUGUI[0];
    public TextMeshProUGUI[] progress = new TextMeshProUGUI[0];

    void OnEnable()
    {
        ProfileService.OnChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        ThemeManager.OnThemeChanged += Refresh;

        if (ProfileService.Instance != null)
            ProfileService.Instance.Load();

        Refresh();
    }

    void OnDisable()
    {
        ProfileService.OnChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        ThemeManager.OnThemeChanged -= Refresh;
    }

    public void Refresh()
    {
        Render(ProfileService.Instance != null ? ProfileService.Instance.Latest : null);
    }

    /// <summary>Draws a given profile; the preview harness passes a sample.</summary>
    public void Render(ProfileService.ProfileData data)
    {

        if (titleLabel != null)
            titleLabel.text = AppStrings.Get("profile.badges");

        if (countLabel != null)
        {
            countLabel.text = data != null
                ? string.Format(AppStrings.Get("profile.badgeCount"), data.Earned, Badges.Keys.Length)
                : AppStrings.Get("profile.badgesLater");
        }

        for (int i = 0; i < Badges.Keys.Length; i++)
        {
            string key = Badges.Keys[i];
            ProfileService.BadgeRow row = Find(data, key);
            bool earned = row != null && row.earned;
            int tier = row != null ? row.tier : 0;
            Color colour = Badges.Colour(key, earned, tier);

            if (i < rings.Length && rings[i] != null)
                rings[i].color = Badges.Wash(colour, earned);

            if (i < icons.Length && icons[i] != null)
                icons[i].color = colour;

            if (i < titles.Length && titles[i] != null)
            {
                titles[i].text = Badges.Name(key);
                titles[i].color = ThemeManager.Get(earned ? ThemeRole.TextPrimary : ThemeRole.TextSecondary);
            }

            if (i < rules.Length && rules[i] != null)
                rules[i].text = Badges.Rule(key);

            if (i < progress.Length && progress[i] != null)
            {
                string line = Badges.Progress(row);
                progress[i].gameObject.SetActive(!string.IsNullOrEmpty(line));
                progress[i].text = line;
            }
        }
    }

    static ProfileService.BadgeRow Find(ProfileService.ProfileData data, string key)
    {
        if (data == null || data.badges == null)
            return null;

        for (int i = 0; i < data.badges.Length; i++)
            if (data.badges[i] != null && data.badges[i].key == key)
                return data.badges[i];

        return null;
    }
}
