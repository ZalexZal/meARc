using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One badge, on a sheet of its own: the disc large, its name, what earns it,
/// and either that it has been earned or how far along it is.
///
///          ( ◎ )
///        Trailblazer
///   Finish 3, then 5, then 10 trails.
///      ▬▬▬▬▬▬▬▬▬▬▬▬▬───────
///        Level 1 · 2 / 3
///
/// What Daylight opens from a badge in the grid. The list of all eleven is
/// still a row away, under "All badges".
/// </summary>
public class BadgeSheetView : MonoBehaviour
{
    public Image ring;
    public Image edge;
    public Image icon;
    public TextMeshProUGUI title;
    public TextMeshProUGUI rule;
    public TextMeshProUGUI caption;

    [Tooltip("The progress bar, for a badge earned in steps.")]
    public GameObject barHolder;
    public RectTransform barFill;
    public Image barFillImage;

    [Tooltip("One per badge, in Badges.Keys order.")]
    public Sprite[] sprites = new Sprite[0];

    public void Open(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        ProfileService service = ProfileService.Instance;
        ProfileService.BadgeRow row = Find(service != null ? service.Latest : null, key);
        bool earned = row != null && row.earned;

        // As the grid draws it: one blue for earned, an outline for not yet.
        if (ring != null)
        {
            Color wash = ThemeManager.Get(ThemeRole.AccentTint);
            if (!earned)
                wash.a = 0f;
            ring.color = wash;
        }

        if (edge != null)
            edge.gameObject.SetActive(!earned);

        if (icon != null)
        {
            int index = Badges.IndexOf(key);
            if (index >= 0 && index < sprites.Length)
                icon.sprite = sprites[index];
            icon.color = ThemeManager.Get(earned ? ThemeRole.AccentText : ThemeRole.TextMuted);
        }

        if (title != null)
            title.text = Badges.Name(key);

        if (rule != null)
            rule.text = Badges.Rule(key);

        // A bar for anything still being worked towards in steps - including
        // Trailblazer's next level once its first is earned.
        bool stepped = row != null && row.target > 1 &&
                       (!earned || (key == "trailblazer" && row.tier < 3));

        if (barHolder != null)
            barHolder.SetActive(stepped);

        if (stepped && barFill != null)
        {
            float done = Mathf.Clamp01(row.progress / (float)Mathf.Max(1, row.target));
            barFill.anchorMax = new Vector2(done, 1f);
        }

        if (barFillImage != null)
            barFillImage.color = ThemeManager.Get(ThemeRole.Accent);

        if (caption != null)
        {
            string progress = Badges.Progress(row);
            caption.text = earned
                ? (string.IsNullOrEmpty(progress)
                    ? AppStrings.Get("badge.earned")
                    : AppStrings.Get("badge.earned") + " · " + progress)
                : (string.IsNullOrEmpty(progress) ? AppStrings.Get("badge.notYet") : progress);
        }

        gameObject.SetActive(true);
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    static ProfileService.BadgeRow Find(ProfileService.ProfileData data, string key)
    {
        if (data == null || data.badges == null)
            return null;

        for (int i = 0; i < data.badges.Length; i++)
        {
            if (data.badges[i] != null && data.badges[i].key == key)
                return data.badges[i];
        }

        return null;
    }
}
